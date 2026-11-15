using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Text.RegularExpressions;
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

    public async Task<IEnumerable<RemoteSearchResult>> GetSearchResults(EpisodeInfo searchInfo, CancellationToken cancellationToken)
    {
        var results = new List<RemoteSearchResult>();
        string? explicitId = FinnishSeriesProvider.ResolveExplicitCompositeId(searchInfo.ProviderIds, searchInfo.Path);
        if (!string.IsNullOrWhiteSpace(explicitId))
        {
            var media = await _apiClient.GetSeriesByIdAsync(explicitId, cancellationToken).ConfigureAwait(false);
            if (media != null)
            {
                var res = new RemoteSearchResult
                {
                    Name = $"{media.Title} [{media.Source}]",
                    Overview = media.Overview,
                    ProductionYear = media.ProductionYear,
                    PremiereDate = media.PremiereDate,
                    ImageUrl = media.ImageUrl,
                    SearchProviderName = Plugin.ProviderName
                };
                FinnishSeriesProvider.ApplyProviderIds(res, media);
                results.Add(res);
            }
        }

        return results;
    }

    public async Task<MetadataResult<Episode>> GetMetadata(EpisodeInfo info, CancellationToken cancellationToken)
    {
        var result = new MetadataResult<Episode>();
        var config = Plugin.Instance?.Configuration ?? new PluginConfiguration();

        // 1. Parse filename for "S01E01", "Kausi 01 - Jakso 03", "YYYY-MM-DD - Jakso XX - Title", and embedded IDs
        var parsedFile = FinnishFilenameParser.Parse(info.Path);
        FlickFetchTextHelper.PopulateIdsFromDirectory(info.Path, parsedFile);

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

        // 3. Check if the episode itself has an explicit ID (in Jellyfin UI or filename)
        string? explicitEpisodeId = FinnishSeriesProvider.ResolveExplicitCompositeId(info.ProviderIds, null);
        FinnishEpisodeItem? remoteEp = null;

        if (!string.IsNullOrWhiteSpace(explicitEpisodeId))
        {
            var directMedia = await _apiClient.GetSeriesByIdAsync(explicitEpisodeId, cancellationToken).ConfigureAwait(false);
            if (directMedia != null)
            {
                remoteEp = new FinnishEpisodeItem
                {
                    CompositeId = directMedia.CompositeId,
                    RawPlatformId = directMedia.RawPlatformId,
                    Source = directMedia.Source,
                    Title = directMedia.Title,
                    Overview = directMedia.Overview,
                    SeasonNumber = seasonNum,
                    EpisodeNumber = episodeNum,
                    AirDate = airDateFromFilename ?? directMedia.PremiereDate,
                    ThumbnailUrl = directMedia.ImageUrl
                };
            }
        }

        // 4. Otherwise resolve via Series ID or Series + Episode title search
        if (remoteEp == null)
        {
            string? seriesCompositeId = FinnishSeriesProvider.ResolveExplicitCompositeId(info.SeriesProviderIds, info.Path);
            string seriesName = !string.IsNullOrWhiteSpace(parsedFile.SeriesName)
                ? parsedFile.SeriesName
                : GetSeriesNameFromPath(info.Path) ?? info.Name;

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
            if (!string.IsNullOrEmpty(remoteEp.RawPlatformId))
            {
                if (remoteEp.Source == "YleAreena")
                {
                    episode.SetProviderId(Plugin.YleProviderId, remoteEp.RawPlatformId);
                }
                else if (remoteEp.Source == "Ruutu")
                {
                    episode.SetProviderId(Plugin.RuutuProviderId, remoteEp.RawPlatformId);
                }
                else if (remoteEp.Source == "Katsomo")
                {
                    episode.SetProviderId(Plugin.KatsomoProviderId, remoteEp.RawPlatformId);
                }
            }
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

    private static string? GetSeriesNameFromPath(string? filePath)
    {
        if (string.IsNullOrWhiteSpace(filePath))
        {
            return null;
        }

        string? parentDir = Path.GetDirectoryName(filePath);
        if (string.IsNullOrEmpty(parentDir))
        {
            return null;
        }

        string folderName = Path.GetFileName(parentDir);
        if (folderName.StartsWith("Season ", StringComparison.OrdinalIgnoreCase) ||
            folderName.StartsWith("Kausi ", StringComparison.OrdinalIgnoreCase))
        {
            string? seriesDir = Path.GetDirectoryName(parentDir);
            if (!string.IsNullOrEmpty(seriesDir))
            {
                folderName = Path.GetFileName(seriesDir);
            }
        }

        folderName = Regex.Replace(folderName, @"\s*\[[^\]]+\]", string.Empty);
        return Regex.Replace(folderName, @"\s*\(\d{4}\)\s*$", string.Empty).Trim();
    }
}
