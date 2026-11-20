// scripts_templates.js
// Starter scripts for the New dialog and request presets for the HTTP step. Templates stay generic: none limits
// itself to particular users or libraries, so each works on any server as is and is a starting point to edit.

import { t } from './configurationpage?name=scripts_common.js';

function step(type, fields) {
    return Object.assign({ Type: type }, fields || {});
}

function condition(field, operator, values) {
    return { Field: field, Operator: operator, Values: values };
}

// templates
// The starter scripts, with their names in the viewer's language. Each build gives a fresh script body.
//
// Output
// [Array]: { key, label, build } for each template.
export function templates() {
    return [
        {
            key: 'blank',
            label: t.tr('TemplateBlank'),
            build: function () { return {}; }
        },
        {
            key: 'stop-long-pause',
            label: t.tr('TemplateStopLongPause'),
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
            label: t.tr('TemplateStillWatching'),
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
            label: t.tr('TemplateTranscodeLimit'),
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
            label: t.tr('TemplateRemoteTranscode'),
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
            label: t.tr('TemplateWelcome'),
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
            label: t.tr('TemplateRestartWarning'),
            build: function () {
                return {
                    Triggers: ['PendingRestart'],
                    Operations: [step('SendMessage', { Target: 'AllSessions', Title: 'Restart coming', Text: 'The server needs a restart and may go offline briefly.', DisplaySeconds: 15 })]
                };
            }
        },
        {
            key: 'admin-failed-task',
            label: t.tr('TemplateAdminFailedTask'),
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
            label: t.tr('TemplateDiscordNewMedia'),
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
            label: t.tr('TemplateNtfyFailedSignin'),
            build: function () {
                return {
                    Triggers: ['AuthenticationFailure'],
                    Operations: [step('HttpRequest', presetFields('ntfy', { Body: 'Failed sign in for {{USERNAME_ATTEMPTED}} from {{REMOTE_ADDRESS}} ({{DEVICE_NAME}})' }))]
                };
            }
        },
        {
            key: 'home-assistant-lights',
            label: t.tr('TemplateHomeAssistantLights'),
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
            label: t.tr('TemplateNightlyScan'),
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
            key: 'tag-unwatched',
            label: t.tr('TemplateTagUnwatched'),
            build: function () {
                return {
                    Triggers: ['Schedule'],
                    ScheduleMode: 'Daily',
                    ScheduleTime: '04:00',
                    ScheduleDays: ['Sunday'],
                    ForEach: 'Items',
                    ForEachLimit: 100,
                    ItemKinds: ['Movie'],
                    Conditions: [condition('DaysSincePlayed', 'AtLeast', ['90'])],
                    Operations: [step('AddTag', { Tag: 'unwatched' })]
                };
            }
        },
        {
            key: 'webhook-scan',
            label: t.tr('TemplateWebhookScan'),
            build: function () {
                return {
                    Triggers: ['Webhook'],
                    CooldownSeconds: 60,
                    Operations: [step('ScanLibraries'), step('WriteActivityLog', { Title: 'Library scan requested by webhook', Text: 'From {{REMOTE_ADDRESS}}' })]
                };
            }
        },
        {
            key: 'check-then-notify',
            label: t.tr('TemplateCheckThenNotify'),
            build: function () {
                return {
                    Triggers: ['Schedule'],
                    ScheduleMode: 'Interval',
                    ScheduleIntervalMinutes: 15,
                    Operations: [
                        step('HttpRequest', { Method: 'GET', Url: 'https://example.com/health', Body: '', OutputName: 'HEALTH', ContinueOnError: true }),
                        step('StopScript', { OnlyIfVariable: 'HEALTH_STATUS', OnlyIfOperator: 'Is', OnlyIfValue: '200' }),
                        step('SendMessage', { Target: 'AdminSessions', Title: 'Service check failed', Text: 'example.com answered {{HEALTH_STATUS}}.', DisplaySeconds: 15 })
                    ]
                };
            }
        },
        {
            key: 'tag-new',
            label: t.tr('TemplateTagNew'),
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
            label: t.tr('TemplateLogPlayback'),
            build: function () {
                return {
                    Triggers: ['PlaybackStart'],
                    Operations: [step('AppendToFile', { FilePath: '/config/log/playback.log', Text: '{{LOCAL_DATE}} {{LOCAL_TIME}} {{USER_NAME}} played {{SERIES_NAME}} {{ITEM_NAME}} on {{DEVICE_NAME}} ({{PLAY_METHOD}})' })]
                };
            }
        },
        {
            key: 'command-new-user',
            label: t.tr('TemplateCommandNewUser'),
            build: function () {
                return {
                    Triggers: ['UserCreated'],
                    Operations: [step('RunCommand', { Command: '/scripts/new-user.sh', Arguments: '{{USER_NAME}}\n{{USER_ID}}' })]
                };
            }
        }
    ];
}

// HTTP step presets: each fills the method, URL, headers, content type, and body for a common service. URLs and
// tokens are placeholders to replace; a token belongs in the script's secrets.
export var HTTP_PRESETS = {
    generic: {
        fields: { Method: 'POST', Url: 'https://example.com/webhook', Headers: '', ContentType: 'application/json', Body: '{ "event": "{{EVENT}}", "user": "{{USER_NAME:json}}", "item": "{{ITEM_NAME:json}}" }' }
    },
    discord: {
        fields: { Method: 'POST', Url: 'https://discord.com/api/webhooks/ID/TOKEN', Headers: '', ContentType: 'application/json', Body: '{ "content": "{{EVENT}}: {{ITEM_NAME:json}}" }' }
    },
    slack: {
        fields: { Method: 'POST', Url: 'https://hooks.slack.com/services/T000/B000/XXXX', Headers: '', ContentType: 'application/json', Body: '{ "text": "{{EVENT}}: {{ITEM_NAME:json}}" }' }
    },
    ntfy: {
        fields: { Method: 'POST', Url: 'https://ntfy.sh/your-topic', Headers: 'Title: Jellyfin {{EVENT}}', ContentType: 'text/plain', Body: '{{EVENT}}: {{ITEM_NAME}}' }
    },
    gotify: {
        fields: { Method: 'POST', Url: 'https://gotify.example.com/message?token={{SECRET_GOTIFY_TOKEN:url}}', Headers: '', ContentType: 'application/json', Body: '{ "title": "Jellyfin {{EVENT}}", "message": "{{ITEM_NAME:json}}", "priority": 5 }' }
    },
    homeassistant: {
        fields: { Method: 'POST', Url: 'http://homeassistant.local:8123/api/services/light/turn_on', Headers: 'Authorization: Bearer {{SECRET_HA_TOKEN}}', ContentType: 'application/json', Body: '{ "entity_id": "light.living_room", "brightness_pct": 10 }' }
    }
};

// httpPresetLabels
// The name of each HTTP step preset, in the viewer's language.
//
// Output
// [object]: The name of each preset, by its key.
export function httpPresetLabels() {
    return {
        generic: t.tr('PresetGeneric'),
        discord: t.tr('PresetDiscord'),
        slack: t.tr('PresetSlack'),
        ntfy: t.tr('PresetNtfy'),
        gotify: t.tr('PresetGotify'),
        homeassistant: t.tr('PresetHomeAssistant')
    };
}

// presetFields
// One preset's fields, with some replaced.
//
// Inputs
// key [string]: The preset, such as discord.
// overrides [object]: Fields to use in place of the preset's [optional].
//
// Output
// [object]: The fields.
export function presetFields(key, overrides) {
    return Object.assign({}, HTTP_PRESETS[key].fields, overrides || {});
}
