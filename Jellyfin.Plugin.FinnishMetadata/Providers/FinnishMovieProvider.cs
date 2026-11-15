using System.Collections.Generic;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Plugin.FinnishMetadata.Client;
using Jellyfin.Plugin.FinnishMetadata.Configuration;
using Jellyfin.Plugin.FinnishMetadata.Helpers;
using MediaBrowser.Controller.Entities.Movies;
using MediaBrowser.Controller.Providers;
using MediaBrowser.Model.Entities;
using MediaBrowser.Model.Providers;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.FinnishMetadata.Providers;

public class FinnishMovieProvider : IRemoteMetadataProvider<Movie, MovieInfo>
{
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly FinnishApiClient _apiClient;

    public FinnishMovieProvider(IHttpClientFactory httpClientFactory, ILogger<FinnishMovieProvider> logger)
    {
        _httpClientFactory = httpClientFactory;
        _apiClient = new FinnishApiClient(httpClientFactory, logger);
    }

    public string Name => "Finnish Streaming (Areena, Ruutu, Katsomo)";

    public async Task<IEnumerable<RemoteSearchResult>> GetSearchResults(MovieInfo searchInfo, CancellationToken cancellationToken)
    {
        var list = new List<RemoteSearchResult>();

        string? explicitId = FinnishSeriesProvider.ResolveExplicitCompositeId(searchInfo.ProviderIds, searchInfo.Path);
        if (!string.IsNullOrWhiteSpace(explicitId))
        {
            var single = await _apiClient.GetSeriesByIdAsync(explicitId, cancellationToken).ConfigureAwait(false);
            if (single != null)
            {
                var res = new RemoteSearchResult
                {
                    Name = $"{single.Title} [{single.Source}]",
                    Overview = single.Overview,
                    ProductionYear = single.ProductionYear,
                    PremiereDate = single.PremiereDate,
                    ImageUrl = single.ImageUrl,
                    SearchProviderName = Plugin.ProviderName
                };
                FinnishSeriesProvider.ApplyProviderIds(res, single);
                list.Add(res);
                return list;
            }
        }

        var items = await _apiClient.SearchSeriesAsync(searchInfo.Name, searchInfo.Year, cancellationToken).ConfigureAwait(false);
        foreach (var item in items)
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
            FinnishSeriesProvider.ApplyProviderIds(res, item);
            list.Add(res);
        }

        return list;
    }

    public async Task<MetadataResult<Movie>> GetMetadata(MovieInfo info, CancellationToken cancellationToken)
    {
        var result = new MetadataResult<Movie>();
        var config = Plugin.Instance?.Configuration ?? new PluginConfiguration();

        string? localTxt = config.ReadFlickFetchTxtSidecar
            ? FlickFetchTextHelper.ReadSidecarDescription(info.Path)
            : null;

        FinnishMediaItem? media = null;
        string? explicitId = FinnishSeriesProvider.ResolveExplicitCompositeId(info.ProviderIds, info.Path);
        if (!string.IsNullOrWhiteSpace(explicitId))
        {
            media = await _apiClient.GetSeriesByIdAsync(explicitId, cancellationToken).ConfigureAwait(false);
        }
        else if (!string.IsNullOrWhiteSpace(info.Name))
        {
            var matches = await _apiClient.SearchSeriesAsync(info.Name, info.Year, cancellationToken).ConfigureAwait(false);
            if (matches.Count > 0)
            {
                media = matches[0];
            }
        }

        if (media == null && localTxt == null)
        {
            return result;
        }

        var movie = new Movie
        {
            Name = media?.Title ?? info.Name,
            Overview = (config.PreferLocalTxtOverRemote && !string.IsNullOrWhiteSpace(localTxt))
                ? localTxt
                : (media?.Overview ?? localTxt),
            ProductionYear = media?.ProductionYear ?? info.Year,
            PremiereDate = media?.PremiereDate
        };

        if (media != null)
        {
            FinnishSeriesProvider.ApplyProviderIds(movie, media);
        }

        result.Item = movie;
        result.HasMetadata = true;
        result.Provider = Name;
        return result;
    }

    public Task<HttpResponseMessage> GetImageResponse(string url, CancellationToken cancellationToken)
    {
        return _httpClientFactory.CreateClient().GetAsync(url, cancellationToken);
    }
}
