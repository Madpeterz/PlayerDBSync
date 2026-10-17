# PlayerDB Sync

A small Windows app that uploads the data collected by the **PlayerDB**, **HomeDB**, **RegradeDB** and **RaidsDB** ArcheAge Classic addons to the PlayerDB server.

It watches the addons' data folders, and every 5 minutes uploads any files that have changed:

| Addon     | Folder it reads       | Sent to                 |
|-----------|-----------------------|-------------------------|
| PlayerDB  | `PlayerDB\Players`    | `<server>/api/players`  |
| HomeDB    | `HomeDB\Houses`       | `<server>/api/homes`    |
| RegradeDB | `RegradeDB\Regrades`  | `<server>/api/regrades` |
| RaidsDB   | `RaidsDB\Raids`       | `<server>/api/raids`    |

You can sync any of the addons; leave the others empty.

## Requirements

- Windows 10 or 11 (64-bit). Nothing else to install; the .NET runtime is bundled.
- At least one of the PlayerDB, HomeDB, RegradeDB or RaidsDB addons installed in ArcheAge Classic.
- Your PlayerDB **user ID** and **API key**.

## Setup

1. **Download** `PlayerDBSync.exe` from the [latest release](https://github.com/Madpeterz/PlayerDBSync/releases/latest) and put it anywhere you like.

   The exe isn't code-signed, so Windows SmartScreen may warn the first time you run it. Click **More info → Run anyway**.

2. **Run it** and fill in the form:

   | Field              | What to enter |
   |--------------------|---------------|
   | **PlayerDB addon** | The PlayerDB addon folder: the one containing `main.lua` and a `Players` folder. Leave empty to skip player uploads. |
   | **HomeDB addon**   | The HomeDB addon folder: the one containing `main.lua` and a `Houses` folder. Leave empty to skip house uploads. |
   | **RegradeDB addon** | The RegradeDB addon folder: the one containing `main.lua` and a `Regrades` folder. Leave empty to skip regrade uploads. |
   | **RaidsDB addon**  | The RaidsDB addon folder: the one containing `main.lua` and a `Raids` folder. Leave empty to skip raid uploads. |
   | **Server URL**     | The server's base address, e.g. `https://playerdb.blackatom.win` (no `/api/...` on the end). |
   | **User ID**        | Your PlayerDB user ID (a number). |
   | **API key**        | Your PlayerDB API key. |

   Use **Browse…** to pick each folder. Addons usually live in one of:

   - `C:\Users\<you>\Documents\AAClassic\Addon\`
   - `C:\AAClassic\Documents\Addon\`

   If you pick the `Players`, `Houses`, `Regrades` or `Raids` folder by mistake, the app selects the addon folder above it for you.

3. **Click Start.** The app uploads everything that isn't on the server yet, then checks for changes every 5 minutes. The log shows what it uploads.

Next time you open the app, it starts syncing straight away with the saved settings. Click **Stop** to change them.

## How it works

- Only changed files are uploaded. The app remembers what it has already sent, so restarting it doesn't upload everything again. Changing a folder, the server URL or the user ID does re-upload everything for that addon.
- Changed files are sent in batches of up to 500.
- Bookkeeping files (`_count.dat`, `empty.keep`) are skipped.
- Raids are uploaded once the recording is stopped and saved to `Raids`; a raid still being recorded isn't sent.
- If the server can't be reached or refuses an upload, the app waits longer between attempts (5, 10, 20, then 30 minutes) and logs the error once instead of every time. Each addon retries on its own, so a problem with one doesn't hold up the others.
- If the server rejects one particular file, that file is skipped until it changes or the app is restarted.

## Raid item ignore list

While RaidsDB is synced, the app downloads the server's list of raid items to ignore when it starts syncing and then every 15 minutes, and saves it as `RaidsDB\ignore.dat` for the addon to read. The file is only rewritten when the list changes:

```lua
{
    items = {
        ["Ash Shard"] = true,
        ["Schematic Scrap"] = true,
    },
}
```

## Raid screenshots

Tick **Save screenshot on RaidsDB request** to let the RaidsDB addon ask for screenshots. While syncing, the app checks every 2 seconds for `RaidsDB\Raids\request.sht`. When it finds one, it captures the `archeage.exe` window and deletes the request once the screenshot is saved. If the capture fails (for example the game is minimised), the request is kept and retried every 2 seconds.

Screenshots are saved to `%TEMP%\PlayerDBSync\Screenshots`, named after the start time of the raid being recorded in `RaidsDB\current.dat`, numbered from 1: `2026-10-17_19-07-20-1.webp`, `2026-10-17_19-07-20-2.webp`, and so on. They're saved as lossy WebP (quality 80) to keep uploads small. Captures larger than 1920×1080 are scaled down to fit, keeping their aspect ratio. The start time is in UTC, the same as the addon's saved raid files. If no raid is recording, the current time is used.

While RaidsDB is synced, the app uploads the screenshots in that folder every 30 seconds. When sync starts, any screenshot still waiting that's larger than 1920×1080 is scaled down first. Uploads go oldest first, up to 3 at a time. Once the server has a screenshot it's deleted, so anything left in the folder is still waiting to be sent. If the server rejects a screenshot (for example because it's too large), that file is skipped until it changes or the app is restarted. If the upload fails, it's tried again on the next 30-second tick.

## Settings

Settings are saved in `%AppData%\PlayerDBSync\settings.json`. The API key is encrypted so only your Windows account can read it. To start fresh, close the app and delete that file.

## Troubleshooting

| Message | What to do |
|---------|------------|
| *This doesn't look like the PlayerDB/HomeDB/RegradeDB/RaidsDB addon folder* | Pick the addon's own folder, the one with `main.lua` in it. If the `Players`, `Houses`, `Regrades` or `Raids` folder doesn't exist yet, run the game with the addon enabled first so it creates it. |
| *Server refused every upload … check the user ID, API key and PC clock* | Check your user ID and API key. Uploads are signed with your PC's time, so make sure Windows has the right time (Settings → Time & language → **Sync now**). |
| *Failing (3x) – next try …* | The server is down or unreachable. The app keeps retrying on its own; check the Server URL if it never recovers. |

## Building from source

Requires the [.NET 10 SDK](https://dotnet.microsoft.com/download).

```sh
dotnet build
```

To build the same single-file exe as the releases:

```sh
dotnet publish PlayerDBSync.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:EnableCompressionInSingleFile=true -o publish
```

### Releasing

Push a version tag (e.g. `1.2.0`, or `1.2.0-beta` for a pre-release). The GitHub Actions workflow builds `PlayerDBSync.exe` and publishes it as a release.
