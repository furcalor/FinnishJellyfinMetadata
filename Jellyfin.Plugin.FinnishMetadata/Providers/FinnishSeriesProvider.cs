using System.Collections.Generic;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Plugin.FinnishMetadata.Client;
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

        if (searchInfo.ProviderIds.TryGetValue(Plugin.ProviderId, out var existingId) && !string.IsNullOrWhiteSpace(existingId))
        {
            var single = await _apiClient.GetSeriesByIdAsync(existingId, cancellationToken).ConfigureAwait(false);
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

        if (info.ProviderIds.TryGetValue(Plugin.ProviderId, out var compositeId) && !string.IsNullOrWhiteSpace(compositeId))
        {
            media = await _apiClient.GetSeriesByIdAsync(compositeId, cancellationToken).ConfigureAwait(false);
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

        series.SetProviderId(Plugin.ProviderId, media.CompositeId);

        result.Item = series;
        result.HasMetadata = true;
        result.Provider = Name;

        return result;
    }

    public Task<HttpResponseMessage> GetImageResponse(string url, CancellationToken cancellationToken)
    {
        return _httpClientFactory.CreateClient().GetAsync(url, cancellationToken);
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
        res.SetProviderId(Plugin.ProviderId, item.CompositeId);
        return res;
    }
}
