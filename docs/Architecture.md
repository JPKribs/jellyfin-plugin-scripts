# Architecture

How the parts of Scripts connect, and the rules that hold across all of them. What a user sees is in [Writing Scripts](Scripts.md).

Scripts targets Jellyfin 12.4 (`net10.0`, ABI `12.4.0.0`). Every event and API it uses is checked against `Jellyfin.Controller`, `Jellyfin.Data`, and `Jellyfin.Model` 12.4.0.

## The Parts

| Area | Files |
| :--- | :--- |
| Configuration model | `Models/ScriptDefinition.cs`, `Models/ScriptCondition.cs`, `Models/ScriptOperation.cs`, `Configuration/PluginConfiguration.cs` |
| Event capture | `Services/EventSubscriber.cs` (manager events and the tick), `Services/ServerEventConsumers.cs` (sign ins, users, plugins, pending restart), `Services/PlaybackTracker.cs`, `Services/ScriptScheduledTasks.cs`, `Tasks/ScriptScheduledTask.cs` |
| Event data | `Services/EventContextBuilder.cs`, `Services/EventContext.cs`, `Services/TriggerCatalog.cs` |
| Matching | `Services/ScriptDispatcher.cs` (triggers, cooldown), `Services/ConditionEvaluator.cs` (item kinds, conditions) |
| Running | `Services/ScriptRunner.cs`, `Services/ForEachExpander.cs`, `Services/OutputCapture.cs`, `Services/WebhookVariables.cs`, `Services/OperationExecutor.cs`, `Services/OperationExecutor.Actions.cs`, `Services/CommandRunner.cs`, `Services/Placeholders.cs`, `Services/ScriptSecrets.cs` |
| State files | `Services/ScriptPaths.cs`, `Services/HistoryStore.cs`, `Services/SecretVault.cs` |
| Validation | `Services/ConfigurationValidator.cs`, called from `Plugin.OnConfigurationSaving` and the test route |
| API | `Api/ScriptsController.cs` |
| Dashboard | `Web/scripts_scripts.*`, `scripts_history.*`, `scripts_settings.*`, `scripts_common.js`, `scripts_templates.js`, `scripts_shared.css` |

The API routes all sit under `Scripts/`: `GET Catalog`, `GET Status`, `POST Test`, `POST Run/{id}`, `POST Webhook/{token}`, `GET` and `DELETE History`, and `GET CheckPath`.

`GET History` takes Base's `PagedQuery`, so the **History** tab's Data Table reads one page at a time with its search and its filter, `failed`, `test`, or a script ID.

The dashboard pages import Base only through `scripts_common.js`, which also holds the tab bar and the labels each enum name displays as. The lists, row editors, pickers, sliders, duration fields, and Save Bars on the three tabs are Base components. `scripts_shared.css` holds only spacing between them, and the bottom alignment of the condition rows.

## State

Everything the plugin keeps sits in Jellyfin's plugin configurations folder, beside the plugin configuration and outside the install folder that an update replaces.

| File | Holds |
| :--- | :--- |
| `Jellyfin.Plugin.Scripts.xml` | The settings and every script |
| `Jellyfin.Plugin.Scripts.History.json` | The run history |
| `Jellyfin.Plugin.Scripts.Secrets.json` | The encrypted secret values |
| `Jellyfin.Plugin.Scripts.Keys/` | The key that encrypts the secrets |
| `Jellyfin.Plugin.Scripts.Tasks.json` | Which scripts have a scheduled task, and which are retired |
| `Jellyfin.Plugin.Scripts.Devices.json` | The devices the server has seen |

## Rules

* **Steps instead of languages.** Common jobs are built in steps, such as HTTP requests, session messages and playback control, tags, collections, played and favorite state, user sign out, library scans, scheduled tasks, the activity log, and appending to a file. Most scripts need no interpreter. **Run command** covers the rest.
* **Only reliable triggers.** Every trigger hangs off a Jellyfin event or an `IEventConsumer`, never polling of state that can be missed. The exceptions are time based. **Paused for a while** and **Playing for a while** run off a 5 second tick. `PlaybackTracker` turns progress reports into pause and resume edges and keeps the paused and playing durations. It forgets a playback that sends no report for 15 minutes, since Jellyfin ends a silent playback well before that.
* **Secrets stay on the server.** Secret values are encrypted with Base's `SecretProtector` into `Jellyfin.Plugin.Scripts.Secrets.json`. The configuration keeps only a kept marker, so no API client ever reads a value. Secrets are left out of exports, and masked in every step message and command output a run records.
* **Scripts live anywhere.** A command is any path the server can reach, or a name on its PATH. There is no managed scripts folder. The editor's **Check path** asks the server whether it can see the file and whether it is executable, which matters most in Docker, where the path is the container's.
* **Item kinds are chosen, never assumed.** The editor offers every `BaseItemKind`, and an empty list means every kind. The only built in exclusion is virtual (placeholder) items on **Item added** and **Item removed**.
* **No shell by default.** Arguments go through `ProcessStartInfo.ArgumentList`, one line per argument. A shell line is never filled with placeholders, so event values reach the shell only as environment variables.
* **Events never wait on scripts.** Handlers build an `EventContext` and queue a job on a bounded channel of 500. `ScriptRunner` runs at most `MaxConcurrentRuns` at once, and drops a run with a log line when the queue is full.
* **One source for what a trigger supplies.** `TriggerCatalog` lists each trigger's variables. Condition applicability, the editor's warnings and variable list, and the test samples all derive from it.
* **A schedule is a Jellyfin scheduled task.** The plugin keeps no clock of its own for schedules. `ScriptScheduledTasks` gives each scheduled script a task in the Scripts category, named and described by the script, and Jellyfin owns when it runs. Jellyfin derives a task's ID from its class name (`ScheduledTaskWorker.Id`), so each script gets an empty subclass of `ScriptScheduledTask` emitted at run time and named after the script's ID, which keeps the ID stable across restarts. The script's schedule fields only seed the task's default triggers.
* **A removed task leaves nothing behind.** `ITaskManager` can add tasks but not remove them, so a task whose script is deleted or unscheduled hides and disables itself (`IConfigurableScheduledTask`) until the restart that no longer adds it. Jellyfin keeps two files per task, `config/ScheduledTasks/{id}.js` once its triggers are changed and `data/ScheduledTasks/{id}.js` after a run. Both are deleted as soon as the script goes, and again at the next start, because the hidden task can still be run through the API in between. Uninstalling the plugin does not run this cleanup.
* **Steps pass values forward, and branch one at a time.** A command or HTTP step can save its output under a name, and every step can carry one condition on one variable. Conditions stay flat on the step, like its other fields, so the XML stays readable, with no nesting and no else. **Stop here** plus a condition covers the early exit. `ConditionEvaluator.MatchesStep` and `OutputCapture` do the work, and `ScriptRunner` owns the loop.
* **For each is the script's own filters, applied later.** A script set to run for each item or user is queued unmatched when its event has none (`TriggerCatalog.Expands`). `ForEachExpander` then builds one context per item or user and matches the script's item kinds, users, and conditions against each. Play state is read across every user only when a condition asks for it, or for the items that matched.
* **A webhook needs the address and a key.** The token in the address picks the script, and the route keeps the controller's administrator policy, so the caller also sends a Jellyfin API key. Either one alone is refused. The token sits in the configuration in the clear, since administrators must be able to read the address back, and is compared in constant time.
* **Thresholds are edges.** Stream count, transcode count, and playback percentage run a script when the value goes from below its number to at or past it, never while it stays there. Counts are refreshed on the tick too, so a session that vanishes without a stop re-arms the trigger.
* **Run now carries no user.** The administrator who pressed it is named in the history but is not the event's user, so a step that acts on the event's user never acts on them.
* **History outside the configuration.** Runs are kept in `Jellyfin.Plugin.Scripts.History.json`, so busy servers do not rewrite their settings on every event.

## Limitations

* A history entry cannot be replayed with its real event. **Test** runs with sample values.
* A script has no overlap setting. A script triggered while it runs starts another run, unless its cooldown holds it back.
* A season or album added at once raises one **Item added** for each item.
* User permission changes do not run a script. Jellyfin raises `OnUserUpdated` only for name and profile edits.
