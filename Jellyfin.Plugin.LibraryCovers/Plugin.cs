using Jellyfin.Plugin.LibraryCovers.Configuration;
using MediaBrowser.Common.Configuration;
using MediaBrowser.Common.Plugins;
using MediaBrowser.Model.Plugins;
using MediaBrowser.Model.Serialization;

namespace Jellyfin.Plugin.LibraryCovers;

/// <summary>
/// Library Covers : génère les couvertures des médiathèques (mur d'affiches + titre + nombre d'éléments)
/// et les tient à jour quand le contenu change.
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
        "Couvertures de médiathèques générées à partir de vos affiches, avec le nombre de films/séries tenu à jour automatiquement.";

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
