// Starter scripts for the New dialog and request presets for the HTTP step. Templates stay generic: none limits
// itself to particular users or libraries, so each works on any server as is and is a starting point to edit.

function step(type, fields) {
    return Object.assign({ Type: type }, fields || {});
}

function condition(field, operator, values) {
    return { Field: field, Operator: operator, Values: values };
}

export var TEMPLATES = [
    {
        key: 'blank',
        label: 'Blank script',
        build: function () { return {}; }
    },
    {
        key: 'stop-long-pause',
        label: 'Stop playback paused for 30 minutes',
        build: function () {
            return {
                Triggers: ['PlaybackPausedFor'],
                PausedForMinutes: 30,
                Operations: [step('StopPlayback', { Title: 'Playback stopped', Text: 'Stopped after 30 minutes paused.', DisplaySeconds: 10 })]
            };
        }
    },
    {
        key: 'still-watching',
        label: 'Ask "Still watching?" after 3 hours of playback',
        build: function () {
            return {
                Triggers: ['PlaybackPlayingFor'],
                PlayingForMinutes: 180,
                Operations: [step('SendMessage', { Target: 'EventSession', Title: 'Still watching?', Text: 'You have been watching for 3 hours.', DisplaySeconds: 15 })]
            };
        }
    },
    {
        key: 'transcode-limit',
        label: 'Limit the server to 3 transcodes',
        build: function () {
            return {
                Triggers: ['PlaybackStart'],
                Conditions: [condition('PlayMethod', 'Is', ['Transcode']), condition('ActiveTranscodes', 'AtLeast', ['4'])],
                Operations: [
                    step('SendMessage', { Target: 'EventSession', Title: 'Server busy', Text: 'Too many streams are converting right now. Try a lower quality or direct play.', DisplaySeconds: 15 }),
                    step('StopPlayback')
                ]
            };
        }
    },
    {
        key: 'remote-transcode',
        label: 'Stop transcoding outside the home network',
        build: function () {
            return {
                Triggers: ['PlaybackStart'],
                Conditions: [condition('PlayMethod', 'Is', ['Transcode']), condition('IsLocal', 'Is', ['false'])],
                Operations: [
                    step('SendMessage', { Target: 'EventSession', Title: 'Direct play only', Text: 'Away from home, choose a quality your device can play directly.', DisplaySeconds: 15 }),
                    step('StopPlayback')
                ]
            };
        }
    },
    {
        key: 'welcome',
        label: 'Greet users when their client connects',
        build: function () {
            return {
                Triggers: ['ClientConnected'],
                CooldownSeconds: 3600,
                Operations: [step('SendMessage', { Target: 'EventSession', Title: 'Welcome', Text: 'Welcome back, {{USER_NAME}}.', DisplaySeconds: 8 })]
            };
        }
    },
    {
        key: 'restart-warning',
        label: 'Warn everyone when a restart is needed',
        build: function () {
            return {
                Triggers: ['PendingRestart'],
                Operations: [step('SendMessage', { Target: 'AllSessions', Title: 'Restart coming', Text: 'The server needs a restart and may go offline briefly.', DisplaySeconds: 15 })]
            };
        }
    },
    {
        key: 'admin-failed-task',
        label: 'Message administrators when a task fails',
        build: function () {
            return {
                Triggers: ['TaskCompleted'],
                Conditions: [condition('TaskStatus', 'Is', ['Failed'])],
                Operations: [step('SendMessage', { Target: 'AdminSessions', Title: 'Task failed', Text: '{{TASK_NAME}} failed: {{TASK_ERROR}}', DisplaySeconds: 20 })]
            };
        }
    },
    {
        key: 'discord-new-media',
        label: 'Post new movies and episodes to Discord',
        build: function () {
            return {
                Triggers: ['ItemAdded'],
                ItemKinds: ['Movie', 'Episode'],
                Operations: [step('HttpRequest', presetFields('discord', { Body: '{ "content": "New on {{SERVER_NAME:json}}: {{SERIES_NAME:json}} {{ITEM_NAME:json}} ({{YEAR}})" }' }))]
            };
        }
    },
    {
        key: 'ntfy-failed-signin',
        label: 'Send a ntfy alert on a failed sign in',
        build: function () {
            return {
                Triggers: ['AuthenticationFailure'],
                Operations: [step('HttpRequest', presetFields('ntfy', { Body: 'Failed sign in for {{USERNAME_ATTEMPTED}} from {{REMOTE_ADDRESS}} ({{DEVICE_NAME}})' }))]
            };
        }
    },
    {
        key: 'home-assistant-lights',
        label: 'Dim Home Assistant lights when playback starts',
        build: function () {
            return {
                Triggers: ['PlaybackStart', 'PlaybackResume'],
                Secrets: [{ Name: 'HA_TOKEN', Value: '' }],
                Operations: [step('HttpRequest', presetFields('homeassistant', {}))]
            };
        }
    },
    {
        key: 'nightly-scan',
        label: 'Scan every library nightly at 3:00',
        build: function () {
            return {
                Triggers: ['Schedule'],
                ScheduleMode: 'Daily',
                ScheduleTime: '03:00',
                Operations: [step('ScanLibraries')]
            };
        }
    },
    {
        key: 'tag-new',
        label: 'Tag new items "new"',
        build: function () {
            return {
                Triggers: ['ItemAdded'],
                ItemKinds: ['Movie', 'Series', 'Episode'],
                Operations: [step('AddTag', { Tag: 'new' })]
            };
        }
    },
    {
        key: 'log-playback',
        label: 'Log every playback to a file',
        build: function () {
            return {
                Triggers: ['PlaybackStart'],
                Operations: [step('AppendToFile', { FilePath: '/config/log/playback.log', Text: '{{LOCAL_DATE}} {{LOCAL_TIME}} {{USER_NAME}} played {{SERIES_NAME}} {{ITEM_NAME}} on {{DEVICE_NAME}} ({{PLAY_METHOD}})' })]
            };
        }
    },
    {
        key: 'command-new-user',
        label: 'Run a script when a user is created',
        build: function () {
            return {
                Triggers: ['UserCreated'],
                Operations: [step('RunCommand', { Command: '/scripts/new-user.sh', Arguments: '{{USER_NAME}}\n{{USER_ID}}' })]
            };
        }
    }
];

// HTTP step presets: each fills the method, URL, headers, content type, and body for a common service. URLs and
// tokens are placeholders to replace; a token belongs in the script's secrets.
export var HTTP_PRESETS = {
    generic: {
        label: 'Generic JSON webhook',
        fields: { Method: 'POST', Url: 'https://example.com/webhook', Headers: '', ContentType: 'application/json', Body: '{ "event": "{{EVENT}}", "user": "{{USER_NAME:json}}", "item": "{{ITEM_NAME:json}}" }' }
    },
    discord: {
        label: 'Discord webhook',
        fields: { Method: 'POST', Url: 'https://discord.com/api/webhooks/ID/TOKEN', Headers: '', ContentType: 'application/json', Body: '{ "content": "{{EVENT}}: {{ITEM_NAME:json}}" }' }
    },
    slack: {
        label: 'Slack webhook',
        fields: { Method: 'POST', Url: 'https://hooks.slack.com/services/T000/B000/XXXX', Headers: '', ContentType: 'application/json', Body: '{ "text": "{{EVENT}}: {{ITEM_NAME:json}}" }' }
    },
    ntfy: {
        label: 'ntfy',
        fields: { Method: 'POST', Url: 'https://ntfy.sh/your-topic', Headers: 'Title: Jellyfin {{EVENT}}', ContentType: 'text/plain', Body: '{{EVENT}}: {{ITEM_NAME}}' }
    },
    gotify: {
        label: 'Gotify',
        fields: { Method: 'POST', Url: 'https://gotify.example.com/message?token={{SECRET_GOTIFY_TOKEN:url}}', Headers: '', ContentType: 'application/json', Body: '{ "title": "Jellyfin {{EVENT}}", "message": "{{ITEM_NAME:json}}", "priority": 5 }' }
    },
    homeassistant: {
        label: 'Home Assistant service call',
        fields: { Method: 'POST', Url: 'http://homeassistant.local:8123/api/services/light/turn_on', Headers: 'Authorization: Bearer {{SECRET_HA_TOKEN}}', ContentType: 'application/json', Body: '{ "entity_id": "light.living_room", "brightness_pct": 10 }' }
    }
};

export function presetFields(key, overrides) {
    return Object.assign({}, HTTP_PRESETS[key].fields, overrides || {});
}
