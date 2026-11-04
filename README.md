# Library Covers for Jellyfin

Library cover images rendered from your own posters, with an item count that **stays up to date**.

Each cover (1920×1080) shows a tilted wall of your posters, a midnight-blue fade, a glow in the library's colour, the library name in large type and an item count ("209 movies", "22 shows", "25 channels"…). When you add or remove media, the affected cover is redrawn on its own.

## Features

- **Every library type**: movies, shows, collections, music, music videos, books, mixed. The count adapts: movies, shows (or episodes when there is a single show), collections, albums (or tracks), books.
- **Retro games**: a Books library of ROMs managed by [JellyEmu](https://github.com/Jellyfin-PG/JellyEmu) is counted in games, with a wall of box art.
- **Live TV**: the Live TV view is not a library, so Jellyfin never generates an image for it. Library Covers gives it a cover too, with a wall of your channel logos and the number of channels.
- **Empty libraries** get a wall of cards tinted in the library's colour instead of a blank background.
- **Automatic updates**: the plugin listens for added and removed items, waits until things calm down (120 s by default, so a library scan triggers a single redraw), then regenerates only the covers whose count, title or colour changed.
- **Scheduled task** "Refresh library covers", daily at 4 am, plus a check two minutes after Jellyfin starts.
- **Settings page** (Dashboard → Plugins → Library Covers): per library, turn the cover on or off and set its title, colour and unit words, with an instant preview. A button regenerates everything.
- **English or French labels**: the words drawn on the covers follow the server's display language by default, or can be forced in the settings.

## Install from the catalogue (recommended)

1. Dashboard → **Plugins** → **Repositories** → **+**
2. Name: `Library Covers`, URL:
   ```
   https://raw.githubusercontent.com/jfrag/jellyfin-plugin-library-covers/main/manifest.json
   ```
3. In the **Catalogue**, install **Library Covers**, then restart Jellyfin.

New versions then show up in the catalogue like any official plugin.

## Manual install

Download `library-covers_<version>.zip` from the [releases](https://github.com/jfrag/jellyfin-plugin-library-covers/releases), unzip it into a `Library Covers_<version>` folder inside Jellyfin's `plugins` directory (e.g. `/var/lib/jellyfin/plugins/`), make it owned by the Jellyfin user, and restart.

## Compatibility and caveats

- Jellyfin **12.x** (targetAbi 12.0.0.0, .NET 10).
- Covers are saved as each library's primary image and **replace the existing one**. Back up any custom library images you care about first (they live in `root/default/<library>/` in Jellyfin's data directory).

## Building

With the .NET 10 SDK:

```sh
dotnet build Jellyfin.Plugin.LibraryCovers/Jellyfin.Plugin.LibraryCovers.csproj -c Release
```

Or with nothing but Docker: `./build.sh` (outputs `out/Library Covers_<version>/` and the `.zip`).

## Releasing

Update `version` and `changelog` in `meta.json` (and the version in the `.csproj`), then push to `main`.

The GitHub Actions workflow builds the plugin, creates the `vX.Y.Z.W` tag and a release with the `.zip`, and adds the version to `manifest.json`. It does nothing if that version has already been released.

## License

Code licensed under [GPL-3.0](LICENSE). Embedded fonts: [Unbounded](https://github.com/googlefonts/unbounded) and [Noto Sans](https://github.com/notofonts/latin-greek-cyrillic), under the SIL Open Font License 1.1 (see `Jellyfin.Plugin.LibraryCovers/Fonts/`).
