using System.Globalization;

namespace Jellyfin.Plugin.LibraryCovers;

/// <summary>Words drawn on the covers (item units), in the supported languages.</summary>
internal static class Labels
{
    private static readonly Dictionary<string, (string EnOne, string EnMany, string FrOne, string FrMany)> Units =
        new(StringComparer.Ordinal)
        {
            ["channel"] = ("channel", "channels", "chaîne", "chaînes"),
            ["movie"] = ("movie", "movies", "film", "films"),
            ["series"] = ("show", "shows", "série", "séries"),
            ["collection"] = ("collection", "collections", "collection", "collections"),
            ["album"] = ("album", "albums", "album", "albums"),
            ["musicvideo"] = ("music video", "music videos", "clip", "clips"),
            ["title"] = ("title", "titles", "titre", "titres"),
            ["episode"] = ("episode", "episodes", "épisode", "épisodes"),
            ["track"] = ("track", "tracks", "piste", "pistes"),
        };

    /// <summary>
    /// Resolves the cover language: the plugin setting, or the server display language when set to "auto".
    /// Returns "fr" or "en".
    /// </summary>
    public static string ResolveLanguage(string? setting, string? serverCulture)
    {
        var lang = string.IsNullOrWhiteSpace(setting) || setting.Equals("auto", StringComparison.OrdinalIgnoreCase)
            ? serverCulture
            : setting;
        return lang is not null && lang.StartsWith("fr", StringComparison.OrdinalIgnoreCase) ? "fr" : "en";
    }

    public static (string Singular, string Plural) Unit(string key, string language)
    {
        var u = Units[key];
        return language == "fr" ? (u.FrOne, u.FrMany) : (u.EnOne, u.EnMany);
    }

    public static CultureInfo Culture(string language)
    {
        return language == "fr" ? CultureInfo.GetCultureInfo("fr-FR") : CultureInfo.GetCultureInfo("en-US");
    }
}
