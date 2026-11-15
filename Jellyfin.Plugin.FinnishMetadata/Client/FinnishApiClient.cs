using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Web;
using System.Xml.Linq;
using Jellyfin.Plugin.FinnishMetadata.Configuration;
using Jellyfin.Plugin.FinnishMetadata.Helpers;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.FinnishMetadata.Client;

public partial class FinnishApiClient
{
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILogger _logger;

    [GeneratedRegex(@"<script[^>]+type=[""']application/ld\+json[""'][^>]*>(?<json>[\s\S]*?)</script>", RegexOptions.IgnoreCase | RegexOptions.Compiled)]
    private static partial Regex JsonLdScriptRegex();

    [GeneratedRegex(@"(?:areena\.yle\.fi/|^|\b)(?<id>1-\d{5,10})\b", RegexOptions.IgnoreCase | RegexOptions.Compiled)]
    private static partial Regex YleIdRegex();

    public FinnishApiClient(IHttpClientFactory httpClientFactory, ILogger logger)
    {
        _httpClientFactory = httpClientFactory;
        _logger = logger;
    }

    private HttpClient CreateClient()
    {
        var client = _httpClientFactory.CreateClient();
        client.DefaultRequestHeaders.UserAgent.ParseAdd("Mozilla/5.0 (Windows NT 10.0; Win64; x64; rv:128.0) Gecko/20100101 Firefox/128.0");
        client.DefaultRequestHeaders.AcceptLanguage.ParseAdd("fi-FI,fi;q=0.9,en;q=0.5");
        client.Timeout = TimeSpan.FromSeconds(15);
        return client;
    }

    public static string NormalizeIdOrUrl(string input, string platformPrefix)
    {
        string trimmed = input.Trim().Trim('"').Trim('/');
        if (platformPrefix == "areena")
        {
            var m = YleIdRegex().Match(trimmed);
            if (m.Success)
            {
                return m.Groups["id"].Value;
            }
        }
        else if (platformPrefix == "ruutu")
        {
            var m = Regex.Match(trimmed, @"(?:ruutu\.fi/(?:series|video|ohjelmat)/|^)(?<id>[a-z0-9\-]+)$", RegexOptions.IgnoreCase);
            if (m.Success)
            {
                return m.Groups["id"].Value;
            }
        }
        else if (platformPrefix == "katsomo")
        {
            var m = Regex.Match(trimmed, @"(?:katsomo\.fi/(?:sarja|ohjelma)/|mtvuutiset\.fi/katsomo/ohjelma/|^)(?<id>[a-z0-9\-]+)$", RegexOptions.IgnoreCase);
            if (m.Success)
            {
                return m.Groups["id"].Value;
            }
        }

        return trimmed;
    }

    public async Task<List<FinnishMediaItem>> SearchSeriesAsync(string query, int? year, CancellationToken cancellationToken)
    {
        var results = new List<FinnishMediaItem>();
        var config = Plugin.Instance?.Configuration ?? new PluginConfiguration();

        if (string.IsNullOrWhiteSpace(query))
        {
            return results;
        }

        // Strip any literal quotes passed by Jellyfin (e.g. '"Kekkonen"' -> 'Kekkonen')
        string cleanQuery = query.Trim().Trim('"', '\'').Trim();
        if (string.IsNullOrWhiteSpace(cleanQuery))
        {
            return results;
        }

        // 1. Check if the query itself is a direct URL or platform ID (e.g. "1-4294832", "https://areena.yle.fi/1-3436275", "https://www.ruutu.fi/series/4839488", "4839488")
        var directIdCheck = new ParsedFinnishFilename();
        FinnishFilenameParser.ExtractIdsFromText(cleanQuery, directIdCheck);

        if (!string.IsNullOrEmpty(directIdCheck.YleId))
        {
            var item = await FetchYleProgramPageAsync($"https://areena.yle.fi/{directIdCheck.YleId}", directIdCheck.YleId, cancellationToken).ConfigureAwait(false);
            if (item != null)
            {
                results.Add(item);
                return results;
            }
        }

        if (!string.IsNullOrEmpty(directIdCheck.RuutuId))
        {
            var item = await FetchRuutuSeriesAsync(directIdCheck.RuutuId, cancellationToken).ConfigureAwait(false);
            if (item != null)
            {
                results.Add(item);
                return results;
            }
        }

        if (!string.IsNullOrEmpty(directIdCheck.KatsomoId))
        {
            var item = await FetchKatsomoPageAsync(directIdCheck.KatsomoId, cancellationToken).ConfigureAwait(false);
            if (item != null)
            {
                results.Add(item);
                return results;
            }
        }

        // Pure numeric ID pasted into search box (e.g. "4839488" for Ruutu or "33006241" for Katsomo)
        if (Regex.IsMatch(cleanQuery, @"^\d{5,12}$"))
        {
            if (config.EnableRuutu)
            {
                var ruutuById = await FetchRuutuSeriesAsync(cleanQuery, cancellationToken).ConfigureAwait(false);
                if (ruutuById != null)
                {
                    results.Add(ruutuById);
                }
            }

            if (config.EnableKatsomo)
            {
                var katsomoById = await FetchKatsomoPageAsync(cleanQuery, cancellationToken).ConfigureAwait(false);
                if (katsomoById != null)
                {
                    results.Add(katsomoById);
                }
            }

            if (results.Count > 0)
            {
                return results;
            }
        }

        // 2. Standard keyword search across enabled providers
        if (config.EnableYleAreena)
        {
            try
            {
                var yleResults = await SearchYleAreenaAsync(cleanQuery, year, cancellationToken).ConfigureAwait(false);
                results.AddRange(yleResults);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "[FinnishMetadata] Yle Areena search failed for '{Query}'", cleanQuery);
            }
        }

        if (config.EnableRuutu)
        {
            try
            {
                var ruutuResults = await SearchRuutuAsync(cleanQuery, cancellationToken).ConfigureAwait(false);
                results.AddRange(ruutuResults);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "[FinnishMetadata] Ruutu search failed for '{Query}'", cleanQuery);
            }
        }

        if (config.EnableKatsomo)
        {
            try
            {
                var katsomoResults = await SearchKatsomoAsync(cleanQuery, cancellationToken).ConfigureAwait(false);
                results.AddRange(katsomoResults);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "[FinnishMetadata] Katsomo search failed for '{Query}'", cleanQuery);
            }
        }

        return results;
    }

    public async Task<FinnishMediaItem?> GetSeriesByIdAsync(string compositeOrRawId, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(compositeOrRawId))
        {
            return null;
        }

        string trimmed = compositeOrRawId.Trim().Trim('"');

        if (trimmed.StartsWith("areena:", StringComparison.OrdinalIgnoreCase) ||
            trimmed.Contains("areena.yle.fi", StringComparison.OrdinalIgnoreCase) ||
            Regex.IsMatch(trimmed, @"^1-\d{5,10}$"))
        {
            string yleId = NormalizeIdOrUrl(trimmed.StartsWith("areena:", StringComparison.OrdinalIgnoreCase) ? trimmed.Substring("areena:".Length) : trimmed, "areena");
            return await FetchYleProgramPageAsync($"https://areena.yle.fi/{yleId}", yleId, cancellationToken).ConfigureAwait(false);
        }

        if (trimmed.StartsWith("ruutu:", StringComparison.OrdinalIgnoreCase) ||
            trimmed.Contains("ruutu.fi", StringComparison.OrdinalIgnoreCase))
        {
            string ruutuId = NormalizeIdOrUrl(trimmed.StartsWith("ruutu:", StringComparison.OrdinalIgnoreCase) ? trimmed.Substring("ruutu:".Length) : trimmed, "ruutu");
            return await FetchRuutuSeriesAsync(ruutuId, cancellationToken).ConfigureAwait(false);
        }

        if (trimmed.StartsWith("katsomo:", StringComparison.OrdinalIgnoreCase) ||
            trimmed.Contains("katsomo.fi", StringComparison.OrdinalIgnoreCase))
        {
            string katsomoSlug = NormalizeIdOrUrl(trimmed.StartsWith("katsomo:", StringComparison.OrdinalIgnoreCase) ? trimmed.Substring("katsomo:".Length) : trimmed, "katsomo");
            return await FetchKatsomoPageAsync(katsomoSlug, cancellationToken).ConfigureAwait(false);
        }

        return null;
    }

    public async Task<FinnishEpisodeItem?> FindEpisodeAsync(
        string? seriesCompositeId,
        string seriesName,
        int? seasonNumber,
        int? episodeNumber,
        string? episodeTitle,
        DateTime? airDate,
        CancellationToken cancellationToken)
    {
        string cleanSeries = seriesName.Trim().Trim('"');
        string? cleanEpTitle = episodeTitle?.Trim().Trim('"');

        // If the episode itself or series has an explicit ID, check if we can resolve from the series page
        if (!string.IsNullOrWhiteSpace(seriesCompositeId) &&
            seriesCompositeId.StartsWith("areena:", StringComparison.OrdinalIgnoreCase) &&
            string.IsNullOrWhiteSpace(cleanEpTitle))
        {
            var seriesMedia = await GetSeriesByIdAsync(seriesCompositeId, cancellationToken).ConfigureAwait(false);
            if (seriesMedia != null)
            {
                return new FinnishEpisodeItem
                {
                    CompositeId = seriesMedia.CompositeId,
                    RawPlatformId = seriesMedia.RawPlatformId,
                    Source = seriesMedia.Source,
                    Title = cleanEpTitle ?? $"Jakso {episodeNumber ?? 1}",
                    Overview = seriesMedia.Overview,
                    SeasonNumber = seasonNumber,
                    EpisodeNumber = episodeNumber,
                    AirDate = airDate ?? seriesMedia.PremiereDate,
                    ThumbnailUrl = seriesMedia.ImageUrl
                };
            }
        }

        string searchQuery = !string.IsNullOrWhiteSpace(cleanEpTitle)
            ? $"{cleanSeries} {cleanEpTitle}"
            : (seasonNumber.HasValue && episodeNumber.HasValue
                ? $"{cleanSeries} Kausi {seasonNumber} Jakso {episodeNumber}"
                : cleanSeries);

        var candidates = await SearchSeriesAsync(searchQuery, airDate?.Year, cancellationToken).ConfigureAwait(false);
        var best = candidates.FirstOrDefault();
        if (best == null)
        {
            return null;
        }

        return new FinnishEpisodeItem
        {
            CompositeId = best.CompositeId,
            RawPlatformId = best.RawPlatformId,
            Source = best.Source,
            Title = !string.IsNullOrWhiteSpace(cleanEpTitle) ? cleanEpTitle : best.Title,
            Overview = best.Overview,
            SeasonNumber = seasonNumber,
            EpisodeNumber = episodeNumber,
            AirDate = airDate ?? best.PremiereDate,
            ThumbnailUrl = best.ImageUrl
        };
    }

    // =========================================================================
    // 1. YLE AREENA (Direct ID + haku.yle.fi + JSON-LD / OpenGraph)
    // =========================================================================
    private async Task<List<FinnishMediaItem>> SearchYleAreenaAsync(string query, int? year, CancellationToken ct)
    {
        var list = new List<FinnishMediaItem>();
        using var client = CreateClient();

        // Try Yle's search service (haku.yle.fi) and Areena TV search without throwing on 404
        string[] searchUrls =
        {
            $"https://haku.yle.fi/?language=fi&UILanguage=fi&q={Uri.EscapeDataString(query)}",
            $"https://areena.yle.fi/tv/ohjelmat/kaikki?q={Uri.EscapeDataString(query)}"
        };

        var foundIds = new List<string>();
        foreach (var searchUrl in searchUrls)
        {
            using var response = await client.GetAsync(searchUrl, ct).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
            {
                continue;
            }

            string html = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
            var matches = YleIdRegex().Matches(html)
                .Select(m => m.Groups["id"].Value)
                .Distinct();

            foreach (var id in matches)
            {
                if (!foundIds.Contains(id, StringComparer.OrdinalIgnoreCase))
                {
                    foundIds.Add(id);
                }
            }

            if (foundIds.Count >= 4)
            {
                break;
            }
        }

        foreach (var yleId in foundIds.Take(4))
        {
            var item = await FetchYleProgramPageAsync($"https://areena.yle.fi/{yleId}", yleId, ct).ConfigureAwait(false);
            if (item != null && !string.IsNullOrWhiteSpace(item.Title))
            {
                list.Add(item);
            }
        }

        return list;
    }

    public async Task<FinnishMediaItem?> FetchYleProgramPageAsync(string url, string yleId, CancellationToken ct)
    {
        using var client = CreateClient();
        using var response = await client.GetAsync(url, ct).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
        {
            return null;
        }

        string html = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
        var ldMatches = JsonLdScriptRegex().Matches(html);

        foreach (Match match in ldMatches)
        {
            string json = match.Groups["json"].Value.Trim();
            try
            {
                using var doc = JsonDocument.Parse(json);
                var root = doc.RootElement;

                if (root.TryGetProperty("@type", out var typeProp))
                {
                    string type = typeProp.GetString() ?? string.Empty;
                    if (type is "TVSeries" or "TVEpisode" or "Movie" or "VideoObject" or "CreativeWorkSeries")
                    {
                        var item = new FinnishMediaItem
                        {
                            CompositeId = $"areena:{yleId}",
                            RawPlatformId = yleId,
                            Source = "YleAreena",
                            PageUrl = url
                        };

                        if (root.TryGetProperty("name", out var nameProp))
                        {
                            item.Title = nameProp.GetString() ?? string.Empty;
                        }

                        if (root.TryGetProperty("description", out var descProp))
                        {
                            item.Overview = descProp.GetString() ?? string.Empty;
                        }

                        if (root.TryGetProperty("image", out var imgProp))
                        {
                            item.ImageUrl = imgProp.ValueKind == JsonValueKind.String
                                ? imgProp.GetString()
                                : (imgProp.ValueKind == JsonValueKind.Array && imgProp.GetArrayLength() > 0
                                    ? imgProp[0].GetString()
                                    : null);
                        }

                        if (root.TryGetProperty("datePublished", out var dateProp) &&
                            DateTime.TryParse(dateProp.GetString(), CultureInfo.InvariantCulture, DateTimeStyles.None, out var pubDate))
                        {
                            item.PremiereDate = pubDate;
                            item.ProductionYear = pubDate.Year;
                        }

                        if (!string.IsNullOrWhiteSpace(item.Title))
                        {
                            return item;
                        }
                    }
                }
            }
            catch
            {
                // Ignore malformed JSON-LD blocks
            }
        }

        // OpenGraph fallback for Yle Areena pages (e.g., https://areena.yle.fi/1-4294832 or https://areena.yle.fi/1-3436275)
        string? ogTitle = ExtractMetaContent(html, "og:title");
        string? ogDesc = ExtractMetaContent(html, "og:description");
        string? ogImage = ExtractMetaContent(html, "og:image");

        if (!string.IsNullOrWhiteSpace(ogTitle))
        {
            ogTitle = Regex.Replace(ogTitle, @"\s*[|\-–]\s*Yle Areena.*$", string.Empty, RegexOptions.IgnoreCase).Trim();
            return new FinnishMediaItem
            {
                CompositeId = $"areena:{yleId}",
                RawPlatformId = yleId,
                Source = "YleAreena",
                Title = ogTitle,
                Overview = ogDesc ?? string.Empty,
                ImageUrl = ogImage,
                PageUrl = url
            };
        }

        return null;
    }

    // =========================================================================
    // 2. RUUTU.FI (Supports /series/{id}, /ohjelmat/{slug}, /video/{id}, and Gatling XML)
    // =========================================================================
    private async Task<List<FinnishMediaItem>> SearchRuutuAsync(string query, CancellationToken ct)
    {
        var list = new List<FinnishMediaItem>();
        using var client = CreateClient();

        string url = $"https://www.ruutu.fi/haku?q={Uri.EscapeDataString(query)}";
        using var response = await client.GetAsync(url, ct).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
        {
            return list;
        }

        string html = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
        var idMatches = Regex.Matches(html, @"href=[""']/(?:series|ohjelmat|video)/(?<id>[a-z0-9\-]+)[""']", RegexOptions.IgnoreCase)
            .Select(m => m.Groups["id"].Value)
            .Distinct()
            .Take(3);

        foreach (var id in idMatches)
        {
            var item = await FetchRuutuSeriesAsync(id, ct).ConfigureAwait(false);
            if (item != null)
            {
                list.Add(item);
            }
        }

        return list;
    }

    public async Task<FinnishMediaItem?> FetchRuutuSeriesAsync(string slugOrId, CancellationToken ct)
    {
        using var client = CreateClient();

        // Try /series/{id} first (e.g. https://www.ruutu.fi/series/4839488), then /ohjelmat/{slug}, then /video/{id}
        string[] candidateUrls = Regex.IsMatch(slugOrId, @"^\d+$")
            ? new[]
            {
                $"https://www.ruutu.fi/series/{slugOrId}",
                $"https://www.ruutu.fi/video/{slugOrId}",
                $"https://www.ruutu.fi/ohjelmat/{slugOrId}"
            }
            : new[]
            {
                $"https://www.ruutu.fi/ohjelmat/{slugOrId}",
                $"https://www.ruutu.fi/series/{slugOrId}"
            };

        foreach (var pageUrl in candidateUrls)
        {
            using var response = await client.GetAsync(pageUrl, ct).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
            {
                continue;
            }

            string html = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
            string? title = ExtractMetaContent(html, "og:title");
            string? desc = ExtractMetaContent(html, "og:description");
            string? image = ExtractMetaContent(html, "og:image");

            if (!string.IsNullOrWhiteSpace(title))
            {
                title = Regex.Replace(title, @"\s*[|\-–]\s*Ruutu.*$", string.Empty, RegexOptions.IgnoreCase).Trim();
                return new FinnishMediaItem
                {
                    CompositeId = $"ruutu:{slugOrId}",
                    RawPlatformId = slugOrId,
                    Source = "Ruutu",
                    Title = title,
                    Overview = desc ?? string.Empty,
                    ImageUrl = image,
                    PageUrl = pageUrl
                };
            }
        }

        // Fallback: Ruutu Gatling XML cache if numeric video ID
        if (Regex.IsMatch(slugOrId, @"^\d+$"))
        {
            try
            {
                string gatlingUrl = $"https://gatling.nelonenmedia.fi/media-xml-cache?id={slugOrId}";
                using var xmlResp = await client.GetAsync(gatlingUrl, ct).ConfigureAwait(false);
                if (xmlResp.IsSuccessStatusCode)
                {
                    string xml = await xmlResp.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
                    var doc = XDocument.Parse(xml);
                    string? programName = doc.Descendants("Program").FirstOrDefault()?.Attribute("program_name")?.Value
                                       ?? doc.Descendants("Clip").FirstOrDefault()?.Element("PassthroughVariables")?.Element("program_name")?.Value;
                    string? desc = doc.Descendants("Program").FirstOrDefault()?.Attribute("description")?.Value;

                    if (!string.IsNullOrWhiteSpace(programName))
                    {
                        return new FinnishMediaItem
                        {
                            CompositeId = $"ruutu:{slugOrId}",
                            RawPlatformId = slugOrId,
                            Source = "Ruutu",
                            Title = programName,
                            Overview = desc ?? string.Empty,
                            PageUrl = $"https://www.ruutu.fi/video/{slugOrId}"
                        };
                    }
                }
            }
            catch
            {
                // Ignore XML parse failure
            }
        }

        return null;
    }

    // =========================================================================
    // 3. MTV KATSOMO (Supports www.katsomo.fi/sarja/{id} & mtvuutiset.fi/katsomo/ohjelma/{id})
    // =========================================================================
    private async Task<List<FinnishMediaItem>> SearchKatsomoAsync(string query, CancellationToken ct)
    {
        var list = new List<FinnishMediaItem>();
        using var client = CreateClient();

        string[] searchUrls =
        {
            $"https://www.katsomo.fi/haku?q={Uri.EscapeDataString(query)}",
            $"https://www.mtvuutiset.fi/katsomo/haku?q={Uri.EscapeDataString(query)}"
        };

        foreach (var url in searchUrls)
        {
            using var response = await client.GetAsync(url, ct).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
            {
                continue;
            }

            string html = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
            var matches = Regex.Matches(html, @"href=[""'](?:https://www\.katsomo\.fi)?/(?:sarja|ohjelma|katsomo/ohjelma)/(?<slug>[^""'/?#]+)[""']", RegexOptions.IgnoreCase)
                .Select(m => m.Groups["slug"].Value)
                .Distinct()
                .Take(3);

            foreach (var slug in matches)
            {
                var item = await FetchKatsomoPageAsync(slug, ct).ConfigureAwait(false);
                if (item != null)
                {
                    list.Add(item);
                }
            }

            if (list.Count > 0)
            {
                break;
            }
        }

        return list;
    }

    public async Task<FinnishMediaItem?> FetchKatsomoPageAsync(string slugOrId, CancellationToken ct)
    {
        using var client = CreateClient();
        string[] candidateUrls =
        {
            $"https://www.katsomo.fi/sarja/{slugOrId}",
            $"https://www.katsomo.fi/ohjelma/{slugOrId}",
            $"https://www.mtvuutiset.fi/katsomo/ohjelma/{slugOrId}"
        };

        foreach (var url in candidateUrls)
        {
            using var response = await client.GetAsync(url, ct).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
            {
                continue;
            }

            string html = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
            string? title = ExtractMetaContent(html, "og:title");
            string? desc = ExtractMetaContent(html, "og:description");
            string? image = ExtractMetaContent(html, "og:image");

            if (!string.IsNullOrWhiteSpace(title))
            {
                title = Regex.Replace(title, @"\s*[-|–]\s*MTV Katsomo.*$", string.Empty, RegexOptions.IgnoreCase).Trim();
                return new FinnishMediaItem
                {
                    CompositeId = $"katsomo:{slugOrId}",
                    RawPlatformId = slugOrId,
                    Source = "Katsomo",
                    Title = title,
                    Overview = desc ?? string.Empty,
                    ImageUrl = image,
                    PageUrl = url
                };
            }
        }

        return null;
    }

    private static string? ExtractMetaContent(string html, string propertyName)
    {
        // Support both <meta property="og:title" content="..."> and <meta content="..." property="og:title">
        var match1 = Regex.Match(
            html,
            $@"<meta[^>]+(?:property|name)=[""']{Regex.Escape(propertyName)}[""'][^>]+content=[""'](?<val>[^""']*)[""']",
            RegexOptions.IgnoreCase);

        if (match1.Success)
        {
            return HttpUtility.HtmlDecode(match1.Groups["val"].Value);
        }

        var match2 = Regex.Match(
            html,
            $@"<meta[^>]+content=[""'](?<val>[^""']*)[""'][^>]+(?:property|name)=[""']{Regex.Escape(propertyName)}[""']",
            RegexOptions.IgnoreCase);

        if (match2.Success)
        {
            return HttpUtility.HtmlDecode(match2.Groups["val"].Value);
        }

        return null;
    }
}
