using System.Collections.Generic;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Plugin.FinnishMetadata.Client;
using Jellyfin.Plugin.FinnishMetadata.Helpers;
using MediaBrowser.Controller.Entities.TV;
using MediaBrowser.Controller.Providers;
using MediaBrowser.Model.Entities;
using MediaBrowser.Model.Providers;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.FinnishMetadata.Providers;

public class FinnishSeriesProvider : IRemoteMetadataProvider<Series, SeriesInfo>
{
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly FinnishApiClient _apiClient;

    public FinnishSeriesProvider(IHttpClientFactory httpClientFactory, ILogger<FinnishSeriesProvider> logger)
    {
        _httpClientFactory = httpClientFactory;
        _apiClient = new FinnishApiClient(httpClientFactory, logger);
    }

    public string Name => "Finnish Streaming (Areena, Ruutu, Katsomo)";

    public async Task<IEnumerable<RemoteSearchResult>> GetSearchResults(SeriesInfo searchInfo, CancellationToken cancellationToken)
    {
        var results = new List<RemoteSearchResult>();

        string? explicitId = ResolveExplicitCompositeId(searchInfo.ProviderIds, searchInfo.Path);
        if (!string.IsNullOrWhiteSpace(explicitId))
        {
            var single = await _apiClient.GetSeriesByIdAsync(explicitId, cancellationToken).ConfigureAwait(false);
            if (single != null)
            {
                results.Add(ToSearchResult(single));
                return results;
            }
        }

        var items = await _apiClient.SearchSeriesAsync(searchInfo.Name, searchInfo.Year, cancellationToken).ConfigureAwait(false);
        foreach (var item in items)
        {
            results.Add(ToSearchResult(item));
        }

        return results;
    }

    public async Task<MetadataResult<Series>> GetMetadata(SeriesInfo info, CancellationToken cancellationToken)
    {
        var result = new MetadataResult<Series>();
        FinnishMediaItem? media = null;

        string? explicitId = ResolveExplicitCompositeId(info.ProviderIds, info.Path);
        if (!string.IsNullOrWhiteSpace(explicitId))
        {
            media = await _apiClient.GetSeriesByIdAsync(explicitId, cancellationToken).ConfigureAwait(false);
        }

        if (media == null && !string.IsNullOrWhiteSpace(info.Name))
        {
            var searchMatches = await _apiClient.SearchSeriesAsync(info.Name, info.Year, cancellationToken).ConfigureAwait(false);
            if (searchMatches.Count > 0)
            {
                media = searchMatches[0];
            }
        }

        if (media == null)
        {
            return result;
        }

        var series = new Series
        {
            Name = media.Title,
            Overview = media.Overview,
            ProductionYear = media.ProductionYear ?? info.Year,
            PremiereDate = media.PremiereDate
        };

        ApplyProviderIds(series, media);

        result.Item = series;
        result.HasMetadata = true;
        result.Provider = Name;

        return result;
    }

    public Task<HttpResponseMessage> GetImageResponse(string url, CancellationToken cancellationToken)
    {
        return _httpClientFactory.CreateClient().GetAsync(url, cancellationToken);
    }

    internal static string? ResolveExplicitCompositeId(Dictionary<string, string> providerIds, string? itemPath)
    {
        if (providerIds.TryGetValue(Plugin.YleProviderId, out var yleId) && !string.IsNullOrWhiteSpace(yleId))
        {
            return $"areena:{FinnishApiClient.NormalizeIdOrUrl(yleId, "areena")}";
        }

        if (providerIds.TryGetValue(Plugin.RuutuProviderId, out var ruutuId) && !string.IsNullOrWhiteSpace(ruutuId))
        {
            return $"ruutu:{FinnishApiClient.NormalizeIdOrUrl(ruutuId, "ruutu")}";
        }

        if (providerIds.TryGetValue(Plugin.KatsomoProviderId, out var katsomoId) && !string.IsNullOrWhiteSpace(katsomoId))
        {
            return $"katsomo:{FinnishApiClient.NormalizeIdOrUrl(katsomoId, "katsomo")}";
        }

        if (providerIds.TryGetValue(Plugin.ProviderId, out var compositeId) && !string.IsNullOrWhiteSpace(compositeId))
        {
            return compositeId;
        }

        if (!string.IsNullOrWhiteSpace(itemPath))
        {
            var parsed = new ParsedFinnishFilename();
            FlickFetchTextHelper.PopulateIdsFromDirectory(itemPath, parsed);
            if (!string.IsNullOrEmpty(parsed.YleId))
            {
                return $"areena:{parsed.YleId}";
            }

            if (!string.IsNullOrEmpty(parsed.RuutuId))
            {
                return $"ruutu:{parsed.RuutuId}";
            }

            if (!string.IsNullOrEmpty(parsed.KatsomoId))
            {
                return $"katsomo:{parsed.KatsomoId}";
            }
        }

        return null;
    }

    internal static void ApplyProviderIds(IHasProviderIds target, FinnishMediaItem media)
    {
        target.SetProviderId(Plugin.ProviderId, media.CompositeId);
        if (!string.IsNullOrEmpty(media.RawPlatformId))
        {
            if (media.Source == "YleAreena")
            {
                target.SetProviderId(Plugin.YleProviderId, media.RawPlatformId);
            }
            else if (media.Source == "Ruutu")
            {
                target.SetProviderId(Plugin.RuutuProviderId, media.RawPlatformId);
            }
            else if (media.Source == "Katsomo")
            {
                target.SetProviderId(Plugin.KatsomoProviderId, media.RawPlatformId);
            }
        }
    }

    private static RemoteSearchResult ToSearchResult(FinnishMediaItem item)
    {
        var res = new RemoteSearchResult
        {
            Name = $"{item.Title} [{item.Source}]",
            Overview = item.Overview,
            ProductionYear = item.ProductionYear,
            PremiereDate = item.PremiereDate,
            ImageUrl = item.ImageUrl,
            SearchProviderName = Plugin.ProviderName
        };
        ApplyProviderIds(res, item);
        return res;
    }
}
