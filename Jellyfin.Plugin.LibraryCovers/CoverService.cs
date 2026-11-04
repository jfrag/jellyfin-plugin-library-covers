using System.Globalization;
using System.Text;
using System.Text.Json;
using Jellyfin.Data.Enums;
using Jellyfin.Plugin.LibraryCovers.Configuration;
using MediaBrowser.Controller.Configuration;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Library;
using MediaBrowser.Controller.LiveTv;
using MediaBrowser.Controller.Providers;
using MediaBrowser.Model.Entities;
using MediaBrowser.Model.Globalization;
using Microsoft.Extensions.Logging;
using SkiaSharp;
using SortOrder = Jellyfin.Database.Implementations.Enums.SortOrder;

namespace Jellyfin.Plugin.LibraryCovers;

/// <summary>Effective settings of a library, as shown on the configuration page.</summary>
public sealed class LibraryCoverInfo
{
    public string Id { get; set; } = string.Empty;

    public string Name { get; set; } = string.Empty;

    public string CollectionType { get; set; } = string.Empty;

    public bool Enabled { get; set; }

    public string Title { get; set; } = string.Empty;

    public string Accent { get; set; } = string.Empty;

    public string UnitSingular { get; set; } = string.Empty;

    public string UnitPlural { get; set; } = string.Empty;

    public int Count { get; set; }

    public string Subtitle { get; set; } = string.Empty;

    public string DefaultTitle { get; set; } = string.Empty;

    public string DefaultAccent { get; set; } = string.Empty;

    public string DefaultUnitSingular { get; set; } = string.Empty;

    public string DefaultUnitPlural { get; set; } = string.Empty;
}

/// <summary>One-off values for an unsaved preview.</summary>
public sealed class CoverOverrides
{
    public string? Title { get; set; }

    public string? Accent { get; set; }

    public string? UnitSingular { get; set; }

    public string? UnitPlural { get; set; }
}

public class CoverService
{
    /// <summary>Bump when the drawing changes: forces every cover to be regenerated.</summary>
    private const int DesignVersion = 2;

    /// <summary>Default colour (and optional French title or unit) for common library names.</summary>
    private sealed record KnownLibrary(string Accent, string? FrenchTitle = null, string? Unit = null);

    private static readonly Dictionary<string, KnownLibrary> Known = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Movies"] = new("#5b8def"),
        ["Films"] = new("#5b8def"),
        ["Shows"] = new("#a78bfa"),
        ["TV Shows"] = new("#a78bfa"),
        ["Séries"] = new("#a78bfa"),
        ["Anime"] = new("#f472b6"),
        ["Animes"] = new("#f472b6"),
        ["Disney"] = new("#38bdf8"),
        ["Anime Movies"] = new("#fb923c"),
        ["Films animes"] = new("#fb923c"),
        ["Kids Movies"] = new("#4ade80"),
        ["Films enfants"] = new("#4ade80"),
        ["Anime Series"] = new("#e879f9"),
        ["Séries animes"] = new("#e879f9"),
        ["Kids Shows"] = new("#facc15"),
        ["Séries enfants"] = new("#facc15"),
        ["Collections"] = new("#94a3b8"),
        ["BayaM"] = new("#fdba74", Unit: "episode"),
        ["Music"] = new("#22d3ee"),
        ["Musiques"] = new("#22d3ee"),
        ["Recordings"] = new("#f87171"),
        ["Enregistrements"] = new("#f87171"),
        ["Recorded Movies"] = new("#f87171", FrenchTitle: "Films enregistrés"),
        ["Live TV"] = new("#2dd4bf"),
        ["TV en direct"] = new("#2dd4bf"),
        ["Games"] = new("#a3e635"),
        ["Retro Games"] = new("#a3e635"),
        ["Jeux"] = new("#a3e635"),
        ["Jeux rétro"] = new("#a3e635"),
        ["Books"] = new("#fbbf24"),
        ["Livres"] = new("#fbbf24"),
    };

    private static readonly string[] Palette =
    {
        "#5b8def", "#a78bfa", "#f472b6", "#38bdf8", "#fb923c", "#4ade80", "#e879f9", "#facc15", "#22d3ee", "#f87171",
    };

    private readonly ILibraryManager _libraryManager;
    private readonly IProviderManager _providerManager;
    private readonly ILocalizationManager _localization;
    private readonly IServerConfigurationManager _serverConfig;
    private readonly ILogger<CoverService> _logger;
    private readonly SemaphoreSlim _lock = new(1, 1);

    public CoverService(
        ILibraryManager libraryManager,
        IProviderManager providerManager,
        ILocalizationManager localization,
        IServerConfigurationManager serverConfig,
        ILogger<CoverService> logger)
    {
        _libraryManager = libraryManager;
        _providerManager = providerManager;
        _localization = localization;
        _serverConfig = serverConfig;
        _logger = logger;
    }

    private static PluginConfiguration Config => Plugin.Instance?.Configuration ?? new PluginConfiguration();

    /// <summary>Lists the libraries with their effective settings and current item count.</summary>
    public List<LibraryCoverInfo> GetLibraries()
    {
        var list = new List<LibraryCoverInfo>();
        foreach (var lib in EnumerateLibraries())
        {
            var plan = BuildPlan(lib.Folder, lib.Name, lib.CollectionType, null);
            list.Add(plan.Info);
        }

        return list;
    }

    /// <summary>Renders a JPEG preview of a cover without saving anything.</summary>
    public byte[]? RenderPreview(string libraryId, CoverOverrides? overrides)
    {
        var lib = EnumerateLibraries().FirstOrDefault(l => SameId(l.Id, libraryId));
        if (lib.Folder is null)
        {
            return null;
        }

        var plan = BuildPlan(lib.Folder, lib.Name, lib.CollectionType, overrides);
        return CoverRenderer.Render(plan.Spec);
    }

    /// <summary>
    /// Regenerates the covers. Without <paramref name="force"/>, only the covers whose title, colour or item count
    /// changed since they were last generated are redrawn.
    /// </summary>
    public async Task<List<string>> RefreshAsync(bool force, IReadOnlyCollection<string>? onlyIds, IProgress<double>? progress, CancellationToken cancellationToken)
    {
        var report = new List<string>();
        await _lock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var state = LoadState();
            var libs = EnumerateLibraries()
                .Where(l => onlyIds is null || onlyIds.Count == 0 || onlyIds.Any(id => SameId(id, l.Id)))
                .ToList();
            int done = 0;
            foreach (var lib in libs)
            {
                cancellationToken.ThrowIfCancellationRequested();
                try
                {
                    var plan = BuildPlan(lib.Folder, lib.Name, lib.CollectionType, null);
                    if (!plan.Info.Enabled)
                    {
                        report.Add($"{lib.Name}: disabled");
                        continue;
                    }

                    var signature = plan.Signature;
                    if (!force && state.TryGetValue(lib.Id, out var previous) && previous == signature)
                    {
                        report.Add($"{lib.Name}: unchanged");
                        continue;
                    }

                    var bytes = CoverRenderer.Render(plan.Spec);
                    using (var ms = new MemoryStream(bytes))
                    {
                        await _providerManager.SaveImage(lib.Folder, ms, "image/jpeg", ImageType.Primary, null, cancellationToken).ConfigureAwait(false);
                    }

                    await lib.Folder.UpdateToRepositoryAsync(ItemUpdateType.ImageUpdate, cancellationToken).ConfigureAwait(false);
                    state[lib.Id] = signature;
                    SaveState(state);
                    var line = $"{lib.Name}: cover regenerated ({plan.Info.Subtitle})";
                    report.Add(line);
                    _logger.LogInformation("Library Covers - {Line}", line);
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    report.Add($"{lib.Name}: error ({ex.Message})");
                    _logger.LogError(ex, "Library Covers - failed for {Library}", lib.Name);
                }
                finally
                {
                    done++;
                    progress?.Report(100.0 * done / Math.Max(1, libs.Count));
                }
            }
        }
        finally
        {
            _lock.Release();
        }

        return report;
    }

    /// <summary>Returns the ids (N format) of the libraries that contain this item.</summary>
    public IReadOnlyList<string> GetLibraryIdsFor(BaseItem item)
    {
        return _libraryManager.GetCollectionFolders(item).Select(f => f.Id.ToString("N")).ToList();
    }

    private IEnumerable<(string Id, string Name, string CollectionType, BaseItem Folder)> EnumerateLibraries()
    {
        foreach (var vf in _libraryManager.GetVirtualFolders())
        {
            if (!Guid.TryParse(vf.ItemId, out var guid))
            {
                continue;
            }

            var folder = _libraryManager.GetItemById(guid);
            if (folder is null)
            {
                continue;
            }

            var ct = vf.CollectionType?.ToString().ToLowerInvariant() ?? "mixed";
            yield return (guid.ToString("N"), vf.Name ?? folder.Name, ct, folder);
        }

        // Live TV is not a library but a named view: same lookup as LiveTvManager.
        var liveTv = GetLiveTvView();
        if (liveTv is not null)
        {
            yield return (liveTv.Id.ToString("N"), liveTv.Name, "livetv", liveTv);
        }
    }

    private BaseItem? GetLiveTvView()
    {
        try
        {
            var hasChannels = _libraryManager.GetItemList(new InternalItemsQuery
            {
                IncludeItemTypes = new[] { BaseItemKind.TvChannel },
                Limit = 1,
            }).Count > 0;
            if (!hasChannels)
            {
                return null;
            }

            var name = _localization.GetLocalizedString("HeaderLiveTV");
            return _libraryManager.GetNamedView(name, CollectionType.livetv, name);
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Library Covers - Live TV view not found");
            return null;
        }
    }

    private sealed record Plan(LibraryCoverInfo Info, CoverSpec Spec, string Signature);

    private Plan BuildPlan(BaseItem folder, string name, string collectionType, CoverOverrides? overrides)
    {
        var cfg = Config;
        var language = Labels.ResolveLanguage(cfg.Language, _serverConfig.Configuration.UICulture);
        var id = folder.Id.ToString("N");
        var settings = cfg.Libraries.FirstOrDefault(l => SameId(l.LibraryId, id));
        var normalizedName = name.Normalize(NormalizationForm.FormC);
        Known.TryGetValue(normalizedName, out var known);

        // What to count and which images to show, by library type.
        BaseItemKind[] countKinds;
        BaseItemKind[] imageKinds;
        BaseItemKind[] fallbackImageKinds = Array.Empty<BaseItemKind>();
        string unit;
        var style = TileStyle.Poster;
        switch (collectionType)
        {
            case "livetv":
                countKinds = new[] { BaseItemKind.TvChannel };
                imageKinds = countKinds;
                unit = "channel";
                style = TileStyle.Logo;
                break;
            case "movies":
                countKinds = new[] { BaseItemKind.Movie };
                imageKinds = countKinds;
                unit = "movie";
                break;
            case "tvshows":
                countKinds = new[] { BaseItemKind.Series };
                imageKinds = countKinds;
                fallbackImageKinds = new[] { BaseItemKind.Season };
                unit = "series";
                break;
            case "boxsets":
                countKinds = new[] { BaseItemKind.BoxSet };
                imageKinds = countKinds;
                unit = "collection";
                break;
            case "music":
                countKinds = new[] { BaseItemKind.MusicAlbum };
                imageKinds = countKinds;
                fallbackImageKinds = new[] { BaseItemKind.Audio };
                unit = "album";
                style = TileStyle.Square;
                break;
            case "books":
                // Books libraries also hold retro games when JellyEmu is installed (it tags each ROM "JellyEmu").
                countKinds = new[] { BaseItemKind.Book };
                imageKinds = countKinds;
                unit = "book";
                break;
            case "musicvideos":
                countKinds = new[] { BaseItemKind.MusicVideo };
                imageKinds = countKinds;
                unit = "musicvideo";
                break;
            default:
                countKinds = new[] { BaseItemKind.Movie, BaseItemKind.Series };
                imageKinds = countKinds;
                fallbackImageKinds = new[] { BaseItemKind.Season };
                unit = "title";
                break;
        }

        // Channels do not belong to any folder: query them globally.
        List<BaseItem>? parents = collectionType == "livetv" ? null : new List<BaseItem> { folder };
        int count = Count(parents, countKinds);

        if (collectionType == "books" && count > 0 && CountTagged(parents, countKinds, "JellyEmu") * 2 > count)
        {
            unit = "game";
        }

        // A single show or a single album: count its episodes or tracks instead.
        if (count < 2)
        {
            if (collectionType is "tvshows" or "mixed")
            {
                int episodes = Count(parents, new[] { BaseItemKind.Episode });
                if (episodes > count)
                {
                    count = episodes;
                    unit = "episode";
                }
            }
            else if (collectionType == "music")
            {
                int tracks = Count(parents, new[] { BaseItemKind.Audio });
                if (tracks > count)
                {
                    count = tracks;
                    unit = "track";
                }
            }
        }

        var (sing, plur) = Labels.Unit(known?.Unit ?? unit, language);
        var defaultTitle = language == "fr" && known?.FrenchTitle is not null ? known.FrenchTitle : name;
        var defaultAccent = known?.Accent ?? Palette[StableHash(normalizedName) % Palette.Length];

        string Pick(string? overrideValue, string? saved, string fallback) =>
            !string.IsNullOrWhiteSpace(overrideValue) ? overrideValue.Trim()
            : !string.IsNullOrWhiteSpace(saved) ? saved.Trim()
            : fallback;

        var title = Pick(overrides?.Title, settings?.Title, defaultTitle);
        var accent = Pick(overrides?.Accent, settings?.Accent, defaultAccent);
        var unitS = Pick(overrides?.UnitSingular, settings?.UnitSingular, sing);
        var unitP = Pick(overrides?.UnitPlural, settings?.UnitPlural, plur);
        if (!SKColor.TryParse(accent, out var accentColor))
        {
            accent = defaultAccent;
            SKColor.TryParse(accent, out accentColor);
        }

        var subtitle = cfg.ShowCount && count > 0
            ? $"{count.ToString("N0", Labels.Culture(language))} {(count > 1 ? unitP : unitS)}"
            : string.Empty;

        var images = GetImagePaths(parents, imageKinds);
        if (images.Count < 6 && fallbackImageKinds.Length > 0)
        {
            images = images.Concat(GetImagePaths(parents, fallbackImageKinds)).Distinct().ToList();
        }

        var info = new LibraryCoverInfo
        {
            Id = id,
            Name = name,
            CollectionType = collectionType,
            Enabled = settings?.Enabled ?? true,
            Title = title,
            Accent = accent,
            UnitSingular = unitS,
            UnitPlural = unitP,
            Count = count,
            Subtitle = subtitle,
            DefaultTitle = defaultTitle,
            DefaultAccent = defaultAccent,
            DefaultUnitSingular = sing,
            DefaultUnitPlural = plur,
        };

        var spec = new CoverSpec
        {
            Title = title,
            Accent = accentColor,
            Subtitle = subtitle,
            Style = style,
            ImagePaths = images,
            Seed = StableHash(normalizedName + "|" + count),
        };

        var signature = string.Join('|', DesignVersion, title, accent, subtitle, style, count, images.Count);
        return new Plan(info, spec, signature);
    }

    private IReadOnlyList<BaseItem> Query(InternalItemsQuery query, List<BaseItem>? parents)
    {
        return parents is null ? _libraryManager.GetItemList(query) : _libraryManager.GetItemList(query, parents);
    }

    private int Count(List<BaseItem>? parents, BaseItemKind[] kinds)
    {
        var query = new InternalItemsQuery
        {
            IncludeItemTypes = kinds,
            Recursive = true,
            IsVirtualItem = false,
        };
        return Query(query, parents).Count;
    }

    private int CountTagged(List<BaseItem>? parents, BaseItemKind[] kinds, string tag)
    {
        var query = new InternalItemsQuery
        {
            IncludeItemTypes = kinds,
            Recursive = true,
            IsVirtualItem = false,
            Tags = new[] { tag },
        };
        return Query(query, parents).Count;
    }

    private List<string> GetImagePaths(List<BaseItem>? parents, BaseItemKind[] kinds)
    {
        var query = new InternalItemsQuery
        {
            IncludeItemTypes = kinds,
            Recursive = true,
            IsVirtualItem = false,
            ImageTypes = new[] { ImageType.Primary },
            OrderBy = new[]
            {
                (ItemSortBy.CommunityRating, SortOrder.Descending),
                (ItemSortBy.SortName, SortOrder.Ascending),
            },
            Limit = 40,
        };

        var items = Query(query, parents);
        if (kinds.Contains(BaseItemKind.TvChannel))
        {
            // Channel logos in channel-number order rather than by rating.
            items = items.OrderBy(i => ChannelNumber(i)).ToList();
        }

        var paths = new List<string>();
        foreach (var item in items)
        {
            var path = LocalImagePath(item);
            if (path is not null)
            {
                paths.Add(path);
            }
        }

        return paths.Distinct().ToList();
    }

    private string? LocalImagePath(BaseItem item)
    {
        var info = item.GetImageInfo(ImageType.Primary, 0);
        if (info is null || string.IsNullOrEmpty(info.Path))
        {
            return null;
        }

        if (info.Path.Contains("://", StringComparison.Ordinal))
        {
            // Channel logo still remote (URL from the M3U tuner): let Jellyfin download and cache it.
            try
            {
                info = _libraryManager.ConvertImageToLocal(item, info, 0, true).GetAwaiter().GetResult();
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "Library Covers - could not fetch remote image for {Item}", item.Name);
                return null;
            }
        }

        return info is not null && !string.IsNullOrEmpty(info.Path) && File.Exists(info.Path) ? info.Path : null;
    }

    private static double ChannelNumber(BaseItem item)
    {
        var number = (item as LiveTvChannel)?.Number;
        return double.TryParse(number, NumberStyles.Float, CultureInfo.InvariantCulture, out var n) ? n : double.MaxValue;
    }

    private static bool SameId(string? a, string? b)
    {
        return Guid.TryParse(a, out var ga) && Guid.TryParse(b, out var gb) && ga == gb;
    }

    private static int StableHash(string s)
    {
        unchecked
        {
            int h = 7;
            foreach (var ch in s)
            {
                h = (h * 31) + ch;
            }

            return h & 0x7fffffff;
        }
    }

    private static string StatePath
    {
        get
        {
            var dir = Plugin.Instance?.DataFolderPath ?? Path.GetTempPath();
            Directory.CreateDirectory(dir);
            return Path.Combine(dir, "state.json");
        }
    }

    private Dictionary<string, string> LoadState()
    {
        try
        {
            if (File.Exists(StatePath))
            {
                return JsonSerializer.Deserialize<Dictionary<string, string>>(File.ReadAllText(StatePath)) ?? new();
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Library Covers - unreadable state file, every cover will be regenerated");
        }

        return new Dictionary<string, string>();
    }

    private void SaveState(Dictionary<string, string> state)
    {
        try
        {
            File.WriteAllText(StatePath, JsonSerializer.Serialize(state));
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Library Covers - could not write the state file");
        }
    }
}
