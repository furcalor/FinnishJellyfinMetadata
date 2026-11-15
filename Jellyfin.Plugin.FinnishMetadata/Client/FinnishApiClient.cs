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
using Jellyfin.Plugin.FinnishMetadata.Configuration;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.FinnishMetadata.Client;

public partial class FinnishApiClient
{
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILogger _logger;

    [GeneratedRegex(@"<script[^>]+type=[""']application/ld\+json[""'][^>]*>(?<json>[\s\S]*?)</script>", RegexOptions.IgnoreCase | RegexOptions.Compiled)]
    private static partial Regex JsonLdScriptRegex();

    [GeneratedRegex(@"(?<id>1-\d{5,10})", RegexOptions.Compiled)]
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

    public async Task<List<FinnishMediaItem>> SearchSeriesAsync(string query, int? year, CancellationToken cancellationToken)
    {
        var results = new List<FinnishMediaItem>();
        var config = Plugin.Instance?.Configuration ?? new PluginConfiguration();

        if (string.IsNullOrWhiteSpace(query))
        {
            return results;
        }

        if (config.EnableYleAreena)
        {
            try
            {
                var yleResults = await SearchYleAreenaAsync(query, year, cancellationToken).ConfigureAwait(false);
                results.AddRange(yleResults);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "[FinnishMetadata] Yle Areena search failed for '{Query}'", query);
            }
        }

        if (config.EnableRuutu)
        {
            try
            {
                var ruutuResults = await SearchRuutuAsync(query, cancellationToken).ConfigureAwait(false);
                results.AddRange(ruutuResults);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "[FinnishMetadata] Ruutu search failed for '{Query}'", query);
            }
        }

        if (config.EnableKatsomo)
        {
            try
            {
                var katsomoResults = await SearchKatsomoAsync(query, cancellationToken).ConfigureAwait(false);
                results.AddRange(katsomoResults);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "[FinnishMetadata] Katsomo search failed for '{Query}'", query);
            }
        }

        return results;
    }

    public async Task<FinnishMediaItem?> GetSeriesByIdAsync(string compositeId, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(compositeId))
        {
            return null;
        }

        if (compositeId.StartsWith("areena:", StringComparison.OrdinalIgnoreCase))
        {
            string yleId = compositeId.Substring("areena:".Length);
            return await FetchYleProgramPageAsync($"https://areena.yle.fi/{yleId}", yleId, cancellationToken).ConfigureAwait(false);
        }

        if (compositeId.StartsWith("ruutu:", StringComparison.OrdinalIgnoreCase))
        {
            string ruutuId = compositeId.Substring("ruutu:".Length);
            return await FetchRuutuSeriesAsync(ruutuId, cancellationToken).ConfigureAwait(false);
        }

        if (compositeId.StartsWith("katsomo:", StringComparison.OrdinalIgnoreCase))
        {
            string katsomoSlug = compositeId.Substring("katsomo:".Length);
            return await FetchKatsomoPageAsync($"https://www.mtvuutiset.fi/katsomo/ohjelma/{katsomoSlug}", katsomoSlug, cancellationToken).ConfigureAwait(false);
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
        string searchQuery = !string.IsNullOrWhiteSpace(episodeTitle)
            ? $"{seriesName} {episodeTitle}"
            : (seasonNumber.HasValue && episodeNumber.HasValue
                ? $"{seriesName} Kausi {seasonNumber} Jakso {episodeNumber}"
                : seriesName);

        var candidates = await SearchSeriesAsync(searchQuery, airDate?.Year, cancellationToken).ConfigureAwait(false);
        var best = candidates.FirstOrDefault();
        if (best == null)
        {
            return null;
        }

        return new FinnishEpisodeItem
        {
            CompositeId = best.CompositeId,
            Title = !string.IsNullOrWhiteSpace(episodeTitle) ? episodeTitle : best.Title,
            Overview = best.Overview,
            SeasonNumber = seasonNumber,
            EpisodeNumber = episodeNumber,
            AirDate = airDate ?? best.PremiereDate,
            ThumbnailUrl = best.ImageUrl
        };
    }

    private async Task<List<FinnishMediaItem>> SearchYleAreenaAsync(string query, int? year, CancellationToken ct)
    {
        var list = new List<FinnishMediaItem>();
        using var client = CreateClient();

        string searchUrl = $"https://areena.yle.fi/haku?q={HttpUtility.UrlEncode(query)}";
        string html = await client.GetStringAsync(searchUrl, ct).ConfigureAwait(false);

        var idMatches = YleIdRegex().Matches(html)
            .Select(m => m.Groups["id"].Value)
            .Distinct()
            .Take(4)
            .ToList();

        foreach (var yleId in idMatches)
        {
            var item = await FetchYleProgramPageAsync($"https://areena.yle.fi/{yleId}", yleId, ct).ConfigureAwait(false);
            if (item != null && !string.IsNullOrWhiteSpace(item.Title))
            {
                list.Add(item);
            }
        }

        return list;
    }

    private async Task<FinnishMediaItem?> FetchYleProgramPageAsync(string url, string yleId, CancellationToken ct)
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
                    if (type is "TVSeries" or "TVEpisode" or "Movie" or "VideoObject")
                    {
                        var item = new FinnishMediaItem
                        {
                            CompositeId = $"areena:{yleId}",
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

                        return item;
                    }
                }
            }
            catch
            {
                // Ignore malformed JSON-LD blocks
            }
        }

        return null;
    }

    private async Task<List<FinnishMediaItem>> SearchRuutuAsync(string query, CancellationToken ct)
    {
        var list = new List<FinnishMediaItem>();
        using var client = CreateClient();

        string url = $"https://www.ruutu.fi/haku?q={HttpUtility.UrlEncode(query)}";
        using var response = await client.GetAsync(url, ct).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
        {
            return list;
        }

        string html = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
        var seriesSlugMatches = Regex.Matches(html, @"href=[""']/ohjelmat/(?<slug>[a-z0-9\-]+)[""']", RegexOptions.IgnoreCase)
            .Select(m => m.Groups["slug"].Value)
            .Distinct()
            .Take(3);

        foreach (var slug in seriesSlugMatches)
        {
            var item = await FetchRuutuSeriesAsync(slug, ct).ConfigureAwait(false);
            if (item != null)
            {
                list.Add(item);
            }
        }

        return list;
    }

    private async Task<FinnishMediaItem?> FetchRuutuSeriesAsync(string slugOrId, CancellationToken ct)
    {
        using var client = CreateClient();
        string pageUrl = $"https://www.ruutu.fi/ohjelmat/{slugOrId}";
        using var response = await client.GetAsync(pageUrl, ct).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
        {
            return null;
        }

        string html = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
        string? title = ExtractMetaContent(html, "og:title");
        string? desc = ExtractMetaContent(html, "og:description");
        string? image = ExtractMetaContent(html, "og:image");

        if (string.IsNullOrWhiteSpace(title))
        {
            return null;
        }

        title = Regex.Replace(title, @"\s*\|\s*Ruutu.*$", string.Empty, RegexOptions.IgnoreCase).Trim();

        return new FinnishMediaItem
        {
            CompositeId = $"ruutu:{slugOrId}",
            Source = "Ruutu",
            Title = title,
            Overview = desc ?? string.Empty,
            ImageUrl = image,
            PageUrl = pageUrl
        };
    }

    private async Task<List<FinnishMediaItem>> SearchKatsomoAsync(string query, CancellationToken ct)
    {
        var list = new List<FinnishMediaItem>();
        using var client = CreateClient();

        string url = $"https://www.mtvuutiset.fi/katsomo/haku?q={HttpUtility.UrlEncode(query)}";
        using var response = await client.GetAsync(url, ct).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
        {
            return list;
        }

        string html = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
        var matches = Regex.Matches(html, @"href=[""'](?<path>/katsomo/ohjelma/(?<slug>[^""'/?#]+))[""']", RegexOptions.IgnoreCase)
            .Select(m => m.Groups["slug"].Value)
            .Distinct()
            .Take(3);

        foreach (var slug in matches)
        {
            var item = await FetchKatsomoPageAsync($"https://www.mtvuutiset.fi/katsomo/ohjelma/{slug}", slug, ct).ConfigureAwait(false);
            if (item != null)
            {
                list.Add(item);
            }
        }

        return list;
    }

    private async Task<FinnishMediaItem?> FetchKatsomoPageAsync(string url, string slug, CancellationToken ct)
    {
        using var client = CreateClient();
        using var response = await client.GetAsync(url, ct).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
        {
            return null;
        }

        string html = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
        string? title = ExtractMetaContent(html, "og:title");
        string? desc = ExtractMetaContent(html, "og:description");
        string? image = ExtractMetaContent(html, "og:image");

        if (string.IsNullOrWhiteSpace(title))
        {
            return null;
        }

        title = Regex.Replace(title, @"\s*[-|]\s*MTV Katsomo.*$", string.Empty, RegexOptions.IgnoreCase).Trim();

        return new FinnishMediaItem
        {
            CompositeId = $"katsomo:{slug}",
            Source = "Katsomo",
            Title = title,
            Overview = desc ?? string.Empty,
            ImageUrl = image,
            PageUrl = url
        };
    }

    private static string? ExtractMetaContent(string html, string propertyName)
    {
        var match = Regex.Match(
            html,
            $@"<meta[^>]+(?:property|name)=[""']{Regex.Escape(propertyName)}[""'][^>]+content=[""'](?<val>[^""']*)[""']",
            RegexOptions.IgnoreCase);

        if (match.Success)
        {
            return HttpUtility.HtmlDecode(match.Groups["val"].Value);
        }

        return null;
    }
}
