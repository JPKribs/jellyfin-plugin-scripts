# Examples

Each example lists what to set in the editor. Several are also offered as templates under **New script**, marked *(template)*. Replace URLs, names, and tokens with your own, and keep tokens in the script's **Secrets**.

## Playback

### Stop playback left paused *(template)*

Frees the stream when someone pauses and walks away.

| Setting | Value |
|---|---|
| Trigger | **Paused for a while**, 30 minutes |
| Step | **Stop playback**, title `Playback stopped`, message `Stopped after 30 minutes paused.` |

### Ask "Still watching?" *(template)*

| Setting | Value |
|---|---|
| Trigger | **Playing for a while**, 180 minutes |
| Step | **Send message** to the session that raised the event, title `Still watching?`, message `You have been watching for 3 hours.` |

The time counts playback only, not pauses, and the script runs once per playback.

### Limit the server to 3 transcodes *(template)*

Stops the newest transcode when it would be the fourth.

| Setting | Value |
|---|---|
| Trigger | **Playback started** |
| Conditions | All of: **Play method** is `Transcode`; **Active transcodes** is at least `4` |
| Steps | 1. **Send message** to the event's session: `Too many streams are converting right now. Try a lower quality or direct play.` <br> 2. **Stop playback** |

`ACTIVE_TRANSCODES` counts the playback that just started, so `4` means three were already running.

### Keep 4K from transcoding away from home

| Setting | Value |
|---|---|
| Trigger | **Playback started** |
| Item kinds | Movie, Episode |
| Conditions | All of: **Play method** is `Transcode`; **Video height** is at least `2160`; **Is local network** is `false` |
| Step | **Stop playback**, message `4K can't be converted for remote streaming. Pick a 1080p version.` |

### Bedtime for the kids' accounts

| Setting | Value |
|---|---|
| Trigger | **Playback started** |
| Users | The kids' accounts |
| Conditions | **Any** of: **Hour (0-23)** is at least `21`; **Hour (0-23)** is at most `5` |
| Steps | 1. **Send message** to the event's session: `It's bedtime. See you tomorrow!` <br> 2. **Stop playback** |

Hours are the server's local time.

### Dim the lights when a movie starts *(template)*

| Setting | Value |
|---|---|
| Triggers | **Playback started**, **Playback resumed** |
| Item kinds | Movie |
| Conditions | **Device name** is `Living Room TV` |
| Secret | `HA_TOKEN`, a Home Assistant long-lived access token |
| Step | **HTTP request** from the **Home Assistant service call** preset |

The preset fills in:

```text
POST http://homeassistant.local:8123/api/services/light/turn_on
Authorization: Bearer {{SECRET_HA_TOKEN}}

{ "entity_id": "light.living_room", "brightness_pct": 10 }
```

Add a second script on **Playback paused** and **Playback stopped** that calls `light/turn_on` with `"brightness_pct": 80` to bring them back up.

## Library

### Post new movies and episodes to Discord *(template)*

| Setting | Value |
|---|---|
| Trigger | **Item added** |
| Item kinds | Movie, Episode |
| Secret | `DISCORD_WEBHOOK`, the part of the webhook URL after `/webhooks/` |
| Step | **HTTP request**, POST `https://discord.com/api/webhooks/{{SECRET_DISCORD_WEBHOOK}}` |

Body, with `:json` so names containing quotes stay valid JSON:

```json
{ "content": "New on {{SERVER_NAME:json}}: {{SERIES_NAME:json}} {{ITEM_NAME:json}} ({{YEAR}})" }
```

**Item added** waits up to a minute for metadata, so the year and provider IDs are usually filled. Set **Retries** to 2 or 3 so a brief Discord outage doesn't lose the post.

### Tag 4K movies as they arrive

| Setting | Value |
|---|---|
| Trigger | **Item added** |
| Item kinds | Movie |
| Conditions | **Video height** is at least `2160` |
| Step | **Add tag** `4K` |

### Tag new items, then untag once watched

Script 1 *(template)*:

| Setting | Value |
|---|---|
| Trigger | **Item added** |
| Item kinds | Movie, Series, Episode |
| Step | **Add tag** `new` |

Script 2:

| Setting | Value |
|---|---|
| Trigger | **Playback stopped** |
| Conditions | All of: **Played to completion** is `true`; **Tag** is `new` |
| Step | **Remove tag** `new` |

Tags belong to the item, not a user, so the first person to finish it removes the tag for everyone.

### Collect everyone's favorites

| Setting | Value |
|---|---|
| Trigger | **Favorite changed** |
| Item kinds | Movie |
| Conditions | **Is favorite** is `true` |
| Step | **Add to collection**, choose a collection such as *Family favorites* |

A second script with **Is favorite** is `false` and **Remove from collection** keeps it tidy.

## Accounts and security

### Alert on failed sign ins *(template)*

| Setting | Value |
|---|---|
| Trigger | **Sign in failed** |
| Cooldown | 300 seconds, so a burst of attempts sends one alert per user |
| Step | **HTTP request** from the **ntfy** preset, URL `https://ntfy.sh/{{SECRET_NTFY_TOPIC}}` |

Body (plain text, so no `:json`):

```text
Failed sign in for {{USERNAME_ATTEMPTED}} from {{REMOTE_ADDRESS}} ({{DEVICE_NAME}})
```

### Tell the administrators about a lockout

| Setting | Value |
|---|---|
| Trigger | **User locked out** |
| Step | **Send message** to every administrator's session, title `Account locked`, message `{{USER_NAME}} was locked out after too many failed sign ins.` |

### Sign out every device after a password change

| Setting | Value |
|---|---|
| Trigger | **Password changed** |
| Step | **Sign user out** |

Anyone still signed in with the old password has to sign in again. Administrators are never signed out or disabled by a script.

## Server

### Warn everyone before a restart *(template)*

| Setting | Value |
|---|---|
| Trigger | **Restart needed** |
| Step | **Send message** to every active session: `The server needs a restart and may go offline briefly.` |

### Message the administrators when a task fails *(template)*

| Setting | Value |
|---|---|
| Trigger | **Scheduled task finished** |
| Conditions | **Task status** is `Failed` |
| Step | **Send message** to every administrator's session: `{{TASK_NAME}} failed: {{TASK_ERROR}}` |

### Scan every library nightly *(template)*

| Setting | Value |
|---|---|
| Trigger | **On a schedule**, Daily at `03:00`, no days picked (every day) |
| Step | **Scan libraries** |

Pick days to run it only on, say, Monday, Wednesday, and Friday. **Monthly** runs on one day of the month instead.

### Heartbeat for an uptime monitor

Pings a service such as Healthchecks.io, which alerts you when the pings stop.

| Setting | Value |
|---|---|
| Trigger | **On a schedule**, Interval, every 5 minutes |
| Secret | `PING_ID`, your check's ID |
| Step | **HTTP request**, GET `https://hc-ping.com/{{SECRET_PING_ID}}`, **Retries** 2 |

## Commands

A **Run command** step runs any program the server can reach, with every variable as a `JF_` environment variable. The examples use `/bin/sh`, which the official Docker image has.

### Log every playback to a CSV file

| Setting | Value |
|---|---|
| Trigger | **Playback started** |
| Step | **Run command**, run directly, command `/scripts/log-play.sh` |

`/scripts/log-play.sh`, made executable with `chmod +x`:

```sh
#!/bin/sh
# Appends one line per playback to plays.csv beside this script.
log="$(dirname "$0")/plays.csv"
[ -f "$log" ] || echo "time,user,item,series,device,method" > "$log"
printf '%s,"%s","%s","%s","%s",%s\n' \
    "$JF_TIMESTAMP" "$JF_USER_NAME" "$JF_ITEM_NAME" "$JF_SERIES_NAME" "$JF_DEVICE_NAME" "$JF_PLAY_METHOD" >> "$log"
```

For a plain text log without a script, an **Append to file** step with the line `{{LOCAL_DATE}} {{LOCAL_TIME}} {{USER_NAME}} played {{ITEM_NAME}}` does the same *(template)*.

### Pass values as arguments *(template)*

| Setting | Value |
|---|---|
| Trigger | **User created** |
| Step | **Run command**, command `/scripts/new-user.sh`, arguments (one per line): <br> `{{USER_NAME}}` <br> `{{USER_ID}}` |

Each line arrives as exactly one argument, `$1` and `$2`, however many spaces or quotes the name has:

```sh
#!/bin/sh
echo "$(date) created $1 ($2)" >> /scripts/new-users.log
```

### Read the whole event as JSON

`JF_EVENT_JSON` holds every variable as one JSON object, which suits a language with a JSON parser. Python is not in the official Docker image, so mount it or use a different image.

```python
#!/usr/bin/env python3
import json
import os

event = json.loads(os.environ["JF_EVENT_JSON"])
print(event["EVENT"], event.get("ITEM_NAME", ""), event.get("USER_NAME", ""))
```

Anything the script prints is kept as the step's output on the **History** tab.

### Pipes and redirects

Choose **Through the shell** to write one `/bin/sh` line. Placeholders aren't filled there, so use the environment variables:

```sh
echo "$JF_LOCAL_DATE $JF_USER_NAME $JF_ITEM_NAME" | tee -a /scripts/plays.log
```

## Reusing steps

### One notifier for many scripts

Put your notification in one script and call it from others with **Run another script**, so the URL and token live in one place.

1. Make a script named *Notify me*, with no triggers and enabled. Give it an **HTTP request** step to your ntfy topic or Discord webhook, with a body such as `{{EVENT}}: {{USER_NAME}} {{ITEM_NAME}}`.
2. In any other script, add **Run another script** and choose *Notify me*.

The helper runs with the calling script's event, so every event placeholder works. `SCRIPT_NAME` is the helper's own name. Scripts may call each other up to 3 levels deep, and a disabled helper is skipped.
