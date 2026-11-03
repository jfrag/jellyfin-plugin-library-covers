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

    /// <summary>Libraries with their effective settings and item counts.</summary>
    [HttpGet("Libraries")]
    public ActionResult<List<LibraryCoverInfo>> GetLibraries()
    {
        return _coverService.GetLibraries();
    }

    /// <summary>JPEG preview (nothing is saved).</summary>
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

    /// <summary>Regenerates every cover (force=true) or only the ones that changed.</summary>
    [HttpPost("Regenerate")]
    public async Task<ActionResult<List<string>>> Regenerate([FromQuery] bool force = true, [FromQuery] string? libraryId = null)
    {
        var ids = string.IsNullOrWhiteSpace(libraryId) ? null : new[] { libraryId };
        return await _coverService.RefreshAsync(force, ids, null, HttpContext.RequestAborted).ConfigureAwait(false);
    }
}
