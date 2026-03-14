using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;
using HtmlAgilityPack;
using OpenPatro.Models;

namespace OpenPatro.Services;

public sealed partial class HamroPatroParser
{
    public ParsedCalendarMonth ParseMonthHtml(int requestedYear, int requestedMonth, string html)
    {
        var document = new HtmlDocument();
        document.LoadHtml(html);

        var titleNodes = document.DocumentNode.SelectNodes("//span[contains(@class,'headderNew')]") ?? throw new InvalidOperationException("Month header not found.");
        var dateNodes = document.DocumentNode.SelectNodes("//ul[contains(@class,'dates')]/li[contains(@onclick,'openPopUp')]") ?? throw new InvalidOperationException("Calendar day nodes not found.");

        var days = new List<CalendarDayRecord>(dateNodes.Count);
        foreach (var dateNode in dateNodes)
        {
            days.Add(ParseDayNode(dateNode));
        }

        return new ParsedCalendarMonth
        {
            RequestedYear = requestedYear,
            RequestedMonth = requestedMonth,
            TitleNepali = CleanText(titleNodes[0].InnerText).TrimEnd('|', ' '),
            TitleEnglish = CleanText(titleNodes[1].InnerText),
            Days = days
        };
    }

    private static CalendarDayRecord ParseDayNode(HtmlNode dayNode)
    {
        var classValue = dayNode.GetAttributeValue("class", string.Empty);
        var isHoliday = classValue.Contains("holiday", StringComparison.OrdinalIgnoreCase);

        var eventText = CleanText(dayNode.SelectSingleNode("./span[contains(@class,'event')]")?.InnerText ?? "--");
        var bsDayText = CleanText(dayNode.SelectSingleNode("./span[contains(@class,'nep')]")?.InnerText ?? throw new InvalidOperationException("BS day text missing."));
        var tithiText = dayNode.SelectNodes("./span[contains(@class,'tithi')]")?.Select(node => CleanText(node.InnerText)).FirstOrDefault(text => !string.IsNullOrWhiteSpace(text)) ?? string.Empty;

        var popupNode = dayNode.SelectSingleNode(".//div[contains(@class,'daydetailsPopOver')]") ?? throw new InvalidOperationException("Popup node missing.");
        var fullBsText = CleanText(popupNode.SelectSingleNode(".//div[contains(@class,'col1')]//span")?.InnerText ?? throw new InvalidOperationException("Full BS date missing."));
        var adDateText = CleanText(popupNode.SelectSingleNode(".//div[contains(@class,'col2')]")?.InnerText ?? throw new InvalidOperationException("AD date missing."));
        var panchangaBlock = popupNode.SelectSingleNode(".//div[contains(@class,'panchangaWrapper')]") ?? throw new InvalidOperationException("Panchanga block missing.");

        var panchangaLines = panchangaBlock.InnerText
            .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(CleanText)
            .Where(static text => !string.IsNullOrWhiteSpace(text))
            .ToArray();

        var lunarText = panchangaLines.ElementAtOrDefault(0) ?? string.Empty;
        var panchanga = panchangaLines.ElementAtOrDefault(1) ?? string.Empty;
        var detailsPath = popupNode.SelectSingleNode(".//h3[contains(@class,'viewDetails')]//a")?.GetAttributeValue("href", string.Empty)
            ?? popupNode.SelectSingleNode(".//div[contains(@class,'eventPopupWrapper')]//a")?.GetAttributeValue("href", string.Empty)
            ?? string.Empty;

        var detailsMatch = DetailsPathRegex().Match(detailsPath);
        if (!detailsMatch.Success)
        {
            throw new InvalidOperationException($"Unexpected details path: {detailsPath}");
        }

        var bsYear = int.Parse(detailsMatch.Groups[1].Value, CultureInfo.InvariantCulture);
        var bsMonth = int.Parse(detailsMatch.Groups[2].Value, CultureInfo.InvariantCulture);
        var bsDay = int.Parse(detailsMatch.Groups[3].Value, CultureInfo.InvariantCulture);

        var bsTextParts = fullBsText.Split(',', 2, StringSplitOptions.TrimEntries);
        var nepaliWeekday = bsTextParts.Length > 1 ? bsTextParts[1] : string.Empty;
        var bsMonthName = ExtractBsMonthName(bsTextParts[0]);
        var adDate = DateTime.ParseExact(adDateText, "MMMM dd, yyyy", CultureInfo.InvariantCulture);

        return new CalendarDayRecord
        {
            BsYear = bsYear,
            BsMonth = bsMonth,
            BsDay = bsDay,
            BsDayText = bsDayText,
            BsMonthName = bsMonthName,
            BsFullDate = fullBsText,
            NepaliWeekday = nepaliWeekday,
            AdDateIso = DateOnly.FromDateTime(adDate).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            AdDateText = adDateText,
            EventSummary = eventText,
            Tithi = tithiText,
            LunarText = lunarText,
            Panchanga = panchanga,
            DetailsPath = detailsPath,
            IsHoliday = isHoliday
        };
    }

    private static string ExtractBsMonthName(string bsDateText)
    {
        var parts = bsDateText.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        return parts.Length >= 3 ? parts[1] : string.Empty;
    }

    private static string CleanText(string value)
    {
        return HtmlEntity.DeEntitize(value).Replace("\r", string.Empty).Replace("\t", " ").Trim();
    }

    [GeneratedRegex(@"/date/(\d+)-(\d+)-(\d+)", RegexOptions.Compiled)]
    private static partial Regex DetailsPathRegex();
}