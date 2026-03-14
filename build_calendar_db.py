#!/usr/bin/env python3
"""Prebuild calendar.db by scraping Hamro Patro calendar HTML month by month."""

from __future__ import annotations

import argparse
import html
import os
import re
import sqlite3
import time
import urllib.error
import urllib.request
from dataclasses import dataclass
from datetime import datetime, timezone
from pathlib import Path
from typing import Iterable

BASE_URL = "https://www.hamropatro.com/gui/home/calender-ajax.php?year={year}&month={month}"
REQUEST_GAP_SECONDS = 0.5
RETRY_DELAY_SECONDS = 2.0
HTTP_TIMEOUT_SECONDS = 30
USER_AGENT = "OpenPatroCalendarBuilder/1.0 (+https://www.hamropatro.com)"

TITLE_RE = re.compile(r"<span\s+class=\"newDateText\s+headderNew\">(.*?)</span>", re.DOTALL | re.IGNORECASE)
DATES_BLOCK_RE = re.compile(r"<ul\s+class=\"dates\s+clearfix\">(.*?)</ul>", re.DOTALL | re.IGNORECASE)
LI_BLOCK_RE = re.compile(r"<li\b[\s\S]*?</li>", re.DOTALL | re.IGNORECASE)
CLASS_RE = re.compile(r"\bclass=\"(.*?)\"", re.DOTALL | re.IGNORECASE)
EVENT_RE = re.compile(r"<span\s+class=\"event\">(.*?)</span>", re.DOTALL | re.IGNORECASE)
NEP_DAY_RE = re.compile(r"<span\s+class=\"nep\">(.*?)</span>", re.DOTALL | re.IGNORECASE)
TITHI_RE = re.compile(r"<span\s+class=\"tithi\">(.*?)</span>", re.DOTALL | re.IGNORECASE)
COL1_SPAN_RE = re.compile(r"<div\s+class=\"col1\"[\s\S]*?<span[^>]*>(.*?)</span>", re.DOTALL | re.IGNORECASE)
COL2_RE = re.compile(r"<div\s+class=\"col2\"[^>]*>(.*?)</div>", re.DOTALL | re.IGNORECASE)
PANCHANGA_RE = re.compile(r"<div\s+class=\"panchangaWrapper\">(.*?)</div>", re.DOTALL | re.IGNORECASE)
VIEW_DETAILS_HREF_RE = re.compile(r"<h3\s+class=\"viewDetails\"[\s\S]*?<a[^>]*href=\"([^\"]+)\"", re.DOTALL | re.IGNORECASE)
EVENT_HREF_RE = re.compile(r"<div\s+class=\"eventPopupWrapper\"[\s\S]*?<a[^>]*href=\"([^\"]+)\"", re.DOTALL | re.IGNORECASE)
DETAILS_PATH_RE = re.compile(r"/date/(\d+)-(\d+)-(\d+)")
BR_RE = re.compile(r"<br\s*/?>", re.IGNORECASE)
TAG_RE = re.compile(r"<[^>]+>")
WHITESPACE_RE = re.compile(r"\s+")


@dataclass
class DayRow:
    bs_year: int
    bs_month: int
    bs_day: int
    bs_day_text: str
    bs_month_name: str
    bs_full_date: str
    nepali_weekday: str
    ad_date_iso: str
    ad_date_text: str
    event_summary: str
    tithi: str
    lunar_text: str
    panchanga: str
    details_path: str
    is_holiday: int


@dataclass
class MonthPayload:
    title_nepali: str
    title_english: str
    days: list[DayRow]


class RequestThrottler:
    def __init__(self, min_gap_seconds: float) -> None:
        self.min_gap_seconds = min_gap_seconds
        self._last_request_finished_at: float | None = None

    def wait_turn(self) -> None:
        if self._last_request_finished_at is None:
            return
        elapsed = time.monotonic() - self._last_request_finished_at
        if elapsed < self.min_gap_seconds:
            time.sleep(self.min_gap_seconds - elapsed)

    def mark_finished(self) -> None:
        self._last_request_finished_at = time.monotonic()


class CalendarScraper:
    def __init__(self, throttler: RequestThrottler) -> None:
        self.throttler = throttler

    def fetch_month_html(self, year: int, month: int) -> str:
        self.throttler.wait_turn()
        url = BASE_URL.format(year=year, month=month)
        request = urllib.request.Request(url, headers={"User-Agent": USER_AGENT})
        try:
            with urllib.request.urlopen(request, timeout=HTTP_TIMEOUT_SECONDS) as response:
                payload = response.read()
                charset = response.headers.get_content_charset() or "utf-8"
        finally:
            self.throttler.mark_finished()

        return payload.decode(charset, errors="replace")

    def parse_month(self, requested_year: int, requested_month: int, html_text: str) -> MonthPayload:
        title_matches = TITLE_RE.findall(html_text)
        if len(title_matches) < 2:
            raise ValueError("Could not find month title spans in response")

        title_nepali = self._clean_text(title_matches[0]).rstrip("|").strip()
        title_english = self._clean_text(title_matches[1])

        dates_block_match = DATES_BLOCK_RE.search(html_text)
        if not dates_block_match:
            raise ValueError("Could not find calendar day block in response")

        li_blocks = LI_BLOCK_RE.findall(dates_block_match.group(1))
        if not li_blocks:
            raise ValueError("No day cards found in calendar day block")

        days: list[DayRow] = []
        seen: set[tuple[int, int, int]] = set()

        for block in li_blocks:
            day = self._parse_day_block(block)
            if day is None:
                continue
            if day.bs_year != requested_year or day.bs_month != requested_month:
                continue

            key = (day.bs_year, day.bs_month, day.bs_day)
            if key in seen:
                continue
            seen.add(key)
            days.append(day)

        days.sort(key=lambda d: d.bs_day)

        if not days:
            raise ValueError(f"No in-month days parsed for BS {requested_year}-{requested_month:02d}")

        return MonthPayload(title_nepali=title_nepali, title_english=title_english, days=days)

    def _parse_day_block(self, block: str) -> DayRow | None:
        class_value = self._first_group(CLASS_RE, block)
        class_tokens = self._clean_text(class_value).split()
        is_holiday = 1 if any(token.lower() == "holiday" for token in class_tokens) else 0

        event_text = self._clean_text(self._first_group(EVENT_RE, block))
        if event_text == "--":
            event_text = ""

        bs_day_text = self._clean_text(self._first_group(NEP_DAY_RE, block))
        if not bs_day_text:
            return None

        tithi = ""
        for value in TITHI_RE.findall(block):
            candidate = self._clean_text(value)
            if candidate:
                tithi = candidate
                break

        bs_full_date = self._clean_text(self._first_group(COL1_SPAN_RE, block))
        ad_date_text = self._clean_text(self._first_group(COL2_RE, block))
        if not bs_full_date or not ad_date_text:
            return None

        panchanga_inner = self._first_group(PANCHANGA_RE, block)
        panchanga_lines = self._extract_text_lines(panchanga_inner)
        lunar_text = panchanga_lines[0] if len(panchanga_lines) > 0 else ""
        panchanga_text = panchanga_lines[1] if len(panchanga_lines) > 1 else ""

        details_path = self._first_group(VIEW_DETAILS_HREF_RE, block) or self._first_group(EVENT_HREF_RE, block)
        details_match = DETAILS_PATH_RE.search(details_path)
        if not details_match:
            return None

        bs_year = int(details_match.group(1))
        bs_month = int(details_match.group(2))
        bs_day = int(details_match.group(3))

        left, sep, right = bs_full_date.partition(",")
        nepali_weekday = right.strip() if sep else ""
        bs_month_name = self._extract_bs_month_name(left)

        ad_date_iso = datetime.strptime(ad_date_text, "%B %d, %Y").strftime("%Y-%m-%d")

        return DayRow(
            bs_year=bs_year,
            bs_month=bs_month,
            bs_day=bs_day,
            bs_day_text=bs_day_text,
            bs_month_name=bs_month_name,
            bs_full_date=bs_full_date,
            nepali_weekday=nepali_weekday,
            ad_date_iso=ad_date_iso,
            ad_date_text=ad_date_text,
            event_summary=event_text,
            tithi=tithi,
            lunar_text=lunar_text,
            panchanga=panchanga_text,
            details_path=details_path,
            is_holiday=is_holiday,
        )

    @staticmethod
    def _first_group(pattern: re.Pattern[str], text: str) -> str:
        match = pattern.search(text)
        return match.group(1) if match else ""

    @staticmethod
    def _extract_text_lines(fragment: str) -> list[str]:
        if not fragment:
            return []
        normalized = BR_RE.sub("\n", fragment)
        stripped = TAG_RE.sub(" ", normalized)
        decoded = html.unescape(stripped).replace("\r", "")
        lines = []
        for raw in decoded.split("\n"):
            clean = WHITESPACE_RE.sub(" ", raw).strip()
            if clean:
                lines.append(clean)
        return lines

    @staticmethod
    def _clean_text(value: str) -> str:
        if not value:
            return ""
        no_tags = TAG_RE.sub(" ", value)
        decoded = html.unescape(no_tags)
        normalized = decoded.replace("\r", " ").replace("\n", " ").replace("\t", " ")
        return WHITESPACE_RE.sub(" ", normalized).strip()

    @staticmethod
    def _extract_bs_month_name(bs_date_text: str) -> str:
        parts = bs_date_text.split()
        return parts[1] if len(parts) >= 3 else ""


def utc_now_iso() -> str:
    return datetime.now(timezone.utc).isoformat()


def format_duration(seconds: float) -> str:
    seconds = max(0, int(round(seconds)))
    hours, rem = divmod(seconds, 3600)
    minutes, secs = divmod(rem, 60)
    if hours > 0:
        return f"{hours}h {minutes:02d}m {secs:02d}s"
    return f"{minutes:02d}m {secs:02d}s"


def month_sequence(start_year: int, end_year: int) -> Iterable[tuple[int, int]]:
    for year in range(start_year, end_year + 1):
        for month in range(1, 13):
            yield year, month


def ensure_schema(connection: sqlite3.Connection) -> None:
    connection.executescript(
        """
        PRAGMA journal_mode = WAL;
        PRAGMA synchronous = NORMAL;
        PRAGMA foreign_keys = ON;

        CREATE TABLE IF NOT EXISTS CalendarMonths (
            BsYear INTEGER NOT NULL,
            BsMonth INTEGER NOT NULL,
            TitleNepali TEXT NOT NULL,
            TitleEnglish TEXT NOT NULL,
            FirstAdDateIso TEXT NOT NULL,
            LastAdDateIso TEXT NOT NULL,
            UpdatedAtUtc TEXT NOT NULL,
            PRIMARY KEY (BsYear, BsMonth)
        );

        CREATE TABLE IF NOT EXISTS CalendarDays (
            BsYear INTEGER NOT NULL,
            BsMonth INTEGER NOT NULL,
            BsDay INTEGER NOT NULL,
            BsDayText TEXT NOT NULL,
            BsMonthName TEXT NOT NULL,
            BsFullDate TEXT NOT NULL,
            NepaliWeekday TEXT NOT NULL,
            AdDateIso TEXT NOT NULL,
            AdDateText TEXT NOT NULL,
            EventSummary TEXT NOT NULL,
            Tithi TEXT NOT NULL,
            LunarText TEXT NOT NULL,
            Panchanga TEXT NOT NULL,
            DetailsPath TEXT NOT NULL,
            IsHoliday INTEGER NOT NULL,
            UpdatedAtUtc TEXT NOT NULL,
            PRIMARY KEY (BsYear, BsMonth, BsDay)
        );

        CREATE TABLE IF NOT EXISTS SyncLog (
            BsYear INTEGER NOT NULL,
            BsMonth INTEGER NOT NULL,
            Status TEXT NOT NULL,
            AttemptCount INTEGER NOT NULL,
            LastError TEXT,
            FetchedAtUtc TEXT NOT NULL,
            PRIMARY KEY (BsYear, BsMonth)
        );

        CREATE INDEX IF NOT EXISTS IX_CalendarDays_BsYear_BsMonth_BsDay ON CalendarDays (BsYear, BsMonth, BsDay);
        CREATE INDEX IF NOT EXISTS IX_CalendarDays_AdDateIso ON CalendarDays (AdDateIso);
        CREATE INDEX IF NOT EXISTS IX_CalendarDays_EventSummary ON CalendarDays (EventSummary);
        CREATE INDEX IF NOT EXISTS IX_CalendarDays_Tithi ON CalendarDays (Tithi);
        CREATE INDEX IF NOT EXISTS IX_SyncLog_Status ON SyncLog (Status);
        """
    )


def upsert_month(connection: sqlite3.Connection, year: int, month: int, payload: MonthPayload) -> None:
    first_ad = payload.days[0].ad_date_iso
    last_ad = payload.days[-1].ad_date_iso
    now = utc_now_iso()

    connection.execute(
        """
        INSERT INTO CalendarMonths (BsYear, BsMonth, TitleNepali, TitleEnglish, FirstAdDateIso, LastAdDateIso, UpdatedAtUtc)
        VALUES (?, ?, ?, ?, ?, ?, ?)
        ON CONFLICT(BsYear, BsMonth) DO UPDATE SET
            TitleNepali = excluded.TitleNepali,
            TitleEnglish = excluded.TitleEnglish,
            FirstAdDateIso = excluded.FirstAdDateIso,
            LastAdDateIso = excluded.LastAdDateIso,
            UpdatedAtUtc = excluded.UpdatedAtUtc;
        """,
        (year, month, payload.title_nepali, payload.title_english, first_ad, last_ad, now),
    )

    for day in payload.days:
        connection.execute(
            """
            INSERT INTO CalendarDays (
                BsYear, BsMonth, BsDay, BsDayText, BsMonthName, BsFullDate, NepaliWeekday,
                AdDateIso, AdDateText, EventSummary, Tithi, LunarText, Panchanga, DetailsPath,
                IsHoliday, UpdatedAtUtc
            ) VALUES (?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?)
            ON CONFLICT(BsYear, BsMonth, BsDay) DO UPDATE SET
                BsDayText = excluded.BsDayText,
                BsMonthName = excluded.BsMonthName,
                BsFullDate = excluded.BsFullDate,
                NepaliWeekday = excluded.NepaliWeekday,
                AdDateIso = excluded.AdDateIso,
                AdDateText = excluded.AdDateText,
                EventSummary = excluded.EventSummary,
                Tithi = excluded.Tithi,
                LunarText = excluded.LunarText,
                Panchanga = excluded.Panchanga,
                DetailsPath = excluded.DetailsPath,
                IsHoliday = excluded.IsHoliday,
                UpdatedAtUtc = excluded.UpdatedAtUtc;
            """,
            (
                day.bs_year,
                day.bs_month,
                day.bs_day,
                day.bs_day_text,
                day.bs_month_name,
                day.bs_full_date,
                day.nepali_weekday,
                day.ad_date_iso,
                day.ad_date_text,
                day.event_summary,
                day.tithi,
                day.lunar_text,
                day.panchanga,
                day.details_path,
                day.is_holiday,
                now,
            ),
        )


def upsert_sync_log(connection: sqlite3.Connection, year: int, month: int, status: str, attempts: int, error: str | None) -> None:
    connection.execute(
        """
        INSERT INTO SyncLog (BsYear, BsMonth, Status, AttemptCount, LastError, FetchedAtUtc)
        VALUES (?, ?, ?, ?, ?, ?)
        ON CONFLICT(BsYear, BsMonth) DO UPDATE SET
            Status = excluded.Status,
            AttemptCount = excluded.AttemptCount,
            LastError = excluded.LastError,
            FetchedAtUtc = excluded.FetchedAtUtc;
        """,
        (year, month, status, attempts, error, utc_now_iso()),
    )


def parse_args() -> argparse.Namespace:
    parser = argparse.ArgumentParser(description="Scrape Hamro Patro months into calendar.db")
    parser.add_argument("--start-year", type=int, default=2000, help="BS year to start (inclusive)")
    parser.add_argument("--end-year", type=int, default=2085, help="BS year to end (inclusive)")
    parser.add_argument("--output", type=Path, default=Path(__file__).resolve().parent / "calendar.db", help="SQLite output path")
    return parser.parse_args()


def main() -> int:
    args = parse_args()
    if args.end_year < args.start_year:
        raise ValueError("end-year must be greater than or equal to start-year")

    total_months = (args.end_year - args.start_year + 1) * 12
    script_dir = Path(__file__).resolve().parent
    failed_path = script_dir / "failed.txt"

    output_path: Path = args.output.resolve()
    if output_path.exists():
        print(f"Existing database found at {output_path}. Removing it for a clean rebuild.")
        output_path.unlink()

    failed_path.write_text("", encoding="utf-8")

    connection = sqlite3.connect(output_path)
    try:
        ensure_schema(connection)

        scraper = CalendarScraper(RequestThrottler(REQUEST_GAP_SECONDS))
        started = time.monotonic()

        successes = 0
        failures = 0

        print(f"Starting scrape for BS {args.start_year}..{args.end_year} ({total_months} months)")

        for done, (year, month) in enumerate(month_sequence(args.start_year, args.end_year), start=1):
            month_started = time.monotonic()
            print(f"[{done}/{total_months}] Fetching BS {year}-{month:02d} ...")

            html_text = ""
            last_error: str | None = None
            attempts = 0

            for attempt in (1, 2):
                attempts = attempt
                try:
                    html_text = scraper.fetch_month_html(year, month)
                    break
                except (urllib.error.URLError, TimeoutError, ValueError) as exc:
                    last_error = f"{type(exc).__name__}: {exc}"
                    if attempt == 1:
                        print("    Request failed. Waiting 2.0s, then retrying once...")
                        time.sleep(RETRY_DELAY_SECONDS)
                    else:
                        html_text = ""

            if not html_text:
                failures += 1
                with failed_path.open("a", encoding="utf-8") as handle:
                    handle.write(f"{year}-{month:02d}\n")
                with connection:
                    upsert_sync_log(connection, year, month, "failed", attempts, last_error)
                print(f"    FAILED: {last_error}")
            else:
                try:
                    payload = scraper.parse_month(year, month, html_text)
                    with connection:
                        upsert_month(connection, year, month, payload)
                        upsert_sync_log(connection, year, month, "success", attempts, None)
                    successes += 1
                    print(f"    Parsed {len(payload.days)} in-month day rows")
                except Exception as exc:  # noqa: BLE001
                    failures += 1
                    parse_error = f"ParseError: {type(exc).__name__}: {exc}"
                    with failed_path.open("a", encoding="utf-8") as handle:
                        handle.write(f"{year}-{month:02d}\n")
                    with connection:
                        upsert_sync_log(connection, year, month, "failed", attempts, parse_error)
                    print(f"    FAILED: {parse_error}")

            elapsed = time.monotonic() - started
            avg_per_month = elapsed / done
            remaining = total_months - done
            eta = avg_per_month * remaining
            month_elapsed = time.monotonic() - month_started
            print(
                f"    Progress: {done}/{total_months} | success={successes} failed={failures} "
                f"| month_time={format_duration(month_elapsed)} | ETA={format_duration(eta)}"
            )

        total_days = connection.execute("SELECT COUNT(*) FROM CalendarDays").fetchone()[0]

    finally:
        connection.close()

    db_size = os.path.getsize(output_path)

    print("\n=== Build Summary ===")
    print(f"Database: {output_path}")
    print(f"Total day rows stored: {total_days}")
    print(f"Months succeeded: {successes}")
    print(f"Months failed: {failures}")
    print(f"Database size: {db_size:,} bytes")
    print(f"Failed month list: {failed_path}")

    return 0


if __name__ == "__main__":
    raise SystemExit(main())
