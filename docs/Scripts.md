# Writing Scripts

Each script has **triggers**, optional **item kinds**, **users**, and **conditions** that narrow which events it runs for, and a list of **steps** it performs in order. Scripts are managed on **Dashboard → Plugins → Scripts**.

## Triggers

| Trigger | Runs when | Carries an item | Carries a session |
|---|---|---|---|
| Playback started | Playback begins on a client. This is after the client has started loading, so it cannot change what plays. | Yes | Yes |
| Playback stopped | Playback ends. `PLAYED_TO_COMPLETION` says whether it reached the end. | Yes | Yes |
| Playback paused / resumed | A playing session pauses or resumes. Fires once per change. | Yes | Yes |
| Item added | An item is added to a library. It waits up to a minute for the item's metadata, so names and provider IDs are filled. Placeholder items, such as missing episodes from the series metadata, are skipped. | Yes | No |
| Item removed | An item is removed from a library. | Yes | No |
| Signed in | A user signs in. | No | No |
| Sign in failed | A sign in attempt fails for an existing user. Jellyfin reports no event for a username that doesn't exist. | No | No |
| Scheduled task finished | A scheduled task finishes, whatever its outcome. | No | No |

## Item kinds

Choose the kinds of item the script runs for, such as `Movie`, `Episode`, `Audio`, or `MusicVideo`. Every kind the server knows is offered, and nothing is assumed: leave the list empty to run for every kind.

Item kinds only filter triggers that carry an item. Sign ins and scheduled tasks ignore them.

!!! warning
    A library scan adds people, genres, studios, and folders as items too. A script on **Item added** with no item kinds runs for all of them.

## Users

Choose the users the script runs for, or leave the list empty to run for everyone. On playback, the script runs when any of the session's users is listed. A failed sign in counts only when the typed name belongs to a listed user.

Users only filter triggers that carry a user: playback and sign ins. Library events and scheduled tasks ignore them. For anything finer, such as matching a name pattern, use a **User** condition.

## Conditions

Each condition compares one event value with one or more values, using **is**, **is not**, **contains**, **does not contain**, **starts with**, or **matches regex**. Text comparisons ignore case. A positive operator matches when any value matches, and a negative one matches when none do.

| Field | Compares |
|---|---|
| User | The user's ID or name. On playback, any of the session's users. |
| Library | The ID or name of any library holding the item. |
| Item name, Series name | The item's name, or the series an episode belongs to. |
| Client, Device name | The client application and device. |
| Play method | `DirectPlay`, `DirectStream`, or `Transcode`. |
| Is local network | `true` when the client is inside the local network. |
| Played to completion | `true` when playback reached the end. Playback stopped only. |
| Username attempted | The username typed into a failed sign in. |
| Scheduled task, Task status | The task's key, and `Completed`, `Failed`, `Cancelled`, or `Aborted`. |

A field the trigger does not supply, or a value this event happens to lack, never matches, for **is not** and **does not contain** too. For example, **Series name is not** does not match a movie, which has no series. The editor warns when a condition's field is missing from some of the selected triggers. Choose whether the script runs when **all** conditions match or when **any** does.

**Cooldown** skips the script for the same trigger, user, and item until the given number of seconds have passed since its last run, so a script on both start and stop still sees the stop. It keeps pause and resume from running a script over and over.

## Steps

| Step | Does | Needs |
|---|---|---|
| Run command | Runs a program or script at any path the server can reach, or a name on the server's PATH such as `curl`. | |
| HTTP request | Sends a GET, POST, PUT, PATCH, or DELETE with your headers and body. | |
| Send message to session | Shows a message on the client that triggered the event. | A session |
| Stop playback | Stops playback on that client, optionally after a message. | A session |
| Refresh item metadata | Queues a standard or full metadata refresh of the event's item. | An item |
| Run scheduled task | Queues a scheduled task. A task finishing can't queue itself, since that would repeat forever. | |
| Write to activity log | Adds an entry to the server's activity log. | |
| Wait | Pauses before the next step. | |

A step that needs a session or an item is skipped on events without one. A failed step stops the rest unless it is set to **Continue on error**. A run with a failed step is also written to the activity log.

### Run command

Use **Check path** to confirm the server can see the file and that it is executable. The server runs it as the account Jellyfin runs under, in the folder that holds the script unless you set a working folder. It is stopped after its timeout, which defaults to the value on the Settings tab. A script may start a background process and exit; the step reports the script's own exit code as soon as it finishes.

Each line of **Arguments** is passed as exactly one argument, with placeholders filled, so a value containing spaces or quotes never splits into more.

With **Run through the shell**, the command is one line for `/bin/sh -c`, which allows pipes and redirects. Placeholders are not filled in a shell line, so event values such as item or device names can never inject shell syntax. Read them from the environment instead:

```sh
/scripts/log.sh "$JF_USER_NAME" "$JF_ITEM_NAME" >> /scripts/plays.log
```

!!! warning "Windows"
    On Windows a shell line runs through `cmd /c`, which expands `%JF_...%` variables *before* it parses the line, so an event value such as a typed username can carry cmd syntax. Read event values inside a script file instead of in the shell line. A `.bat` or `.cmd` file is refused when one of its filled arguments contains `% " & | < > ^`, since cmd does not honour the quoting other programs get.

!!! info "Docker"
    The path is the path inside the container. Mount your scripts, for example `-v /srv/jellyfin-scripts:/scripts:ro`, and use `/scripts/notify.sh`. The official image has a shell but no Python or most other interpreters, so write scripts for `/bin/sh` or mount the interpreter you need.

## Variables

A command receives every variable the trigger supplies as an environment variable with a `JF_` prefix, such as `$JF_ITEM_NAME`, plus `$JF_EVENT_JSON` holding all of them as one JSON object. A value the event lacks is set to an empty string, so a script under `set -u` can still test it.

In a step's text fields, write a variable as `{{ITEM_NAME}}`. Use `{{ITEM_NAME:json}}` inside a JSON string, and `{{ITEM_NAME:url}}` inside a URL. The **Variables** section of the editor lists the ones the selected triggers supply, and clicking one copies it.

| Group | Variables |
|---|---|
| Every event | `EVENT`, `TIMESTAMP`, `SCRIPT_NAME`, `SERVER_NAME`, `SERVER_ID`, `SERVER_VERSION`, `EVENT_JSON` |
| User | `USER_ID`, `USER_NAME` |
| Session | `SESSION_ID`, `CLIENT`, `CLIENT_VERSION`, `DEVICE_ID`, `DEVICE_NAME`, `REMOTE_ADDRESS`, `IS_LOCAL` |
| Item | `ITEM_ID`, `ITEM_NAME`, `ITEM_KIND`, `ITEM_PATH`, `LIBRARY_ID`, `LIBRARY_NAME`, `YEAR`, `RUNTIME_SECONDS`, `SERIES_NAME`, `SEASON_NUMBER`, `EPISODE_NUMBER`, `ALBUM`, `ARTIST`, `PROVIDER_IMDB`, `PROVIDER_TMDB`, `PROVIDER_TVDB` |
| Playback | `PLAY_SESSION_ID`, `PLAY_METHOD` (on stop, the method seen while playing), `POSITION_SECONDS`, `PROGRESS_PERCENT`, `IS_PAUSED`, `VIDEO_CODEC`, `AUDIO_CODEC`, `TRANSCODE_REASONS`, and on stop `PLAYED_TO_COMPLETION` |
| Sign in failed | `USERNAME_ATTEMPTED`, `USER_ID` when the name matches a user |
| Scheduled task | `TASK_NAME`, `TASK_KEY`, `TASK_STATUS`, `TASK_ERROR`, `TASK_DURATION_SECONDS` |

## Export and import

**Export** on the Scripts tab saves every script, including unsaved edits, to a JSON file. **Import** reads one back, adding its scripts or replacing ones with the same ID. Imported scripts start **disabled** and unsaved, so review each one's steps before enabling it and saving, since a command runs on your server.

Users are stored by ID, so on another server a script's users show as unknown until you pick them again.

## Testing

**Test** runs the open script's steps right away, including unsaved edits, with sample values for the chosen trigger. Item kinds and conditions are not applied, and steps that act on a session or an item are skipped, because the sample has neither. Every run, tests included, is listed on the **History** tab with each step's result, the captured output, and the variables it received.
