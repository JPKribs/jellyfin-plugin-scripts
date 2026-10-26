# Scripts

![Scripts](assets/logo.png){ width="160" }

Scripts is a Jellyfin plugin that acts on server events, much like Tautulli's notification scripts: playback starting, stopping, or staying paused, items and collections changing, users signing in or being created, scheduled tasks and plugins, and timed schedules. It offers more than 45 triggers.

## How It Works

Each script names the **triggers** it runs on, the **item kinds** and **conditions** that narrow them, and the **steps** it performs. More than 20 built-in steps cover common jobs without any scripting language, such as sending an HTTP request to Discord, Slack, ntfy, Gotify, or Home Assistant, messaging, pausing, or stopping sessions, tagging items or adding them to collections, disabling or signing out users, scanning libraries, or queuing a scheduled task. Templates start you off with common scripts, and tokens are kept as encrypted secrets. When you need more, a **Run command** step runs any script or program the server can reach, with the event's details as environment variables. See [Writing Scripts](Scripts.md).

The **History** tab lists recent runs with each step's result and output, and the **Settings** tab sets how many run at once, the default command timeout, and how much history is kept.

!!! warning
    A command runs on the server as the account Jellyfin runs under. **Only add commands you trust!**
