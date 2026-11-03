using MediaBrowser.Model.Plugins;

namespace Jellyfin.Plugin.LibraryCovers.Configuration;

public class PluginConfiguration : BasePluginConfiguration
{
    /// <summary>Regenerate a cover automatically when the content of its library changes.</summary>
    public bool AutoUpdate { get; set; } = true;

    /// <summary>Delay after the last added/removed item before regenerating (avoids working during a library scan).</summary>
    public int DebounceSeconds { get; set; } = 120;

    /// <summary>Show the item count under the title.</summary>
    public bool ShowCount { get; set; } = true;

    /// <summary>Language of the words drawn on the covers: "auto" (server display language), "en" or "fr".</summary>
    public string Language { get; set; } = "auto";

    /// <summary>Per-library settings (empty = defaults).</summary>
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
