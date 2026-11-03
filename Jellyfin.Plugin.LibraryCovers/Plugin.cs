using Jellyfin.Plugin.LibraryCovers.Configuration;
using MediaBrowser.Common.Configuration;
using MediaBrowser.Common.Plugins;
using MediaBrowser.Model.Plugins;
using MediaBrowser.Model.Serialization;

namespace Jellyfin.Plugin.LibraryCovers;

/// <summary>
/// Library Covers: renders library cover images (poster wall + title + item count)
/// and keeps them up to date when the library content changes.
/// </summary>
public class Plugin : BasePlugin<PluginConfiguration>, IHasWebPages
{
    public Plugin(IApplicationPaths applicationPaths, IXmlSerializer xmlSerializer)
        : base(applicationPaths, xmlSerializer)
    {
        Instance = this;
    }

    public static Plugin? Instance { get; private set; }

    public override string Name => "Library Covers";

    public override Guid Id => Guid.Parse("0ef768ea-c398-447f-aba3-6fdd97de7b02");

    public override string Description =>
        "Library cover images rendered from your own posters, with an item count that stays up to date.";

    public IEnumerable<PluginPageInfo> GetPages()
    {
        return new[]
        {
            new PluginPageInfo
            {
                Name = Name,
                EmbeddedResourcePath = GetType().Namespace + ".Configuration.configPage.html"
            }
        };
    }
}
