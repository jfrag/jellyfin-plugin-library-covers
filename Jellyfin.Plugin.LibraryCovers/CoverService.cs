using System.Globalization;
using System.Text;
using System.Text.Json;
using Jellyfin.Data.Enums;
using Jellyfin.Plugin.LibraryCovers.Configuration;
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

/// <summary>Réglages effectifs d'une médiathèque (renvoyés à la page de configuration).</summary>
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

/// <summary>Valeurs ponctuelles pour un aperçu non enregistré.</summary>
public sealed class CoverOverrides
{
    public string? Title { get; set; }

    public string? Accent { get; set; }

    public string? UnitSingular { get; set; }

    public string? UnitPlural { get; set; }
}

public class CoverService
{
    /// <summary>À incrémenter quand le dessin change : force la régénération de toutes les couvertures.</summary>
    private const int DesignVersion = 2;

    private static readonly CultureInfo Fr = CultureInfo.GetCultureInfo("fr-FR");

    // Couleurs et libellés par défaut pour des noms de médiathèques courants ; les autres prennent une couleur de la palette.
    private static readonly Dictionary<string, (string Accent, string? Title, string? Sing, string? Plur)> Known =
        new(StringComparer.OrdinalIgnoreCase)
        {
            ["Films"] = ("#5b8def", null, null, null),
            ["Séries"] = ("#a78bfa", null, null, null),
            ["Animes"] = ("#f472b6", null, null, null),
            ["Disney"] = ("#38bdf8", null, null, null),
            ["Films animes"] = ("#fb923c", null, null, null),
            ["Films enfants"] = ("#4ade80", null, null, null),
            ["Séries animes"] = ("#e879f9", null, null, null),
            ["Séries enfants"] = ("#facc15", null, null, null),
            ["Collections"] = ("#94a3b8", null, null, null),
            ["BayaM"] = ("#fdba74", null, "épisode", "épisodes"),
            ["Musiques"] = ("#22d3ee", null, null, null),
            ["Enregistrements"] = ("#f87171", null, null, null),
            ["Recorded Movies"] = ("#f87171", "Films enregistrés", null, null),
            ["TV en direct"] = ("#2dd4bf", null, null, null),
        };

    private static readonly string[] Palette =
    {
        "#5b8def", "#a78bfa", "#f472b6", "#38bdf8", "#fb923c", "#4ade80", "#e879f9", "#facc15", "#22d3ee", "#f87171",
    };

    private readonly ILibraryManager _libraryManager;
    private readonly IProviderManager _providerManager;
    private readonly ILocalizationManager _localization;
    private readonly ILogger<CoverService> _logger;
    private readonly SemaphoreSlim _lock = new(1, 1);

    public CoverService(ILibraryManager libraryManager, IProviderManager providerManager, ILocalizationManager localization, ILogger<CoverService> logger)
    {
        _libraryManager = libraryManager;
        _providerManager = providerManager;
        _localization = localization;
        _logger = logger;
    }

    private static PluginConfiguration Config => Plugin.Instance?.Configuration ?? new PluginConfiguration();

    /// <summary>Liste les médiathèques avec leurs réglages effectifs et leur compteur actuel.</summary>
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

    /// <summary>Aperçu JPEG d'une couverture, sans rien enregistrer.</summary>
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
    /// Régénère les couvertures. Sans <paramref name="force"/>, seules celles dont le titre, la couleur ou le compteur
    /// ont changé depuis la dernière génération sont refaites.
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
                        report.Add($"{lib.Name} : désactivée");
                        continue;
                    }

                    var signature = plan.Signature;
                    if (!force && state.TryGetValue(lib.Id, out var previous) && previous == signature)
                    {
                        report.Add($"{lib.Name} : inchangée");
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
                    var line = $"{lib.Name} : couverture régénérée ({plan.Info.Subtitle})";
                    report.Add(line);
                    _logger.LogInformation("Library Covers - {Line}", line);
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    report.Add($"{lib.Name} : erreur ({ex.Message})");
                    _logger.LogError(ex, "Library Covers - échec pour {Library}", lib.Name);
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

    /// <summary>Renvoie l'identifiant (format N) des médiathèques qui contiennent cet élément.</summary>
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

        // « TV en direct » n'est pas une médiathèque mais une vue nommée : même mécanisme que LiveTvManager.
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
            _logger.LogDebug(ex, "Library Covers - vue TV en direct introuvable");
            return null;
        }
    }

    private sealed record Plan(LibraryCoverInfo Info, CoverSpec Spec, string Signature);

    private Plan BuildPlan(BaseItem folder, string name, string collectionType, CoverOverrides? overrides)
    {
        var cfg = Config;
        var id = folder.Id.ToString("N");
        var settings = cfg.Libraries.FirstOrDefault(l => SameId(l.LibraryId, id));
        var normalizedName = name.Normalize(NormalizationForm.FormC);
        Known.TryGetValue(normalizedName, out var known);

        // Ce que l'on compte et ce que l'on affiche selon le type de médiathèque.
        BaseItemKind[] countKinds;
        BaseItemKind[] imageKinds;
        BaseItemKind[] fallbackImageKinds = Array.Empty<BaseItemKind>();
        string sing, plur;
        var style = TileStyle.Poster;
        switch (collectionType)
        {
            case "livetv":
                countKinds = new[] { BaseItemKind.TvChannel };
                imageKinds = countKinds;
                (sing, plur) = ("chaîne", "chaînes");
                style = TileStyle.Logo;
                break;
            case "movies":
                countKinds = new[] { BaseItemKind.Movie };
                imageKinds = countKinds;
                (sing, plur) = ("film", "films");
                break;
            case "tvshows":
                countKinds = new[] { BaseItemKind.Series };
                imageKinds = countKinds;
                fallbackImageKinds = new[] { BaseItemKind.Season };
                (sing, plur) = ("série", "séries");
                break;
            case "boxsets":
                countKinds = new[] { BaseItemKind.BoxSet };
                imageKinds = countKinds;
                (sing, plur) = ("collection", "collections");
                break;
            case "music":
                countKinds = new[] { BaseItemKind.MusicAlbum };
                imageKinds = countKinds;
                fallbackImageKinds = new[] { BaseItemKind.Audio };
                (sing, plur) = ("album", "albums");
                style = TileStyle.Square;
                break;
            case "musicvideos":
                countKinds = new[] { BaseItemKind.MusicVideo };
                imageKinds = countKinds;
                (sing, plur) = ("clip", "clips");
                break;
            default:
                countKinds = new[] { BaseItemKind.Movie, BaseItemKind.Series };
                imageKinds = countKinds;
                fallbackImageKinds = new[] { BaseItemKind.Season };
                (sing, plur) = ("titre", "titres");
                break;
        }

        // Les chaînes ne sont rattachées à aucun dossier : requête globale.
        List<BaseItem>? parents = collectionType == "livetv" ? null : new List<BaseItem> { folder };
        int count = Count(parents, countKinds);

        // Une seule série (ex. One Pace) ou un seul album : on compte plutôt les épisodes / pistes.
        if (count < 2)
        {
            if (collectionType is "tvshows" or "mixed")
            {
                int episodes = Count(parents, new[] { BaseItemKind.Episode });
                if (episodes > count)
                {
                    count = episodes;
                    (sing, plur) = ("épisode", "épisodes");
                }
            }
            else if (collectionType == "music")
            {
                int tracks = Count(parents, new[] { BaseItemKind.Audio });
                if (tracks > count)
                {
                    count = tracks;
                    (sing, plur) = ("piste", "pistes");
                }
            }
        }

        var defaultTitle = known.Title ?? name;
        var defaultAccent = known.Accent ?? Palette[StableHash(normalizedName) % Palette.Length];
        var defaultSing = known.Sing ?? sing;
        var defaultPlur = known.Plur ?? plur;

        string Pick(string? overrideValue, string? saved, string fallback) =>
            !string.IsNullOrWhiteSpace(overrideValue) ? overrideValue.Trim()
            : !string.IsNullOrWhiteSpace(saved) ? saved.Trim()
            : fallback;

        var title = Pick(overrides?.Title, settings?.Title, defaultTitle);
        var accent = Pick(overrides?.Accent, settings?.Accent, defaultAccent);
        var unitS = Pick(overrides?.UnitSingular, settings?.UnitSingular, defaultSing);
        var unitP = Pick(overrides?.UnitPlural, settings?.UnitPlural, defaultPlur);
        if (!SKColor.TryParse(accent, out var accentColor))
        {
            accent = defaultAccent;
            SKColor.TryParse(accent, out accentColor);
        }

        var subtitle = cfg.ShowCount && count > 0
            ? $"{count.ToString("N0", Fr)} {(count > 1 ? unitP : unitS)}"
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
            DefaultUnitSingular = defaultSing,
            DefaultUnitPlural = defaultPlur,
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
            // Logos dans l'ordre des chaînes (TF1, France 2…) plutôt que par note.
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
            // Logo de chaîne encore distant (URL du M3U) : Jellyfin le télécharge et le garde en cache.
            try
            {
                info = _libraryManager.ConvertImageToLocal(item, info, 0, true).GetAwaiter().GetResult();
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "Library Covers - image distante non récupérée pour {Item}", item.Name);
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
            _logger.LogWarning(ex, "Library Covers - état illisible, toutes les couvertures seront refaites");
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
            _logger.LogWarning(ex, "Library Covers - impossible d'écrire l'état");
        }
    }
}
