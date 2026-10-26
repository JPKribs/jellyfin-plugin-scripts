// Shared by the three Scripts dashboard tabs: the plugin ID, the tab bar, and the labels the server's enum
// names display as.

export var PLUGIN_ID = '449817c2-7661-46fb-b302-bbd6dd3b0a01';

export var TABS = [
    { href: 'configurationpage?name=scripts_scripts', name: 'Scripts' },
    { href: 'configurationpage?name=scripts_history', name: 'History' },
    { href: 'configurationpage?name=scripts_settings', name: 'Settings' }
];

export var FIELD_LABELS = {
    User: 'User',
    Library: 'Library',
    ItemName: 'Item name',
    SeriesName: 'Series name',
    Client: 'Client',
    DeviceName: 'Device name',
    PlayMethod: 'Play method',
    IsLocal: 'Is local network',
    PlayedToCompletion: 'Played to completion',
    UsernameAttempted: 'Username attempted',
    TaskKey: 'Scheduled task',
    TaskStatus: 'Task status'
};

export var OPERATOR_LABELS = {
    Is: 'is',
    IsNot: 'is not',
    Contains: 'contains',
    DoesNotContain: 'does not contain',
    StartsWith: 'starts with',
    MatchesRegex: 'matches regex'
};

// needs: what the event must carry for the step to act. A step whose need is missing reports Skipped.
export var OPERATION_TYPES = {
    RunCommand: { label: 'Run command', needs: null },
    HttpRequest: { label: 'HTTP request', needs: null },
    SendMessage: { label: 'Send message to session', needs: 'session' },
    StopPlayback: { label: 'Stop playback', needs: 'session' },
    RefreshMetadata: { label: 'Refresh item metadata', needs: 'item' },
    RunScheduledTask: { label: 'Run scheduled task', needs: null },
    WriteActivityLog: { label: 'Write to activity log', needs: null },
    Wait: { label: 'Wait', needs: null }
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
            '<strong>' + shared.escapeHtml(type) + '</strong>' +
            '<span>' + shared.escapeHtml(step.Message || '') + '</span></div>' +
            (step.Output ? '<pre class="jpk-error-message scripts-output">' + shared.escapeHtml(step.Output) + '</pre>' : '') +
            '</li>';
    }).join('') + '</ol>';
}
