# Jellyfin.Plugin.FinnishMetadata

Native Jellyfin C# metadata plugin for **Yle Areena** (`areena.yle.fi`), **Ruutu** (`ruutu.fi`), and **MTV Katsomo** (`katsomo.fi`), with built-in support for **FlickFetch** `.txt` sidecar descriptions and Finnish episode naming conventions.

## Supported Filename Formats

1. **Standard `SxxExx`:**
   - `Muistojeni Karjala (2026) - S01E01 - Lapsuus Karjalassa.mkv`
2. **Finnish `Kausi XX - Jakso YY` (with or without air date):**
   - `Sarjan Nimi - Kausi 01 - Jakso 03 - Jakson nimi.mkv`
   - `Sarjan Nimi - 2024-02-05 - Kausi 01 - Jakso 03 - Jakson nimi.mkv`
   - `Sarjan Nimi (2025) - Kausi 2 Jakso 4 - Jakson nimi.mkv`
3. **Finnish `Jakso YY` (defaults to Season 1 if `Kausi` is omitted):**
   - `Erittäin salainen - Vakoilua Suomessa - 2024-02-05 - Jakso 01 - Seksiä, rakkautta ja vakoilua.mkv`
4. **FlickFetch Sidecar `.txt` Files:**
   - Automatically reads `<video_filename>.txt` in the same folder for episode/movie descriptions (ignoring `.srt` subtitle files).

## Building the Plugin

Requires the **.NET 8.0 SDK** (for Jellyfin 10.9 / 10.10). If running Jellyfin 10.11+ (.NET 9), update `<TargetFramework>` in `Jellyfin.Plugin.FinnishMetadata/Jellyfin.Plugin.FinnishMetadata.csproj` to `net9.0`.

```bash
dotnet build Jellyfin.Plugin.FinnishMetadata.sln -c Release
```

## Installing in Jellyfin

1. Create a folder named `FinnishMetadata` inside your Jellyfin server `plugins` directory:
   - **Linux / Docker:** `/config/plugins/FinnishMetadata/` or `/var/lib/jellyfin/plugins/FinnishMetadata/`
   - **Windows:** `%ProgramData%\Jellyfin\Server\plugins\FinnishMetadata\`
2. Copy `Jellyfin.Plugin.FinnishMetadata/bin/Release/net8.0/Jellyfin.Plugin.FinnishMetadata.dll` into that directory.
3. Restart Jellyfin.
4. Go to **Dashboard -> Libraries -> [Your Library] -> Manage Library** and enable **Finnish Streaming (Areena, Ruutu, Katsomo)** under Series, Episode, Movie, and Image fetchers.
