#!/usr/bin/env python3
"""Prebuild calendar.db by scraping Hamro Patro calendar pages month by month."""

from __future__ import annotations

import argparse
import json
import os
import re
import sqlite3
import time
import urllib.error
import urllib.request
from dataclasses import dataclass
from datetime import date, datetime, timezone
from pathlib import Path
from typing import Iterable

BASE_URL = "https://www.hamropatro.com/calendar/{year}/{month}"
REQUEST_GAP_SECONDS = 0.5
RETRY_DELAY_SECONDS = 2.0
HTTP_TIMEOUT_SECONDS = 30
USER_AGENT = "OpenPatroCalendarBuilder/1.0 (+https://www.hamropatro.com)"

# The month page embeds its grid as JSON in <script id="hp-bootstrap"> under data.calendar.initialMonths.
BOOTSTRAP_RE = re.compile(r"<script[^>]*\bid=\"hp-bootstrap\"[^>]*>(.*?)</script>", re.DOTALL)
WHITESPACE_RE = re.compile(r"\s+")

# Name tables mirror Hamro Patro's JS bundle; panchanga fields are indices into them.
# Keep in sync with Services/HamroPatroParser.cs.
LUNAR_MONTH_NAMES = ["वैशाख", "जेठ", "असार", "साउन", "भदौ", "असोज", "कार्तिक", "मंसिर", "पुष", "माघ", "फागुन", "चैत"]
PAKSHA_NAMES = ["शुक्ल", "कृष्ण"]
YOGA_NAMES = ["विषकुम्भ", "प्रीति", "आयुष्मान", "सौभाग्य", "शोभन", "अतिगंड", "सुकर्मा", "धृति", "शूल", "गण्ड", "वृद्धि", "ध्रुव", "व्याघात", "हर्षण", "वज्र", "सिद्धि", "व्यतिपात", "वरियान", "परिधि", "शिव", "सिद्ध", "साध्य", "शुभ", "शुक्ल", "ब्रह्म", "एन्द्र", "वैधृति"]
KARANA_NAMES = ["बव", "बालव", "कौलव", "तैतिल", "गर", "वणिज", "विष्टि/भद्रा", "शकुनि", "चतुष्पद", "नाग", "किंस्तुघ्न"]
NAKSHATRA_NAMES = ["अश्विनी", "भरणी", "कृत्तिका", "रोहिणी", "मृगशिरा", "आर्द्रा", "पुनर्वसु", "पुष्य", "अश्लेषा", "मघा", "पूर्वफल्गुनी", "उत्तरफल्गुनी", "हस्ता", "चित्रा", "स्वाति", "विशाखा", "अनुराधा", "ज्येष्ठा", "मूल", "पूर्वाषाढा", "उत्तरषाढा", "श्रवण", "धनिष्ठा", "शतभिषा", "पूर्वभाद्र", "उत्तरभाद्र", "रेवती"]


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
        bootstrap_match = BOOTSTRAP_RE.search(html_text)
        if not bootstrap_match:
            raise ValueError("Could not find hp-bootstrap calendar data in response")

        months = json.loads(bootstrap_match.group(1))["data"]["calendar"]["initialMonths"]
        month = next((m for m in months if m["yearBs"] == requested_year and m["monthBs"] == requested_month), None)
        if month is None:
            raise ValueError(f"BS {requested_year}-{requested_month:02d} missing from calendar data")

        in_month = sorted((d for d in month["days"] if d["inMonth"]), key=lambda d: d["day_bs"])
        if not in_month or len(in_month) != month["daysInMonth"]:
            raise ValueError(f"Expected {month['daysInMonth']} days for BS {requested_year}-{requested_month:02d}, found {len(in_month)}")

        weekdays = [header["np"] for header in month["weekdayHeaders"]]
        days = [self._parse_day(month, day, weekdays) for day in in_month]

        first_ad = self._ad_date(in_month[0])
        last_ad = self._ad_date(in_month[-1])
        english_months = f"{first_ad:%b}/{last_ad:%b}"
        title_english = f"{english_months} {first_ad.year}" if first_ad.year == last_ad.year else f"{english_months} {first_ad.year}-{last_ad.year}"

        return MonthPayload(title_nepali=f"{month['yearBsNp']} {month['monthNameNp']}", title_english=title_english, days=days)

    def _parse_day(self, month: dict, day: dict, weekdays: list[str]) -> DayRow:
        ad_date = self._ad_date(day)
        if ad_date.isoweekday() % 7 != day["dayOfWeek"]:
            raise ValueError(f"Weekday mismatch for BS {day['year_bs']}-{day['month_bs']}-{day['day_bs']}")

        nepali_weekday = weekdays[day["dayOfWeek"]]
        patro_event = day.get("patroEvent") or {}
        events = [self._clean_text(item.get("titleNp")) for item in patro_event.get("items") or []]
        tithi = self._clean_text(day.get("tithiShort"))
        panchanga = day.get("panchanga")

        if panchanga:
            adhik = "अधिक " if panchanga.get("monthType") == 1 else ""
            lunar_text = f"{adhik}{self._lookup(LUNAR_MONTH_NAMES, panchanga.get('month'))} {self._lookup(PAKSHA_NAMES, panchanga.get('paksha'))} {tithi}".strip()
            panchanga_text = (
                f"पञ्चाङ्ग: {self._lookup(YOGA_NAMES, panchanga.get('yoga'))} "
                f"{self._lookup(KARANA_NAMES, panchanga.get('karana'))} "
                f"{self._lookup(NAKSHATRA_NAMES, panchanga.get('nakshtra'))}"
            )
        else:
            lunar_text = "--"
            panchanga_text = ""

        return DayRow(
            bs_year=day["year_bs"],
            bs_month=day["month_bs"],
            bs_day=day["day_bs"],
            bs_day_text=day["dayBsNp"],
            bs_month_name=month["monthNameNp"],
            bs_full_date=f"{day['dayBsNp']} {month['monthNameNp']} {month['yearBsNp']}, {nepali_weekday}",
            nepali_weekday=nepali_weekday,
            ad_date_iso=ad_date.strftime("%Y-%m-%d"),
            ad_date_text=ad_date.strftime("%B %d, %Y"),
            event_summary="/".join(event for event in events if event),
            tithi=tithi,
            lunar_text=lunar_text,
            panchanga=panchanga_text,
            details_path=f"/date/{day['year_bs']}-{day['month_bs']}-{day['day_bs']}",
            # Event-level flag only: the day-level isHoliday also covers weekends, which the app colours itself.
            is_holiday=1 if patro_event.get("isHoliday") else 0,
        )

    @staticmethod
    def _ad_date(day: dict) -> date:
        return date(day["year_ad"], day["month_ad"], day["day_ad"])

    @staticmethod
    def _lookup(names: list[str], index: int | None) -> str:
        return names[index] if isinstance(index, int) and 0 <= index < len(names) else ""

    @staticmethod
    def _clean_text(value: str | None) -> str:
        return WHITESPACE_RE.sub(" ", value).strip() if value else ""


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
