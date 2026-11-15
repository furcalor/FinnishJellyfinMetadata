using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Plugin.FinnishMetadata.Client;
using Jellyfin.Plugin.FinnishMetadata.Configuration;
using Jellyfin.Plugin.FinnishMetadata.Helpers;
using MediaBrowser.Controller.Entities.TV;
using MediaBrowser.Controller.Providers;
using MediaBrowser.Model.Entities;
using MediaBrowser.Model.Providers;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.FinnishMetadata.Providers;

public class FinnishEpisodeProvider : IRemoteMetadataProvider<Episode, EpisodeInfo>
{
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly FinnishApiClient _apiClient;

    public FinnishEpisodeProvider(IHttpClientFactory httpClientFactory, ILogger<FinnishEpisodeProvider> logger)
    {
        _httpClientFactory = httpClientFactory;
        _apiClient = new FinnishApiClient(httpClientFactory, logger);
    }

    public string Name => "Finnish Streaming Episode Provider";

    public Task<IEnumerable<RemoteSearchResult>> GetSearchResults(EpisodeInfo searchInfo, CancellationToken cancellationToken)
    {
        return Task.FromResult<IEnumerable<RemoteSearchResult>>(Array.Empty<RemoteSearchResult>());
    }

    public async Task<MetadataResult<Episode>> GetMetadata(EpisodeInfo info, CancellationToken cancellationToken)
    {
        var result = new MetadataResult<Episode>();
        var config = Plugin.Instance?.Configuration ?? new PluginConfiguration();

        // 1. Parse filename for "S01E01", "Kausi 01 - Jakso 03", and "YYYY-MM-DD - Jakso XX - Title"
        var parsedFile = FinnishFilenameParser.Parse(info.Path);

        int? seasonNum = parsedFile.SeasonNumber ?? info.ParentIndexNumber;
        int? episodeNum = parsedFile.EpisodeNumber ?? info.IndexNumber;
        string? epTitleFromFilename = !string.IsNullOrWhiteSpace(parsedFile.EpisodeTitle) ? parsedFile.EpisodeTitle : null;
        DateTime? airDateFromFilename = parsedFile.AirDate ?? info.PremiereDate;

        // 2. Read local FlickFetch .txt sidecar if enabled
        string? localSidecarOverview = null;
        if (config.ReadFlickFetchTxtSidecar)
        {
            localSidecarOverview = FlickFetchTextHelper.ReadSidecarDescription(info.Path);
        }

        // 3. Query remote API (Areena / Ruutu / Katsomo)
        info.SeriesProviderIds.TryGetValue(Plugin.ProviderId, out var seriesCompositeId);
        string seriesName = !string.IsNullOrWhiteSpace(parsedFile.SeriesName) ? parsedFile.SeriesName : info.SeriesName;

        FinnishEpisodeItem? remoteEp = null;
        if (!string.IsNullOrWhiteSpace(seriesName))
        {
            remoteEp = await _apiClient.FindEpisodeAsync(
                seriesCompositeId,
                seriesName,
                seasonNum,
                episodeNum,
                epTitleFromFilename ?? info.Name,
                airDateFromFilename,
                cancellationToken).ConfigureAwait(false);
        }

        if (localSidecarOverview == null && remoteEp == null && epTitleFromFilename == null && !episodeNum.HasValue)
        {
            return result;
        }

        var episode = new Episode
        {
            ParentIndexNumber = seasonNum,
            IndexNumber = episodeNum,
            Name = epTitleFromFilename ?? remoteEp?.Title ?? info.Name,
            PremiereDate = airDateFromFilename ?? remoteEp?.AirDate,
            ProductionYear = (airDateFromFilename ?? remoteEp?.AirDate)?.Year ?? parsedFile.ProductionYear
        };

        if (config.PreferLocalTxtOverRemote && !string.IsNullOrWhiteSpace(localSidecarOverview))
        {
            episode.Overview = localSidecarOverview;
        }
        else
        {
            episode.Overview = !string.IsNullOrWhiteSpace(remoteEp?.Overview)
                ? remoteEp.Overview
                : localSidecarOverview;
        }

        if (remoteEp != null && !string.IsNullOrWhiteSpace(remoteEp.CompositeId))
        {
            episode.SetProviderId(Plugin.ProviderId, remoteEp.CompositeId);
        }

        result.Item = episode;
        result.HasMetadata = true;
        result.Provider = Name;

        return result;
    }

    public Task<HttpResponseMessage> GetImageResponse(string url, CancellationToken cancellationToken)
    {
        return _httpClientFactory.CreateClient().GetAsync(url, cancellationToken);
    }
}
