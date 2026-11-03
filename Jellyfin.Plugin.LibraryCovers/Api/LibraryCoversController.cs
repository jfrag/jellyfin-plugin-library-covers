using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Jellyfin.Plugin.LibraryCovers.Api;

[ApiController]
[Route("LibraryCovers")]
[Authorize(Policy = "RequiresElevation")]
public class LibraryCoversController : ControllerBase
{
    private readonly CoverService _coverService;

    public LibraryCoversController(CoverService coverService)
    {
        _coverService = coverService;
    }

    /// <summary>Médiathèques, réglages effectifs et compteurs.</summary>
    [HttpGet("Libraries")]
    public ActionResult<List<LibraryCoverInfo>> GetLibraries()
    {
        return _coverService.GetLibraries();
    }

    /// <summary>Aperçu JPEG (rien n'est enregistré).</summary>
    [HttpGet("Preview/{libraryId}")]
    [Produces("image/jpeg")]
    public ActionResult GetPreview(
        [FromRoute] string libraryId,
        [FromQuery] string? title,
        [FromQuery] string? accent,
        [FromQuery] string? unitSingular,
        [FromQuery] string? unitPlural)
    {
        var bytes = _coverService.RenderPreview(libraryId, new CoverOverrides
        {
            Title = title,
            Accent = accent,
            UnitSingular = unitSingular,
            UnitPlural = unitPlural,
        });
        return bytes is null ? NotFound() : File(bytes, "image/jpeg");
    }

    /// <summary>Régénère tout (force=true) ou seulement ce qui a changé.</summary>
    [HttpPost("Regenerate")]
    public async Task<ActionResult<List<string>>> Regenerate([FromQuery] bool force = true, [FromQuery] string? libraryId = null)
    {
        var ids = string.IsNullOrWhiteSpace(libraryId) ? null : new[] { libraryId };
        return await _coverService.RefreshAsync(force, ids, null, HttpContext.RequestAborted).ConfigureAwait(false);
    }
}
