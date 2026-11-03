using MediaBrowser.Model.Plugins;

namespace Jellyfin.Plugin.LibraryCovers.Configuration;

public class PluginConfiguration : BasePluginConfiguration
{
    /// <summary>Régénère automatiquement une couverture quand le contenu de sa médiathèque change.</summary>
    public bool AutoUpdate { get; set; } = true;

    /// <summary>Délai d'attente après le dernier ajout/suppression avant de régénérer (évite de travailler pendant un scan).</summary>
    public int DebounceSeconds { get; set; } = 120;

    /// <summary>Affiche le nombre d'éléments sous le titre.</summary>
    public bool ShowCount { get; set; } = true;

    /// <summary>Réglages par médiathèque (vide = valeurs par défaut).</summary>
    public List<LibrarySettings> Libraries { get; set; } = new();
}

public class LibrarySettings
{
    public string LibraryId { get; set; } = string.Empty;

    public bool Enabled { get; set; } = true;

    public string? Title { get; set; }

    public string? Accent { get; set; }

    public string? UnitSingular { get; set; }

    public string? UnitPlural { get; set; }
}
