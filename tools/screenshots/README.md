# Screenshot Kit

Takes every image in `docs/screenshots/`. It serves the plugin's real pages with jellyfin-web's real styles and a stand-in server, and drives headless Chromium.

| File | Does |
| :--- | :--- |
| `server.py` | Serves the plugin pages, Base from the NuGet cache, and jellyfin-web, the way Jellyfin's `configurationpage` route does |
| `index.html` | Loads jellyfin-web's stylesheets in load order, draws the dashboard header, and mounts one tab |
| `tasks.html` | The Scripts row of Dashboard, Scheduled Tasks |
| `upgrade.js` | Adds the classes and markup Jellyfin's `emby-*` elements add on upgrade, so the real CSS applies |
| `stub.js` | A stand-in `ApiClient`, `Dashboard`, and `LibraryMenu` with the generic sample data |
| `catalog/` | A console project that writes the plugin's own `TriggerCatalog` to `catalog.json` |
| `shoot.mjs` | Opens one page in Chromium at 2x |
| `shots.mjs` | Sets up each subject and writes its PNG to `docs/screenshots/` |

## Requirements

* Python 3, Node, and the .NET 10 SDK.
* Base restored to the NuGet cache at the version `Jellyfin.Plugin.Scripts.csproj` names. Run `dotnet restore` on the plugin once.
* jellyfin-web. The default is the macOS app's. Set `JELLYFIN_WEB` to use another folder.
* playwright-core. Set `PLAYWRIGHT_CORE` to its `index.mjs` unless Node resolves `playwright-core` on its own.
* Chromium. The default is Playwright's `chromium-1148` in `~/Library/Caches/ms-playwright`. Set `CHROMIUM` to use another binary.

## Running

Run these from this folder. Write the catalog first, and again whenever a trigger, field, or variable changes.

```bash
dotnet run --project catalog -- catalog.json
python3 -I server.py 8873 &
```

Take every shot.

```bash
PLAYWRIGHT_CORE=/path/to/node_modules/playwright-core/index.mjs node shots.mjs
```

Take only the named shots.

```bash
PLAYWRIGHT_CORE=/path/to/node_modules/playwright-core/index.mjs node shots.mjs Conditions,History
```

Look at every PNG it writes before committing it. History and Run details show times relative to now, so their clock text changes on every run.
