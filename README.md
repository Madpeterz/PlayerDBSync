# PlayerDB Sync

A small Windows app that uploads the data collected by the **PlayerDB** and **HomeDB** ArcheAge Classic addons to the PlayerDB server.

It watches the addons' data folders, and every 5 minutes uploads any files that have changed:

| Addon    | Folder it reads    | Sent to                |
|----------|--------------------|------------------------|
| PlayerDB | `PlayerDB\Players` | `<server>/api/players` |
| HomeDB   | `HomeDB\Houses`    | `<server>/api/homes`   |

You can sync either addon or both.

## Requirements

- Windows 10 or 11 (64-bit). Nothing else to install; the .NET runtime is bundled.
- The PlayerDB and/or HomeDB addon installed in ArcheAge Classic.
- Your PlayerDB **user ID** and **API key**.

## Setup

1. **Download** `PlayerDBSync.exe` from the [latest release](https://github.com/Madpeterz/PlayerDBSync/releases/latest) and put it anywhere you like.

   The exe isn't code-signed, so Windows SmartScreen may warn the first time you run it. Click **More info → Run anyway**.

2. **Run it** and fill in the form:

   | Field              | What to enter |
   |--------------------|---------------|
   | **PlayerDB addon** | The PlayerDB addon folder: the one containing `main.lua` and a `Players` folder. Leave empty to skip player uploads. |
   | **HomeDB addon**   | The HomeDB addon folder: the one containing `main.lua` and a `Houses` folder. Leave empty to skip house uploads. |
   | **Server URL**     | The server's base address, e.g. `https://playerdb.blackatom.win` (no `/api/...` on the end). |
   | **User ID**        | Your PlayerDB user ID (a number). |
   | **API key**        | Your PlayerDB API key. |

   Use **Browse…** to pick each folder. Addons usually live in one of:

   - `C:\Users\<you>\Documents\AAClassic\Addon\`
   - `C:\AAClassic\Documents\Addon\`

   If you pick the `Players` or `Houses` folder by mistake, the app selects the addon folder above it for you.

3. **Click Start.** The app uploads everything that isn't on the server yet, then checks for changes every 5 minutes. The log shows what it uploads.

Next time you open the app, it starts syncing straight away with the saved settings. Click **Stop** to change them.

## How it works

- Only changed files are uploaded. The app remembers what it has already sent, so restarting it doesn't upload everything again. Changing a folder, the server URL or the user ID does re-upload everything for that addon.
- Changed files are sent in batches of up to 500.
- Bookkeeping files (`_count.dat`, `empty.keep`) are skipped.
- If the server can't be reached or refuses an upload, the app waits longer between attempts (5, 10, 20, then 30 minutes) and logs the error once instead of every time. Players and homes retry on their own, so a problem with one doesn't hold up the other.
- If the server rejects one particular file, that file is skipped until it changes or the app is restarted.

## Settings

Settings are saved in `%AppData%\PlayerDBSync\settings.json`. The API key is encrypted so only your Windows account can read it. To start fresh, close the app and delete that file.

## Troubleshooting

| Message | What to do |
|---------|------------|
| *This doesn't look like the PlayerDB/HomeDB addon folder* | Pick the addon's own folder, the one with `main.lua` in it. If the `Players` or `Houses` folder doesn't exist yet, run the game with the addon enabled first so it creates it. |
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
