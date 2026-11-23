# Glossary

The agreed name for every concept in this plugin. The UI, the docs, messages, and code use these words and no others.

* Use the **Term** in the UI, the docs, and messages.
* The **In Code** column names the type or member that holds it, when the code name differs.
* Never use a word from the **Not** column for the concept.
* Add a term here before it appears anywhere else. Rename it here first when it changes, then everywhere in the same commit.

## Scripts

| Term | Meaning | In Code | Not |
| :--- | :--- | :--- | :--- |
| Script | One named set of triggers, filters, conditions, and steps | `ScriptDefinition` | Rule, automation, job |
| Enabled | Whether a script runs on its triggers and its schedule | `Enabled` | Active, on |
| Template | A starter script the New dialog offers | `TEMPLATES` in `scripts_templates.js` | Preset, example |
| Duplicate | A disabled copy of the open script, without its secret values | | Clone |
| Export, Import | A JSON file of scripts with no secret values or webhook addresses, and reading one back | | Backup, restore |

## Triggers

| Term | Meaning | In Code | Not |
| :--- | :--- | :--- | :--- |
| Trigger | A server event a script runs on, such as **Playback started** | `ScriptTrigger`, `TriggerCatalog` | Event type, hook |
| Event | One time a trigger happens, with its values | `EventContext` | Occurrence, message |
| Schedule | The **On a schedule** trigger, run as a Jellyfin scheduled task in the Scripts category | `ScriptScheduledTask`, `ScheduleMode` | Timer, cron |
| Webhook | The **Webhook received** trigger, run by a `POST` to the script's address | `WebhookToken` | Callback, endpoint |
| Webhook address | The script's own URL for its webhook, which picks the script | `WebhookToken` | Webhook URL, token, link |
| Threshold | The number a count or percentage trigger waits for, run on the edge from below to at or past it | `StreamCountThreshold`, `TranscodeCountThreshold`, `ProgressPercentThreshold` | Limit, trigger value |
| Run now | Runs the saved script for real, with no user, whatever its triggers say | `POST Scripts/Run/{id}` | Trigger, start, execute |

## Filters

| Term | Meaning | In Code | Not |
| :--- | :--- | :--- | :--- |
| Item kinds | The kinds of item a script runs for. None means every kind | `ItemKinds` | Item types, media types |
| Users | The users a script runs for. None means everyone | `UserIds` | Accounts, people |
| Condition | One test of a field against values that a script must pass | `ScriptCondition` | Filter, rule |
| All conditions, Any condition | Whether every condition or at least one must match | `ConditionMatch`, `Match` | And, or |
| Cooldown | How long a script skips the same trigger, user, and item after a run, set in hours, minutes, and seconds | `CooldownSeconds` | Throttle, debounce |
| Run for each | Running once for each library item or user, for a trigger with none of its own | `ForEachMode`, `ForEach` | Loop, batch, fan out |
| Most runs per event | The cap on runs one event starts while running for each | `ForEachLimit` | Limit, max |

## Steps

| Term | Meaning | In Code | Not |
| :--- | :--- | :--- | :--- |
| Step | One action a script performs, in order | `ScriptOperation`, `OperationType` | Operation, action, task |
| Run command | The step that starts a program or script on the server | `RunCommand` | Exec, shell step |
| Shell line | A **Run command** line run through `/bin/sh`, with no placeholders filled | `RunThroughShell` | Shell command, script line |
| Check path | Asks the server whether it can see and run a command's path | `GET Scripts/CheckPath` | Validate, test path |
| Run another script | The step that runs another script's steps with the same event | `RunScript` | Call, include |
| Stop here | The step that ends a run and counts it as succeeded | `StopScript` | Exit, break, return |
| Continue on error | Lets the steps after a failed step run | `ContinueOnError` | Ignore errors |
| Step condition | The one variable test that decides whether a step runs, shown as **Only run this step if** | `OnlyIfVariable`, `OnlyIfOperator`, `OnlyIfValue` | Guard, if |
| Saved output | A command's output or an HTTP response kept under a name for later steps | `OutputName`, `OutputJsonPath`, `OutputCapture` | Result, return value |
| Preset | The fields an **HTTP request** step starts from for a common service | `HTTP_PRESETS` in `scripts_templates.js` | Template |
| Failure script | The script that runs when this one fails, set by **When this script fails, run** | `OnFailureScriptId` | Error handler, fallback |

## Values

| Term | Meaning | In Code | Not |
| :--- | :--- | :--- | :--- |
| Variable | A named value an event, a step, or a secret supplies, such as `ITEM_NAME` | `VariableInfo`, `Variables` | Field, parameter |
| Placeholder | A variable written as `{{NAME}}` in a step's text, filled when it runs | `Placeholders` | Token, tag, template variable |
| Environment variable | A variable a command reads as `$JF_NAME` | `TriggerCatalog.EnvironmentPrefix` | Env var |
| Secret | A token or password a script keeps encrypted on the server, used as `{{SECRET_NAME}}` | `ScriptSecret`, `SecretVault` | Credential, password, key |

## Runs

| Term | Meaning | In Code | Not |
| :--- | :--- | :--- | :--- |
| Run | One time a script ran its steps, as the history records it | `ScriptRun` | Execution, job, invocation |
| Test | Runs the open script's steps now with sample values, unsaved edits included | `POST Scripts/Test`, `IsTest` | Dry run, preview |
| History | The **History** tab's list of recent runs, tests included | `ScriptHistory`, `HistoryStore` | Log, audit |
| Succeeded, Failed, Skipped | A step's result, and Succeeded or Failed for a whole run | `OperationStatus`, `Succeeded` | Passed, errored, ignored |

## Settings

| Term | Meaning | In Code | Not |
| :--- | :--- | :--- | :--- |
| Runs at once | How many scripts may run at the same time | `MaxConcurrentRuns` | Concurrency, parallelism |
| Default command timeout | How long a **Run command** step runs when it sets no timeout | `DefaultTimeoutSeconds` | Timeout |
| Runs kept | How many runs the history keeps, on the Settings tab | `HistorySize` | History size, retention |

## Housekeeping

| Term | Meaning | Not |
| :--- | :--- | :--- |
| Activity page | Jellyfin's dashboard activity log, where failed runs are summed into one entry | Activity feed, log page |
| Server log | Jellyfin's log files | Debug log |
| Scheduled Tasks | Jellyfin's **Dashboard > Scheduled Tasks** page, where a script's schedule is set | Task scheduler, jobs |
