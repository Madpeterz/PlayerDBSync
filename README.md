# PlayerDB Sync

A small Windows app that uploads the data collected by the **PlayerDB** and **RaidsDB** ArcheAge Classic addons to the PlayerDB server.

It watches the addons' data folders, and every 5 minutes uploads any files that have changed:

| Addon     | Folder it reads       | Sent to                 |
|-----------|-----------------------|-------------------------|
| PlayerDB  | `PlayerDB\Players`    | `<server>/api/players`  |
| PlayerDB  | `PlayerDB\Houses`     | `<server>/api/homes`    |
| PlayerDB  | `PlayerDB\Regrades`   | `<server>/api/regrades` |
| PlayerDB  | `PlayerDB\Inventory`  | `<server>/api/inventory` |
| RaidsDB   | `RaidsDB\Raids`       | `<server>/api/raids`    |
| PlayerDB  | `PlayerDB\Demos`      | `<server>/api/demoocr` (screenshots, see [Demo OCR screenshots](#demo-ocr-screenshots)) |

You can sync either addon; leave the other empty. Houses and regrades used to come from the separate HomeDB and RegradeDB addons; both are now defunct, merged into PlayerDB (see [Upgrading from HomeDB and RegradeDB](#upgrading-from-homedb-and-regradedb)).

## Requirements

- Windows 10 or 11 (64-bit). Nothing else to install; the .NET runtime is bundled.
- The PlayerDB or RaidsDB addon (or both) installed in ArcheAge Classic.
- Your PlayerDB **user ID** and **API key**.

## Setup

1. **Download** `PlayerDBSync.exe` from the [latest release](https://github.com/Madpeterz/PlayerDBSync/releases/latest) and put it anywhere you like.

   The exe isn't code-signed, so Windows SmartScreen may warn the first time you run it. Click **More info → Run anyway**.

2. **Run it** and fill in the form:

   | Field              | What to enter |
   |--------------------|---------------|
   | **PlayerDB addon** | The PlayerDB addon folder: the one containing `main.lua` and the `Players`, `Houses`, `Regrades` and `Inventory` folders. Each of those is synced if it's there; one the addon hasn't made yet is skipped until you stop and start again. Leave empty to skip player, house, regrade and inventory uploads. |
   | **RaidsDB addon**  | The RaidsDB addon folder: the one containing `main.lua` and a `Raids` folder. Leave empty to skip raid uploads. |
   | **Server URL**     | The server's base address, e.g. `https://playerdb.blackatom.win` (no `/api/...` on the end). |
   | **User ID**        | Your PlayerDB user ID (a number). |
   | **API key**        | Your PlayerDB API key. |

   Use **Browse…** to pick each folder. Addons usually live in one of:

   - `C:\Users\<you>\Documents\AAClassic\Addon\`
   - `C:\AAClassic\Documents\Addon\`

   If you pick the `Players`, `Houses`, `Regrades`, `Inventory` or `Raids` folder by mistake, the app selects the addon folder above it for you.

   Under each folder the addon's version is shown in green (e.g. `2.5.0`), read from `addon_version` in its most recently written data file. It's read again whenever the folder changes and after each upload, so it follows addon updates. Nothing is shown if the folder isn't an addon's yet, or if its newest five data files have no version.

3. **Click Start.** The app uploads everything that isn't on the server yet, then checks for changes every 5 minutes. The log shows what it uploads.

Next time you open the app, it starts syncing straight away with the saved settings. Click **Stop** to change them.

## How it works

- Only changed files are uploaded. The app remembers what it has already sent, so restarting it doesn't upload everything again. Changing a folder, the server URL or the user ID does re-upload everything for that addon.
- Changed files are sent in batches of up to 500.
- Bookkeeping files (`_count.dat`, `_files.dat`, `empty.keep`) are skipped.
- Raids are uploaded once the recording is stopped and saved to `Raids`; a raid still being recorded isn't sent.
- If the server can't be reached or refuses an upload, the app waits longer between attempts (5, 10, 20, then 30 minutes) and logs the error once instead of every time. Each addon retries on its own, so a problem with one doesn't hold up the others.
- If the server rejects one particular file, that file is skipped until it changes or the app is restarted.

## Clearing addon data

If the addons were updated to a version the server doesn't take yet, the files they saved are rejected on every sync. **Clear addon data…** (next to **Start**, while stopped) gets rid of them:

- It moves every `*.dat` file in `PlayerDB\Players`, `PlayerDB\Houses`, `PlayerDB\Regrades`, `PlayerDB\Inventory` and `RaidsDB\Raids` to the Recycle Bin, so they can be restored. This includes the addons' `_count.dat` and `_files.dat`. The folders and any other files are left alone.
- It first asks you to disable the addons (and reload, if you're in the game), as they'd otherwise save their old data straight back, and shows how many files each folder has. Click **Clean** to go ahead or **Nevermind** to leave everything as it is. Once it's done, it reminds you to re-enable the addons.
- The app forgets what it uploaded from those folders, so everything the addons collect from then on is uploaded as new.

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

Tick **Save screenshot on RaidsDB request** to let the RaidsDB addon ask for screenshots. While syncing, the app checks every 2 seconds for `RaidsDB\Raids\request.sht`, which the addon writes like this:

```lua
{
    raid_started = "1792312031",
    id = "1792312031-2",
    reason = "end",
    -- …
}
```

When it finds one, it captures the `archeage.exe` window and deletes the request once the screenshot is saved. If the capture fails (for example the game is minimised), the request is kept and retried every 2 seconds. A request that's only partly written (no closing `}` yet) is read again on the next check.

Screenshots are saved to `%TEMP%\PlayerDBSync\Screenshots`. The **Open folder** button next to the checkbox opens it in Explorer.

### File names

A screenshot is named `<raid start>-<number>-<reason>.webp`, so the request above is saved as `2026-10-18_08-27-11-2-end.webp`:

- **Raid start**: `raid_started`, in UTC, the same as the addon's saved raid files. If the request doesn't give it, the raid being recorded in `RaidsDB\current.dat` is used; if no raid is recording either, the current time.
- **Number**: the number at the end of `id`. If the request doesn't give one, the next unused number is taken, counting from 1. The last number used is kept in `last.txt` in the screenshot folder, so a number isn't reused after its screenshot has been uploaded and deleted.
- **Reason**: `reason` (e.g. `start`, `end`, `drop`), in lower case. It's left off if the request has none, or one that isn't just letters and digits: `2026-10-18_08-27-11-2.webp`.

### Size

Screenshots are saved as lossy WebP (quality 80) to keep uploads small. If that comes out over 390 KB, it's re-encoded 5 quality lower at a time (no lower than 40) until it fits; the server takes up to 400 KB. Captures larger than 1920×1080 are scaled down to fit, keeping their aspect ratio.

### Uploading

While RaidsDB is synced, the app uploads the screenshots in that folder every 30 seconds, oldest first, up to 3 at a time. When sync starts, any screenshot still waiting that's larger than 1920×1080 is scaled down first.

- Once the server has a screenshot it's deleted, so anything left in the folder is still waiting to be sent.
- If the server rejects a screenshot (for example because it's too large), that file is skipped until it changes or the app is restarted.
- If the upload fails, it's tried again on the next 30-second tick.
- If another recorder already uploaded a screenshot with the same number for that raid, the server gives yours the next free number and keeps its reason.

## Demo OCR screenshots

Tick **Upload screenshot on PlayerDB DemoOCR request** to let the PlayerDB addon's DemoOCR icon (the burning house) send the server a screenshot of a demolition notice to read. It needs the addon's `Demos` folder, so the **PlayerDB addon** folder must be set. The addon and the app hand over through two files in `PlayerDB\Demos`:

| File | Written by | Meaning |
|------|------------|---------|
| `request.dat` | addon, then app | `{ status = true }` is a request for a screenshot. The app sets it back to `{ status = false }` once the screenshot is saved; the addon then shows the icon as done and stops writing the location. If it is still `true` after 60 seconds the addon gives up and tells you to enable the sync app. |
| `location.dat` | addon | The player's sextant location, rewritten every second while a request is open. |

While syncing, the app checks `request.dat` every second. On `status = true` it waits until `location.dat` is up to date (written after the request and in the last 3 seconds), captures the `archeage.exe` window, and checks that the location hasn't changed since. If anything isn't right yet (the game is minimised, the location is stale) it logs why once and tries again every second until the addon gives up.

The game window must be exactly **2560x1440**; the server only takes that size, so the app never scales it. Otherwise it logs the size it found and waits.

### Uploading

Screenshots are saved as WebP (lossless, or lossy if that would be over the server's 10 MB limit) in `%TEMP%\PlayerDBSync\Screenshots\DemoOCR`, named after when and where they were taken: `2026-10-23_13-08-00_16-10-20S_9-41-2W.webp`. The app uploads them 2 seconds after they are saved (a file isn't always readable straight away) and then every 30 seconds, oldest first, up to 3 at a time, with the location sent as `16° 10' 20" S, 9° 41' 2" W`. A screenshot is deleted once the server has dealt with it, so anything left in that folder is still waiting.

- **Queued**: the server answers with the request's id and place in the queue, which the log shows.
- **Already known** (409): the same picture, or a demo or request within 4 m of that spot, is already on the server, so the screenshot is deleted.
- **Rejected** (422): the server found the picture or location wrong. The file is kept and skipped until it changes or the app is restarted.
- **Ten already waiting** (429) or any other failure: the file is kept and tried again on the next 30-second tick.

## Upgrading from HomeDB and RegradeDB

HomeDB and RegradeDB are defunct; their houses and regrades are now collected by PlayerDB, in its `Houses` and `Regrades` folders. This version of the app reads them from the **PlayerDB addon** folder and no longer has separate HomeDB and RegradeDB fields; folders saved for them by older versions are ignored.

Because the folders have moved, the first sync after upgrading uploads every house and regrade file once more.

## Settings

Settings are saved in `%AppData%\PlayerDBSync\settings.json`. The API key is encrypted so only your Windows account can read it. To start fresh, close the app and delete that file.

## Troubleshooting

| Message | What to do |
|---------|------------|
| *This doesn't look like the PlayerDB/RaidsDB addon folder* | Pick the addon's own folder, the one with `main.lua` in it. If the `Players`, `Houses`, `Regrades`, `Inventory` or `Raids` folder doesn't exist yet, run the game with the addon enabled first so it creates it. |
| *Server refused every upload … check the user ID, API key and PC clock* | Check your user ID and API key. Uploads are signed with your PC's time, so make sure Windows has the right time (Settings → Time & language → **Sync now**). |
| *Failing (3x) – next try …* | The server is down or unreachable. The app keeps retrying on its own; check the Server URL if it never recovers. |
| *Homes: skipped, the PlayerDB addon has no Houses folder yet* (or Players/Regrades/Inventory) | The addon hasn't saved any of those yet. Play with the addon enabled until it creates the folder, then click **Stop** and **Start**. |
| No version shown under a folder | The folder isn't an addon folder, or the addon hasn't written a data file with `addon_version` yet. Syncing still works. |
| *Demo OCR requested but the game window is …, it needs to be 2560x1440* | Set the game to a 2560x1440 window or full screen resolution and click the DemoOCR icon again. |
| *… the capture came out black (is the game in exclusive full screen? …)* | The app captures the game window without taking over the screen, which shows black while the game is in exclusive full screen. Set the game to windowed or borderless full screen; the request is retried on its own. |
| *Demo OCR requested but location.dat isn't up to date yet* | The addon isn't writing its location. Check that the DemoOCR feature is enabled in the PlayerDB addon and the game isn't paused or loading. |
| *Demo OCR: needs the PlayerDB addon's Demos folder* | Update the PlayerDB addon, then play with it enabled until it creates `Demos`, and click **Stop** and **Start**. |
| *Server rejected … not named \<raid start\>-\<n\>[-\<reason\>].webp* | A file in the screenshot folder wasn't saved by the app. Remove it from `%TEMP%\PlayerDBSync\Screenshots`. |

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
