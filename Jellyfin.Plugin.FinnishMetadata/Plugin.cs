using System;
using System.Collections.Generic;
using System.Globalization;
using Jellyfin.Plugin.FinnishMetadata.Configuration;
using MediaBrowser.Common.Configuration;
using MediaBrowser.Common.Plugins;
using MediaBrowser.Model.Plugins;
using MediaBrowser.Model.Serialization;

namespace Jellyfin.Plugin.FinnishMetadata;

public class Plugin : BasePlugin<PluginConfiguration>, IHasWebPages
{
    public const string ProviderName = "FinnishMetadata";
    public const string ProviderId = "FinnishMetadataId";

    public const string YleProviderId = "YleAreena";
    public const string RuutuProviderId = "Ruutu";
    public const string KatsomoProviderId = "Katsomo";

    public Plugin(IApplicationPaths applicationPaths, IXmlSerializer xmlSerializer)
        : base(applicationPaths, xmlSerializer)
    {
        Instance = this;
    }

    public override string Name => "Finnish Streaming Metadata (Areena, Ruutu, Katsomo)";

    public override Guid Id => Guid.Parse("b87c6a12-49e2-4f9b-9c2a-51f23c84a901");

    public static Plugin? Instance { get; private set; }

    public IEnumerable<PluginPageInfo> GetPages()
    {
        return new[]
        {
            new PluginPageInfo
            {
                Name = Name,
                EmbeddedResourcePath = string.Format(
                    CultureInfo.InvariantCulture,
                    "{0}.Configuration.configPage.html",
                    GetType().Namespace)
            }
        };
    }
}
