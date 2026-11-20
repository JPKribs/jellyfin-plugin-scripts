# Translations

How this plugin shows its text in the viewer's language. Base builds the strings in and checks them on every build. The full reference is Base's [Translations](https://jpkribs.github.io/jellyfin-plugin-base/Translations/) guide.

## Where the Strings Live

| Strings | Where |
| :--- | :--- |
| Jellyfin's own | `<JellyfinString Include="Key" />` items in `Jellyfin.Plugin.Scripts/Jellyfin.Plugin.Scripts.csproj` |
| This plugin's own | `Jellyfin.Plugin.Scripts/Localization/en-us.json`, one file per language |
| The endpoint pages load them from | `GET /Scripts/Translations`, which answers with `BaseTranslations.Serve` |

## Rules

* Every word a person reads comes from a key. No hard coded English in a page, a script, or a message the server shows.
* Use a Jellyfin string whenever Jellyfin has the words. Search the web client's `src/strings/en-us.json` and the server's `Localization/Core/en-US.json` before adding a key.
* Add a local key only when Jellyfin has no string for it. Put it in `en-us.json` with its US English.
* One string, one key. The build fails on a key said twice or English said twice.
* Keep placeholders as `{0}`, `{1}`. Never build a sentence from pieces, since word order differs by language.
* A key is PascalCase and says what the text is, such as `HeaderQueue`, `LabelApiKey`, `ButtonRetry`, `ToastSaved`, `ErrorServerUnreachable`.
* Log lines and exception text for admins in the log stay in English and need no key.

## Add a String

1. Search Jellyfin's strings for the words.
2. Found: add `<JellyfinString Include="Key" />` to the csproj.
3. Not found: add `"Key": "English"` to `Localization/en-us.json`.
4. Use it.
    * Markup: `data-str="Key"`, `data-str-title`, `data-str-label`, or `data-str-placeholder`, with the English as the element's own text.
    * Text with code samples or links inside: `data-str-rich="Key"` on the element, with `{0}`, `{1}` in the string standing for its child elements in order.
    * Script: `t.tr('Key')` or `t.format('Key', value)` on `t` from `scripts_common.js`, which loads the strings with `loadTranslations('Scripts/Translations')`.
    * C#: `BaseTranslations.Get(TranslationKeys.Key, language)` or `BaseTranslations.Format(...)`. Text for everyone, such as an activity log entry, uses `BaseTranslations.ServerLanguage(config)`.
5. Build. Fix every `JPKT` error and warning.

## This Plugin

* `createScriptsPage` loads the strings and translates the page's markup before a page's `bind` and `load` run, so a page script reads `t` only inside them or in functions they call.
* Labels the pages build, such as the condition fields, operators, and step types, come from functions in `scripts_common.js`, never from lists built when the module loads.
* The trigger catalog holds keys. `GET /Scripts/Catalog?language=` answers with its labels, groups, descriptions, variables, and item kinds in that language.
* A run's step messages and summary are written into the history once, in US English, and stay that way.
* Template and preset names are strings. A template's own step text, such as a message it sends, is part of the script and stays as written.
* The screenshot kit's `server.py` serves the strings at `/Scripts/Translations`, so the harness shows them as the server does.

## Add a Language

Add `Localization/<code>.json`, such as `de.json` or `pt-br.json`, with only keys `en-us.json` has. A missing key falls back to the language's base and then to US English. Jellyfin's strings come in every language Jellyfin has, with no file.

## Checklist

* [ ] No English in a page, script, or server message outside `Localization/en-us.json`.
* [ ] Jellyfin's string used wherever it has the words.
* [ ] The build has no `JPKT` error or warning.
* [ ] The pages show the strings, checked once in another language such as `de`.
