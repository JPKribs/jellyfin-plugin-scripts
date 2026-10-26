// Shared by the three Scripts dashboard tabs: the plugin ID, the tab bar, and the labels the server's enum
// names display as.

export var PLUGIN_ID = '449817c2-7661-46fb-b302-bbd6dd3b0a01';

export var TABS = [
    { href: 'configurationpage?name=scripts_scripts', name: 'Scripts' },
    { href: 'configurationpage?name=scripts_history', name: 'History' },
    { href: 'configurationpage?name=scripts_settings', name: 'Settings' }
];

// Condition fields, grouped for the field picker. numeric marks fields compared with "at least" and "at most".
export var FIELD_GROUPS = [
    { label: 'Who', fields: [
        { key: 'User', label: 'User' },
        { key: 'IsAdmin', label: 'Is administrator' },
        { key: 'UsernameAttempted', label: 'Username attempted' }
    ] },
    { label: 'Where', fields: [
        { key: 'Client', label: 'Client' },
        { key: 'ClientVersion', label: 'Client version' },
        { key: 'DeviceName', label: 'Device name' },
        { key: 'DeviceId', label: 'Device ID' },
        { key: 'RemoteAddress', label: 'Remote address' },
        { key: 'IsLocal', label: 'Is local network' }
    ] },
    { label: 'What', fields: [
        { key: 'Library', label: 'Library' },
        { key: 'ItemName', label: 'Item name' },
        { key: 'SeriesName', label: 'Series name' },
        { key: 'ItemPath', label: 'Item path' },
        { key: 'Genre', label: 'Genre' },
        { key: 'Tag', label: 'Tag' },
        { key: 'Studio', label: 'Studio' },
        { key: 'OfficialRating', label: 'Parental rating' },
        { key: 'Year', label: 'Year', numeric: true },
        { key: 'VideoHeight', label: 'Video height', numeric: true },
        { key: 'CommunityRating', label: 'Community rating', numeric: true },
        { key: 'CollectionName', label: 'Collection' }
    ] },
    { label: 'Playback', fields: [
        { key: 'PlayMethod', label: 'Play method' },
        { key: 'ProgressPercent', label: 'Progress percent', numeric: true },
        { key: 'PlayedToCompletion', label: 'Played to completion' },
        { key: 'TranscodeReasons', label: 'Transcode reasons' },
        { key: 'VideoCodec', label: 'Video codec' },
        { key: 'AudioCodec', label: 'Audio codec' },
        { key: 'ActiveStreams', label: 'Active streams', numeric: true },
        { key: 'ActiveTranscodes', label: 'Active transcodes', numeric: true },
        { key: 'IsPlayed', label: 'Is played' },
        { key: 'IsFavorite', label: 'Is favorite' }
    ] },
    { label: 'When', fields: [
        { key: 'Hour', label: 'Hour (0-23)', numeric: true },
        { key: 'DayOfWeek', label: 'Day of week' }
    ] },
    { label: 'System', fields: [
        { key: 'TaskKey', label: 'Scheduled task' },
        { key: 'TaskStatus', label: 'Task status' },
        { key: 'TaskCategory', label: 'Task category' },
        { key: 'PluginName', label: 'Plugin name' },
        { key: 'Provider', label: 'Provider' },
        { key: 'UpdateReason', label: 'Update reason' },
        { key: 'ActivityType', label: 'Activity type' },
        { key: 'ActivitySeverity', label: 'Activity severity' }
    ] }
];

export var FIELD_LABELS = {};
export var NUMERIC_FIELDS = {};
FIELD_GROUPS.forEach(function (group) {
    group.fields.forEach(function (f) {
        FIELD_LABELS[f.key] = f.label;
        if (f.numeric) NUMERIC_FIELDS[f.key] = true;
    });
});

export var OPERATOR_LABELS = {
    Is: 'is',
    IsNot: 'is not',
    Contains: 'contains',
    DoesNotContain: 'does not contain',
    StartsWith: 'starts with',
    MatchesRegex: 'matches regex',
    AtLeast: 'is at least',
    AtMost: 'is at most'
};

// needs: what the event must carry for the step to act. A step whose need is missing reports Skipped.
export var OPERATION_TYPES = {
    RunCommand: { label: 'Run command', group: 'Run', needs: null },
    HttpRequest: { label: 'HTTP request', group: 'Run', needs: null },
    RunScript: { label: 'Run another script', group: 'Run', needs: null },
    Wait: { label: 'Wait', group: 'Run', needs: null },
    SendMessage: { label: 'Send message', group: 'Sessions', needs: null },
    PausePlayback: { label: 'Pause playback', group: 'Sessions', needs: 'session' },
    StopPlayback: { label: 'Stop playback', group: 'Sessions', needs: 'session' },
    MarkPlayed: { label: 'Mark played', group: 'Items', needs: 'useritem' },
    MarkUnplayed: { label: 'Mark unplayed', group: 'Items', needs: 'useritem' },
    SetFavorite: { label: 'Set favorite', group: 'Items', needs: 'useritem' },
    AddTag: { label: 'Add tag', group: 'Items', needs: 'item' },
    RemoveTag: { label: 'Remove tag', group: 'Items', needs: 'item' },
    AddToCollection: { label: 'Add to collection', group: 'Items', needs: 'item' },
    RemoveFromCollection: { label: 'Remove from collection', group: 'Items', needs: 'item' },
    RefreshMetadata: { label: 'Refresh metadata', group: 'Items', needs: 'item' },
    SetUserDisabled: { label: 'Disable or enable user', group: 'Users', needs: 'user' },
    SignOutUser: { label: 'Sign user out', group: 'Users', needs: 'user' },
    ScanLibraries: { label: 'Scan libraries', group: 'Server', needs: null },
    RunScheduledTask: { label: 'Run scheduled task', group: 'Server', needs: null },
    WriteActivityLog: { label: 'Write to activity log', group: 'Server', needs: null },
    AppendToFile: { label: 'Append to file', group: 'Server', needs: null }
};

export var STATUS_BADGES = { Succeeded: 'green', Failed: 'red', Skipped: 'gray' };

// Renders a run's steps as a list: status badge, step type, message, and any captured output.
export function renderRunSteps(shared, run) {
    var steps = run.Operations || [];
    if (!steps.length) return shared.emptySection('This script has no steps.');
    return '<ol class="scripts-run-steps">' + steps.map(function (step) {
        var type = OPERATION_TYPES[step.Type] ? OPERATION_TYPES[step.Type].label : step.Type;
        return '<li>' +
            '<div class="scripts-run-step-head">' + shared.badge(STATUS_BADGES[step.Status] || 'gray', step.Status) +
            '<strong>' + shared.escapeHtml(type) + '</strong></div>' +
            (step.Message ? '<div class="scripts-run-step-message">' + shared.escapeHtml(step.Message) + '</div>' : '') +
            (step.Output ? '<pre class="jpk-error-message scripts-output">' + shared.escapeHtml(step.Output) + '</pre>' : '') +
            '</li>';
    }).join('') + '</ol>';
}

// Renders a run's variables as a definition list.
export function renderRunVariables(shared, run) {
    var variables = run.Variables || {};
    var names = Object.keys(variables).sort();
    if (!names.length) return shared.emptySection('No variables.');
    return '<dl class="scripts-run-variables">' + names.map(function (name) {
        return '<dt>JF_' + shared.escapeHtml(name) + '</dt><dd>' + shared.escapeHtml(variables[name]) + '</dd>';
    }).join('') + '</dl>';
}

export function formatDuration(shared, ms) {
    return ms < 1000 ? ms + ' ms' : shared.formatDuration(Math.round(ms / 1000));
}
