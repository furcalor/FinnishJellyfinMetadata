using System.Collections.Generic;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Plugin.FinnishMetadata.Client;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Entities.Movies;
using MediaBrowser.Controller.Entities.TV;
using MediaBrowser.Controller.Providers;
using MediaBrowser.Model.Entities;
using MediaBrowser.Model.Providers;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.FinnishMetadata.Providers;

public class FinnishImageProvider : IRemoteImageProvider
{
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly FinnishApiClient _apiClient;

    public FinnishImageProvider(IHttpClientFactory httpClientFactory, ILogger<FinnishImageProvider> logger)
    {
        _httpClientFactory = httpClientFactory;
        _apiClient = new FinnishApiClient(httpClientFactory, logger);
    }

    public string Name => "Finnish Streaming Images";

    public bool Supports(BaseItem item) => item is Series or Episode or Movie;

    public IEnumerable<ImageType> GetSupportedImages(BaseItem item)
    {
        return new[] { ImageType.Primary, ImageType.Thumb, ImageType.Backdrop };
    }

    public async Task<IEnumerable<RemoteImageInfo>> GetImages(BaseItem item, CancellationToken cancellationToken)
    {
        var list = new List<RemoteImageInfo>();
        if (item.ProviderIds.TryGetValue(Plugin.ProviderId, out var compositeId) && !string.IsNullOrWhiteSpace(compositeId))
        {
            var media = await _apiClient.GetSeriesByIdAsync(compositeId, cancellationToken).ConfigureAwait(false);
            if (!string.IsNullOrWhiteSpace(media?.ImageUrl))
            {
                list.Add(new RemoteImageInfo
                {
                    ProviderName = Name,
                    Url = media.ImageUrl,
                    Type = item is Episode ? ImageType.Thumb : ImageType.Primary
                });
            }
        }

        return list;
    }

    public Task<HttpResponseMessage> GetImageResponse(string url, CancellationToken cancellationToken)
    {
        return _httpClientFactory.CreateClient().GetAsync(url, cancellationToken);
    }
}
