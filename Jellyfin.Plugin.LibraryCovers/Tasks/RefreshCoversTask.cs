using MediaBrowser.Model.Tasks;

namespace Jellyfin.Plugin.LibraryCovers.Tasks;

public sealed class RefreshCoversTask : IScheduledTask
{
    private readonly CoverService _coverService;

    public RefreshCoversTask(CoverService coverService)
    {
        _coverService = coverService;
    }

    public string Name => "Mettre à jour les couvertures des médiathèques";

    public string Key => "LibraryCoversRefresh";

    public string Description => "Régénère les couvertures dont le nombre d'éléments, le titre ou la couleur a changé.";

    public string Category => "Library Covers";

    public Task ExecuteAsync(IProgress<double> progress, CancellationToken cancellationToken)
    {
        return _coverService.RefreshAsync(false, null, progress, cancellationToken);
    }

    public IEnumerable<TaskTriggerInfo> GetDefaultTriggers()
    {
        return new[]
        {
            new TaskTriggerInfo
            {
                Type = TaskTriggerInfoType.DailyTrigger,
                TimeOfDayTicks = TimeSpan.FromHours(4).Ticks,
            },
        };
    }
}
