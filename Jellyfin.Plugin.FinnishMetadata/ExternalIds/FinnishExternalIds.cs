using MediaBrowser.Controller.Entities.Movies;
using MediaBrowser.Controller.Entities.TV;
using MediaBrowser.Controller.Providers;
using MediaBrowser.Model.Entities;
using MediaBrowser.Model.Providers;

namespace Jellyfin.Plugin.FinnishMetadata.ExternalIds;

public class YleAreenaExternalId : IExternalId
{
    public string ProviderName => "Yle Areena";

    public string Key => Plugin.YleProviderId;

    public ExternalIdMediaType? Type => null;

    public string? UrlFormatString => "https://areena.yle.fi/{0}";

    public bool Supports(IHasProviderIds item) => item is Series or Episode or Movie;
}

public class RuutuSeriesExternalId : IExternalId
{
    public string ProviderName => "Ruutu";

    public string Key => Plugin.RuutuProviderId;

    public ExternalIdMediaType? Type => ExternalIdMediaType.Series;

    public string? UrlFormatString => "https://www.ruutu.fi/series/{0}";

    public bool Supports(IHasProviderIds item) => item is Series;
}

public class RuutuEpisodeExternalId : IExternalId
{
    public string ProviderName => "Ruutu Video";

    public string Key => Plugin.RuutuProviderId;

    public ExternalIdMediaType? Type => null;

    public string? UrlFormatString => "https://www.ruutu.fi/video/{0}";

    public bool Supports(IHasProviderIds item) => item is Episode or Movie;
}

public class KatsomoExternalId : IExternalId
{
    public string ProviderName => "MTV Katsomo";

    public string Key => Plugin.KatsomoProviderId;

    public ExternalIdMediaType? Type => null;

    public string? UrlFormatString => "https://www.katsomo.fi/sarja/{0}";

    public bool Supports(IHasProviderIds item) => item is Series or Episode or Movie;
}
