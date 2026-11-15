# Documentation

How to write the README, the docs site, screenshots, and release notes for this plugin. It applies to every page under `docs/`, to `README.md`, and to the `changelog` in `build.yaml`.

## The One Rule

Documentation describes the release it ships with. It reads as if this version were the only one that ever existed.

* Write what the plugin does. Never write what it used to do, what it no longer does, or what it now does.
* Write how a feature works. Never write that a bug was fixed, a problem is gone, or a limit was lifted.
* History belongs in the release notes. See [Release Notes](#release-notes).

| Do not write | Write |
| :--- | :--- |
| Secrets are now encrypted. | Secrets are encrypted. |
| The old schedule editor was removed. | Leave it out. |
| No longer runs on virtual items. | Item added skips placeholder items. |
| Fixed an issue where failed runs filled the activity log. | Failed runs are summed into one activity entry. |
| New in 12.4.2.0, Stop here ends a run... | Stop here ends a run... |

"Now" and "no longer" are fine when they describe state rather than history. "The item is no longer in its library" describes what the event sees, so it stays.

A version number belongs in docs only when the reader needs it to act, such as the minimum Jellyfin version. Keep those facts in [Installation](../docs/Installation.md) and [Versioning](../docs/Versioning.md), and name the version outright instead of "this release" or "an older release".

## Writing Style

### Sentences

* Use present tense and plain declarative sentences.
* Describe the system in the third person. "The script runs its steps in order."
* Address the reader in the second person only in steps and tips. "Select **New** and pick a template."
* State one fact per sentence. Aim for 20 words or fewer and split anything over 30.
* Use no contractions. Write "cannot", not "can't".
* Use no em dashes, en dashes as dashes, or semicolons. Split the sentence, or join it with a comma and "and".
* Use colons only to introduce a list or a code block.
* Use parentheses rarely. Move the aside into its own sentence.
* End every sentence with a period.
* Write compounds as separate words the way the UI does, such as "one time setup" and "sign in".
* Write one paragraph per line. Never hard wrap prose.
* Use no marketing words such as "powerful", "seamless", "easily", or "simply".
* Write examples with generic names and `example.com` addresses, never a real server, person, or address.

### Headings

* Overview, Installation, and Architecture use Title Case headings, such as "## How It Works". Writing Scripts and Examples use sentence case, as the UI does, such as "## Step conditions and saved output". Keep each page consistent with itself, and move a page to Title Case whole, with its anchors.
* Page titles are `#` and appear once per page.
* Name the thing, not the action, unless the section is a procedure. "## Settings" and "## Add the Plugin Repository" are both correct.
* Never rename a heading without fixing its anchors. `mkdocs build --strict` fails on a broken anchor.

### UI Names

* Write a label exactly as the page shows it, in bold. **Cooldown in seconds**.
* Write a path through the UI with `→` between the steps, in bold, as the existing pages do. **Dashboard → Plugins → Scripts**.
* Write a tab as its label, in bold. **Scripts**, **History**, **Settings**.
* Quote activity log names and on-screen messages in double quotes. "3 Script Runs Failed".
* Write routes, files, variables, and values in code. `POST /Scripts/Webhook/{token}`, `{{ITEM_NAME}}`.

A docs page and the UI must use the same word for the same thing. When a label changes, search `docs/` for the old one and change it everywhere in the same commit.

## Site Structure

The docs site is MkDocs with the Material theme, built from `docs/` by `mkdocs.yml` and published to `https://jpkribs.github.io/jellyfin-plugin-scripts/`.

### Navigation

Keep the nav in this order.

1. **Overview** (`index.md`). What the plugin is, how it works, and the warning about commands.
2. **Getting Started**. Installation.
3. **Using Scripts**. Writing Scripts, then Examples.
4. **Reference**. Versioning.
5. **Development**. [Architecture](../docs/Architecture.md).

Name page files in PascalCase with no spaces, such as `Architecture.md`.

## Architecture

[docs/Architecture.md](../docs/Architecture.md) maps how the parts of the plugin connect and the rules that hold across all of them. It is the page a new contributor, person or AI, reads first.

Review it after every change, and update it in the same commit when the change affects any of these.

* A part is added, removed, renamed, or moved to another folder.
* A part calls a different part, or data takes a different path.
* A trigger source, a scheduled task, a hosted service, a controller, or a state file is added or removed.
* A rule that holds everywhere changes, such as no shell by default, secrets staying on the server, or events never waiting on scripts.

Name classes and files exactly as the code does, in code format, with the folder when it is not obvious. Never link to source files, since the docs build refuses links outside `docs/`. A wrong name on this page sends every reader to the wrong file, so check each one against the code.

### Page Templates

A page on one area, such as **Writing Scripts**, opens with one sentence on what it covers, then one section per thing a reader sets or reads, in the order the editor shows them.

```markdown
# Writing Scripts

One sentence that says what a script is.

## Triggers

## Conditions

## Steps
```

An example on **Examples** follows this shape, and is marked *(template)* when the New dialog offers it.

```markdown
### Stop playback left paused *(template)*

One sentence on what it does and when it helps.

| Setting | Value |
|---|---|
| Trigger | **Paused for a while**, 30 minutes |
| Step | **Stop playback**, title `Playback stopped` |
```

An API route follows this shape.

````markdown
### POST /Scripts/Run/{id}

One sentence on what the route does and who calls it.

| Status | Meaning |
| :--- | :--- |
| `202` | The run was queued |
| `404` | No saved script has this ID |
````

### Settings References

List settings as bullets in the order the page shows them. Start each with the label in bold and follow it with a verb.

```markdown
* **Runs at once** sets how many scripts may run at the same time.
```

When a page has many settings with defaults, use a table instead.

| Setting | What it does | Default |
| :--- | :--- | :--- |
| **Runs at once** | Sets how many scripts may run at the same time | 4 |

Table cells have no closing period. Keep them to one sentence.

### Troubleshooting

Write trouble as a table of what the reader sees and what to do.

| Reason | What to do |
| :--- | :--- |
| The server can see this file, but it is not executable. | Run `chmod +x` on *path*, or call it through an interpreter |

Put the exact on-screen text in the first column. Use italics for a placeholder inside it, such as *server*.

### Admonitions

Give every admonition a short title.

```markdown
!!! warning "Commands run on the server"
    One or two sentences.
```

* `note` for a detail the reader needs.
* `tip` for a better way to do something.
* `warning` for anything that can lose data, run a command, or break a server.
* `info` for a requirement, such as a mounted folder in Docker.

### Links

* Link pages relatively with their `.md` name, such as `[Secrets](Scripts.md#secrets)`.
* End a Getting Started page with one "Next" line, such as `Next: [Writing Scripts](Scripts.md).`
* Link to Base's README for a shared component instead of describing it again.

## README

The README is a landing page. The docs site holds the detail.

1. The icon, `<img src="docs/assets/icon.png" alt="Scripts" width="96" align="left">`, then a one paragraph pitch and `<br clear="left">`.
2. `## Documentation`. One sentence on what the docs cover, a bold link to the site, then the repository URL for Jellyfin in a code block.

Use `##` for README headings. Keep the README under 40 lines.

## Screenshots

### What to Capture

* Capture every page and dialog a docs page describes.
* Capture the dark theme only, which is Jellyfin's default.
* Capture from a test server whose users, libraries, and addresses are generic.
* Capture after a few runs so the history, counts, and badges are filled.
* Hide nothing with editing. When a value is private, change the test server's data instead.

### Sizes

| Kind | Size | How |
| :--- | :--- | :--- |
| Dialog | 800 wide | The browser at 800 px, cropped to the dialog |
| Full page | 1920 wide | The browser at 1920 px, cropped to the content area |
| Phone | 375 wide at 2x | The browser's device mode at 375 px |

Save every capture as PNG. Crop out the browser frame and the Jellyfin header unless the header is the subject.

### Names and Placement

* Save to `docs/screenshots/`.
* Name the file in PascalCase after what it shows, as the existing captures are. `RunDetails.png`, `TestRun.png`.
* Link with alt text that names the subject. `![Run details](screenshots/RunDetails.png)`.
* Place two related shots side by side in a table whose header row holds the captions.

```markdown
| A test run | Run details |
| :---: | :---: |
| ![A test run](screenshots/TestRun.png) | ![Run details](screenshots/RunDetails.png) |
```

### When to Retake

Retake a screenshot in the same commit as any change that alters what it shows, including a label, a tab, a layout, or the Jellyfin version in the header. A new Base version changes how every component looks, so retake every screenshot when it is raised. A screenshot that shows a tab or label the plugin no longer has is a bug.

## Installing a Build by Hand

```bash
./build.sh
```

The zip lands in `dist/` as `jellyfin-plugin-scripts-<version>.zip`. Unzip it into `<config>/plugins/Scripts_<version>/` on a Jellyfin 12.4 test server and restart the server.

## Building the Docs

```bash
pip install mkdocs==1.6.1 mkdocs-material==9.7.7
```

```bash
mkdocs serve -a 127.0.0.1:8765
```

```bash
mkdocs build --strict
```

Run the strict build before every docs commit. CI runs the same build and refuses broken links, anchors, and nav entries.

## Release Notes

Release notes live in the `changelog` of `build.yaml`, under four keys. The publish workflow copies them into `manifest.json`, so every line reaches users' plugin catalogs.

| Key | Holds | Voice |
| :--- | :--- | :--- |
| `added` | A feature that did not exist | The feature name, then what it does |
| `changed` | Behavior a user will notice | The new behavior, in present tense |
| `fixed` | A bug | What went wrong in past tense, then the fix in present tense |
| `developer` | Routes, schema, tests, and tooling | Plain statements for other developers |

```yaml
changelog:
  added:
    - Run now button, which runs the saved script at once whatever its triggers, filters, and cooldown say
  fixed:
    - The arrows on collapsible sections were too small. They are back to their usual size
```

* This is the one place that may say "now", "no longer", and "was".
* Write full sentences with no closing period.
* Write one entry per change a user would notice. Fold internal steps into the change they serve.
* Name UI labels exactly as the page shows them, without bold or backticks.
* Name the Base version in `developer`, with what it replaced locally.
* Give a new or changed route or query parameter a `developer` line, and list it in [Architecture](../docs/Architecture.md) in the same commit.

## Commit Messages

* Write an imperative sentence in sentence case with no closing period. "Sum failed script runs into one activity entry".
* Describe the behavior, not the files. Use no prefixes such as `fix:` or `feat:`.
* Add a body of plain paragraphs, wrapped at 72 characters, when the reason is not obvious.
* Commit docs and screenshots with the change they describe.

## Checklist

* [ ] Nothing describes an earlier version, a removed feature, or a fixed bug.
* [ ] Every UI label matches the page exactly and is bold.
* [ ] Every heading matches the site's casing.
* [ ] No em dashes, semicolons, or contractions.
* [ ] Every screenshot matches the current UI and uses generic data.
* [ ] [Architecture](../docs/Architecture.md) was reviewed and matches the code.
* [ ] Every term matches [glossary.md](glossary.md).
* [ ] `mkdocs build --strict` passes.
* [ ] `build.yaml` has a changelog entry for any change a user would notice.
