using System;
using System.Globalization;
using System.IO;
using System.Text.RegularExpressions;

namespace Jellyfin.Plugin.FinnishMetadata.Helpers;

public sealed class ParsedFinnishFilename
{
    public string SeriesName { get; set; } = string.Empty;
    public int? ProductionYear { get; set; }
    public int? SeasonNumber { get; set; }
    public int? EpisodeNumber { get; set; }
    public DateTime? AirDate { get; set; }
    public string EpisodeTitle { get; set; } = string.Empty;
}

public static partial class FinnishFilenameParser
{
    // Pattern 1: Standard SxxExx
    // Example: "Muistojeni Karjala (2026) - S01E01 - Lapsuus Karjalassa"
    [GeneratedRegex(@"^(?<series>.+?)(?:\s*\((?<year>\d{4})\))?\s*(?:[-_ ]+\s*)?[Ss](?<season>\d{1,3})[Ee](?<episode>\d{1,4})(?:\s*[-_ ]+\s*(?<title>.+))?$", RegexOptions.IgnoreCase | RegexOptions.Compiled)]
    private static partial Regex StandardSxxExxRegex();

    // Pattern 2: Finnish "Kausi XX - Jakso YY" or "Jakso YY" (with optional YYYY-MM-DD air date before or after Kausi/Jakso)
    // Examples:
    // - "Erittäin salainen - Vakoilua Suomessa - 2024-02-05 - Jakso 01 - Seksiä, rakkautta ja vakoilua"
    // - "Sarjan Nimi - Kausi 01 - Jakso 03 - Jakson nimi"
    // - "Sarjan Nimi (2025) - 2025-03-10 - Kausi 01 - Jakso 03 - Jakson nimi"
    // - "Sarjan Nimi - Kausi 1 Jakso 3 - Jakson nimi"
    [GeneratedRegex(@"^(?<series>.+?)\s*(?:[-_ ]\s*(?<date1>\d{4}-\d{2}-\d{2}))?\s*(?:[-_ ]\s*Kausi\s*(?<season>\d{1,3}))?\s*[-_ ]*\s*Jakso\s*(?<episode>\d{1,4})(?:\s*[-_ ]\s*(?<date2>\d{4}-\d{2}-\d{2}))?(?:\s*[-_ ]+\s*(?<title>.+))?$", RegexOptions.IgnoreCase | RegexOptions.Compiled)]
    private static partial Regex JaksoPatternRegex();

    // Standalone "Kausi XX" in parent folder name (e.g., "Kausi 1", "Kausi 02", "Season 1")
    [GeneratedRegex(@"(?:Kausi|Season)\s*(?<season>\d{1,3})", RegexOptions.IgnoreCase | RegexOptions.Compiled)]
    private static partial Regex FolderSeasonRegex();

    // Helper to strip (YYYY) from a series title if present
    [GeneratedRegex(@"^(?<cleanSeries>.+?)\s*\((?<year>\d{4})\)\s*$", RegexOptions.Compiled)]
    private static partial Regex TrailingYearRegex();

    public static ParsedFinnishFilename Parse(string? filePath)
    {
        var result = new ParsedFinnishFilename();
        if (string.IsNullOrWhiteSpace(filePath))
        {
            return result;
        }

        string fileName = Path.GetFileNameWithoutExtension(filePath);
        int? folderSeason = ExtractSeasonFromParentDirectory(filePath);

        // 1. Try Finnish "Kausi XX - Jakso YY" / "Jakso YY" pattern first
        var jaksoMatch = JaksoPatternRegex().Match(fileName);
        if (jaksoMatch.Success)
        {
            ExtractSeriesAndYear(jaksoMatch.Groups["series"].Value, result);

            string? dateStr = jaksoMatch.Groups["date1"].Success
                ? jaksoMatch.Groups["date1"].Value
                : (jaksoMatch.Groups["date2"].Success ? jaksoMatch.Groups["date2"].Value : null);

            if (!string.IsNullOrEmpty(dateStr) &&
                DateTime.TryParseExact(dateStr, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsedDate))
            {
                result.AirDate = parsedDate;
                result.ProductionYear ??= parsedDate.Year;
            }

            if (jaksoMatch.Groups["season"].Success && int.TryParse(jaksoMatch.Groups["season"].Value, out var sNum))
            {
                result.SeasonNumber = sNum;
            }
            else
            {
                result.SeasonNumber = folderSeason ?? 1;
            }

            if (int.TryParse(jaksoMatch.Groups["episode"].Value, out var eNum))
            {
                result.EpisodeNumber = eNum;
            }

            if (jaksoMatch.Groups["title"].Success)
            {
                result.EpisodeTitle = jaksoMatch.Groups["title"].Value.Trim();
            }

            return result;
        }

        // 2. Try standard SxxExx pattern
        var sxxExxMatch = StandardSxxExxRegex().Match(fileName);
        if (sxxExxMatch.Success)
        {
            ExtractSeriesAndYear(sxxExxMatch.Groups["series"].Value, result);

            if (sxxExxMatch.Groups["year"].Success && int.TryParse(sxxExxMatch.Groups["year"].Value, out var yr))
            {
                result.ProductionYear = yr;
            }

            if (int.TryParse(sxxExxMatch.Groups["season"].Value, out var sNum))
            {
                result.SeasonNumber = sNum;
            }
            else if (folderSeason.HasValue)
            {
                result.SeasonNumber = folderSeason;
            }

            if (int.TryParse(sxxExxMatch.Groups["episode"].Value, out var eNum))
            {
                result.EpisodeNumber = eNum;
            }

            if (sxxExxMatch.Groups["title"].Success)
            {
                result.EpisodeTitle = sxxExxMatch.Groups["title"].Value.Trim();
            }
        }

        return result;
    }

    private static int? ExtractSeasonFromParentDirectory(string filePath)
    {
        try
        {
            string? dir = Path.GetDirectoryName(filePath);
            if (string.IsNullOrEmpty(dir))
            {
                return null;
            }

            string folderName = Path.GetFileName(dir);
            var match = FolderSeasonRegex().Match(folderName);
            if (match.Success && int.TryParse(match.Groups["season"].Value, out var season))
            {
                return season;
            }
        }
        catch
        {
            // Ignore path errors
        }

        return null;
    }

    private static void ExtractSeriesAndYear(string rawSeries, ParsedFinnishFilename target)
    {
        var trimmed = rawSeries.Trim(' ', '-', '_');
        var yearMatch = TrailingYearRegex().Match(trimmed);
        if (yearMatch.Success)
        {
            target.SeriesName = yearMatch.Groups["cleanSeries"].Value.Trim(' ', '-', '_');
            if (int.TryParse(yearMatch.Groups["year"].Value, out var y))
            {
                target.ProductionYear = y;
            }
        }
        else
        {
            target.SeriesName = trimmed;
        }
    }
}
