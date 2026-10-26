# Scripts: design notes

The user-facing behavior is documented in [docs/Scripts.md](docs/Scripts.md). This file records the decisions behind it and what is left.

Targets Jellyfin 12.2 (`net10.0`, ABI `12.2.0.0`). Every event and API used was checked against `Jellyfin.Controller`, `Jellyfin.Data`, and `Jellyfin.Model` 12.2.0.

## Decisions

* **Steps instead of languages.** Common jobs (HTTP request, message or stop the session, refresh metadata, queue a scheduled task, write to the activity log, wait) are built in, so most scripts need no interpreter. **Run command** covers the rest.
* **Scripts live anywhere.** A command is any path the server can reach, or a name on its PATH. There is no managed scripts folder. The editor's **Check path** asks the server whether it can see the file and whether it is executable, which matters most in Docker, where the path is the container's.
* **Item kinds are chosen, never assumed.** The editor offers every `BaseItemKind`, and an empty list means every kind. The only built-in exclusion is virtual (placeholder) items on Item added and Item removed.
* **No shell by default.** Arguments go through `ProcessStartInfo.ArgumentList`, one line per argument. A shell line is never filled with placeholders, so event values reach the shell only as environment variables.
* **Events never wait on scripts.** Handlers build an `EventContext` and queue a job on a bounded channel (500). `ScriptRunner` runs at most `MaxConcurrentRuns` at once and drops, with a log line, when the queue is full.
* **One source for what a trigger supplies.** `TriggerCatalog` lists each trigger's variables. Condition applicability, the editor's warnings and variable list, and the test samples all derive from it.
* **History outside the configuration.** Runs are kept in `Jellyfin.Plugin.Scripts.History.json` beside the plugin configuration, so busy servers do not rewrite their settings on every event.

## Code map

| Area | Files |
|---|---|
| Configuration model | `Models/ScriptDefinition.cs`, `ScriptCondition.cs`, `ScriptOperation.cs`, `Configuration/PluginConfiguration.cs` |
| Event capture | `Services/EventSubscriber.cs` (playback, library, tasks), `Services/AuthenticationConsumers.cs` (sign ins), `Services/PauseTracker.cs` |
| Event data | `Services/EventContextBuilder.cs`, `Services/EventContext.cs`, `Services/TriggerCatalog.cs` |
| Matching | `Services/ScriptDispatcher.cs` (triggers, cooldown), `Services/ConditionEvaluator.cs` (item kinds, conditions) |
| Running | `Services/ScriptRunner.cs`, `Services/OperationExecutor.cs`, `Services/CommandRunner.cs`, `Services/Placeholders.cs` |
| Validation | `Services/ConfigurationValidator.cs`, called from `Plugin.OnConfigurationSaving` and the test endpoint |
| API | `API/ScriptsController.cs`: `GET Scripts/Catalog`, `POST Scripts/Test`, `GET`/`DELETE Scripts/History`, `GET Scripts/CheckPath` |
| Dashboard | `Configuration/scripts_scripts.*`, `scripts_history.*`, `scripts_settings.*`, `scripts_common.js`, `scripts_shared.css` |

## Not built yet

* More triggers: user created, deleted, or locked out, sessions starting and ending, items updated, user data saved (played, favorite), plugin installs, pending restart, server startup.
* Replaying a history entry with its real event, rather than testing with samples.
* Encrypted secrets for header and body values, such as API tokens, using `SecretProtector`.
* An overlap mode (queue, skip, or run in parallel) for a script that is already running.
* Grouping a season or album added at once into one event.
* A real logo and docs icons. The current ones are Custom Pages' placeholders.
