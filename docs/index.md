# Scripts

![Scripts](assets/logo.png){ width="160" }

Scripts is a Jellyfin plugin that acts on server events: playback starting, stopping, pausing, or resuming, items being added or removed, sign ins, and scheduled tasks finishing.

## How It Works

Each script names the **triggers** it runs on, the **item kinds** and **conditions** that narrow them, and the **steps** it performs. Steps cover common jobs without any scripting language, such as sending an HTTP request, messaging or stopping the session, refreshing metadata, queuing a scheduled task, or writing to the activity log. When you need more, a **Run command** step runs any script or program the server can reach, with the event's details as environment variables. See [Writing Scripts](Scripts.md).

The **History** tab lists recent runs with each step's result and output, and the **Settings** tab sets how many run at once, the default command timeout, and how much history is kept.

!!! warning
    A command runs on the server as the account Jellyfin runs under. **Only add commands you trust!**
