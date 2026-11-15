// scripts_common.js
// Shared by the three Scripts tabs: the only module that imports Base, the page wrapper, the tab bar, and the
// labels the server's enum names display as.

import {
    createConfigPage, createPaginatedTable, createChipSelect, createCheckboxList, createUserMultiSelector,
    createSortableCardList, createChoiceGroup, createCopyField, createTimeline, createDefinitionList,
    createRepeater, createDataTable, confirmDialog, promptDialog, openDialog, generateGuid, downloadJson, pickJsonFile,
    bindSecretField, clearSecretField, readSecretField, SECRET_KEPT
} from './configurationpage?name=scripts_jpkribs_shared.js';

export {
    createPaginatedTable, createRepeater, createDataTable, createChipSelect, createCheckboxList, createUserMultiSelector, createSortableCardList,
    createChoiceGroup, createCopyField, confirmDialog, promptDialog, openDialog, generateGuid, downloadJson,
    pickJsonFile, bindSecretField, clearSecretField, readSecretField, SECRET_KEPT
};

export var PLUGIN_ID = '449817c2-7661-46fb-b302-bbd6dd3b0a01';

export var TABS = [
    { href: 'configurationpage?name=scripts_scripts', name: 'Scripts' },
    { href: 'configurationpage?name=scripts_history', name: 'History' },
    { href: 'configurationpage?name=scripts_settings', name: 'Settings' }
];

// createScriptsPage
// Wraps Base's createConfigPage with the plugin ID and the tab bar, so each tab names only its index.
//
// Inputs
// view [HTMLElement]: The page the dashboard passes to the module.
// tabIndex [number]: The tab this page is, from 0.
// options [object]: The rest of createConfigPage's options, such as bind, load, and isDirty.
//
// Output
// [object]: The page handle from createConfigPage.
export function createScriptsPage(view, tabIndex, options) {
    return createConfigPage(view, Object.assign({
        pluginId: PLUGIN_ID,
        tabsKey: 'scripts',
        tabIndex: tabIndex,
        tabs: TABS
    }, options));
}

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
        { key: 'DaysSinceAdded', label: 'Days since added', numeric: true },
        { key: 'HasSubtitles', label: 'Has subtitles' },
        { key: 'CollectionName', label: 'Collection' }
    ] },
    { label: 'For each library item', fields: [
        { key: 'PlayedByAnyone', label: 'Played by anyone' },
        { key: 'DaysSincePlayed', label: 'Days since last played', numeric: true }
    ] },
    { label: 'For each user', fields: [
        { key: 'DaysSinceActive', label: 'Days since last active', numeric: true },
        { key: 'IsDisabled', label: 'Is disabled' }
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
    StopScript: { label: 'Stop here', group: 'Run', needs: null },
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

// renderRunSteps
// Builds a run's steps as a Base Timeline, each with its status, step type, message, and any captured output.
//
// Inputs
// shared [object]: The helper bag from createShared.
// run [object]: A run from the history or a test.
//
// Output
// [HTMLElement]: The timeline, or an empty state when the script has no steps.
export function renderRunSteps(shared, run) {
    var timeline = createTimeline({
        emptyText: 'This script has no steps.',
        statusLabels: { green: 'Succeeded', red: 'Failed', gray: 'Skipped' },
        items: (run.Operations || []).map(function (step) {
            return {
                title: OPERATION_TYPES[step.Type] ? OPERATION_TYPES[step.Type].label : step.Type,
                body: step.Message || '',
                pre: step.Output || '',
                status: STATUS_BADGES[step.Status] || 'gray'
            };
        })
    });
    return timeline.element;
}

// renderRunVariables
// Builds a run's variables as a Base Definition List, named as a command reads them.
//
// Inputs
// shared [object]: The helper bag from createShared.
// run [object]: A run from the history.
//
// Output
// [HTMLElement]: The list of variables.
export function renderRunVariables(shared, run) {
    var variables = run.Variables || {};
    var names = Object.keys(variables).sort();
    if (!names.length) {
        var empty = document.createElement('div');
        empty.innerHTML = shared.emptySection('No variables.', { inline: true });
        return empty;
    }
    return createDefinitionList({
        items: names.map(function (name) {
            var value = variables[name] === null || variables[name] === undefined ? '' : String(variables[name]);
            return { label: 'JF_' + name, value: { text: value, mono: true } };
        })
    }).element;
}

// formatDuration
// Shows a run's length in milliseconds under a second, and as Base's duration text above it.
//
// Inputs
// shared [object]: The helper bag from createShared.
// ms [number]: The length in milliseconds.
//
// Output
// [string]: The length as text.
export function formatDuration(shared, ms) {
    return ms < 1000 ? ms + ' ms' : shared.formatDuration(Math.round(ms / 1000));
}
