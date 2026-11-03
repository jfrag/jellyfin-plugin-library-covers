using Jellyfin.Data.Enums;
using MediaBrowser.Controller.Library;
using MediaBrowser.Model.Plugins;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.LibraryCovers;

/// <summary>
/// Listens for added/removed items and regenerates the affected covers once things calm down
/// (debounced: a scan that adds 50 episodes triggers a single regeneration).
/// </summary>
public sealed class CoverUpdateService : IHostedService, IDisposable
{
    private static readonly HashSet<BaseItemKind> Relevant = new()
    {
        BaseItemKind.Movie, BaseItemKind.Series, BaseItemKind.Season, BaseItemKind.Episode, BaseItemKind.BoxSet,
        BaseItemKind.MusicAlbum, BaseItemKind.Audio, BaseItemKind.MusicVideo, BaseItemKind.Video, BaseItemKind.TvChannel,
    };

    private readonly ILibraryManager _libraryManager;
    private readonly CoverService _coverService;
    private readonly ILogger<CoverUpdateService> _logger;
    private readonly object _gate = new();
    private readonly HashSet<string> _dirty = new();
    private bool _allDirty;
    private Timer? _timer;

    public CoverUpdateService(ILibraryManager libraryManager, CoverService coverService, ILogger<CoverUpdateService> logger)
    {
        _libraryManager = libraryManager;
        _coverService = coverService;
        _logger = logger;
    }

    public Task StartAsync(CancellationToken cancellationToken)
    {
        _timer = new Timer(_ => _ = RunAsync(), null, Timeout.Infinite, Timeout.Infinite);
        _libraryManager.ItemAdded += OnItemChanged;
        _libraryManager.ItemRemoved += OnItemChanged;
        if (Plugin.Instance is not null)
        {
            Plugin.Instance.ConfigurationChanged += OnConfigurationChanged;
        }

        // Startup check (catches up with changes made while the server was down).
        Schedule(null, TimeSpan.FromMinutes(2));
        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        _libraryManager.ItemAdded -= OnItemChanged;
        _libraryManager.ItemRemoved -= OnItemChanged;
        if (Plugin.Instance is not null)
        {
            Plugin.Instance.ConfigurationChanged -= OnConfigurationChanged;
        }

        _timer?.Change(Timeout.Infinite, Timeout.Infinite);
        return Task.CompletedTask;
    }

    public void Dispose()
    {
        _timer?.Dispose();
    }

    private void OnConfigurationChanged(object? sender, BasePluginConfiguration e)
    {
        // New titles/colours: apply them quickly.
        Schedule(null, TimeSpan.FromSeconds(5));
    }

    private void OnItemChanged(object? sender, ItemChangeEventArgs e)
    {
        var config = Plugin.Instance?.Configuration;
        if (config is null || !config.AutoUpdate || e.Item is null)
        {
            return;
        }

        if (!Relevant.Contains(e.Item.GetBaseItemKind()))
        {
            return;
        }

        IReadOnlyList<string>? ids = null;
        try
        {
            ids = _coverService.GetLibraryIdsFor(e.Item);
        }
        catch
        {
            // Item already gone: re-check every library.
        }

        Schedule(ids is { Count: > 0 } ? ids : null, TimeSpan.FromSeconds(Math.Clamp(config.DebounceSeconds, 10, 3600)));
    }

    private void Schedule(IReadOnlyList<string>? ids, TimeSpan delay)
    {
        lock (_gate)
        {
            if (ids is null)
            {
                _allDirty = true;
            }
            else
            {
                foreach (var id in ids)
                {
                    _dirty.Add(id);
                }
            }

            _timer?.Change(delay, Timeout.InfiniteTimeSpan);
        }
    }

    private async Task RunAsync()
    {
        List<string>? ids;
        lock (_gate)
        {
            ids = _allDirty ? null : _dirty.ToList();
            _allDirty = false;
            _dirty.Clear();
        }

        try
        {
            var report = await _coverService.RefreshAsync(false, ids, null, CancellationToken.None).ConfigureAwait(false);
            _logger.LogDebug("Library Covers - check: {Report}", string.Join(" ; ", report));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Library Covers - automatic update failed");
        }
    }
}
