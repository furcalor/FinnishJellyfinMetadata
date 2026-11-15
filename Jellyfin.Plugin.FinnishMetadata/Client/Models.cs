using System;
using System.Collections.Generic;

namespace Jellyfin.Plugin.FinnishMetadata.Client;

public sealed class FinnishMediaItem
{
    /// <summary>
    /// Prefixed ID such as "areena:1-1234567", "ruutu:ohjelma-slug", or "katsomo:ohjelma-slug".
    /// </summary>
    public string CompositeId { get; set; } = string.Empty;
    public string Source { get; set; } = string.Empty; // "YleAreena", "Ruutu", "Katsomo"
    public string Title { get; set; } = string.Empty;
    public string Overview { get; set; } = string.Empty;
    public int? ProductionYear { get; set; }
    public DateTime? PremiereDate { get; set; }
    public string? ImageUrl { get; set; }
    public string? PageUrl { get; set; }
    public List<string> Genres { get; set; } = new();
}

public sealed class FinnishEpisodeItem
{
    public string CompositeId { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string Overview { get; set; } = string.Empty;
    public int? SeasonNumber { get; set; }
    public int? EpisodeNumber { get; set; }
    public DateTime? AirDate { get; set; }
    public string? ThumbnailUrl { get; set; }
}
