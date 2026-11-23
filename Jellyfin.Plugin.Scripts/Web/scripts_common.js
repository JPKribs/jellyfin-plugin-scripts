// scripts_common.js
// Shared by the three Scripts tabs: the only module that imports Base, the page wrapper, the tab bar, the plugin's
// strings, and the labels the server's enum names display as.

import {
    createConfigPage, createChipSelect, createTagInput, createComboBox, adornField, createCheckboxList, createUserMultiSelector, createSortableCardList,
    createChoiceGroup, createCopyField, createTimeline, createDefinitionList, createRepeater, createDataTable,
    createRecordList, createSaveBar, createSliderField, createDurationField, createToaster, createBarChart, createCollapsibleSection,
    confirmDialog, promptDialog, openDialog, generateGuid, downloadJson, pickJsonFile, setFieldState, SECRET_KEPT,
    loadTranslations, createTranslations, setTabs
} from './configurationpage?name=scripts_jpkribs_shared.js';

export {
    createRepeater, createDataTable, createChipSelect, createTagInput, createComboBox, adornField, createCheckboxList, createUserMultiSelector, createSortableCardList,
    createChoiceGroup, createCopyField, createRecordList, createSaveBar, createSliderField, createDurationField,
    createToaster, createBarChart, createCollapsibleSection, confirmDialog, promptDialog, openDialog, generateGuid, downloadJson, pickJsonFile,
    setFieldState, SECRET_KEPT
};

export var PLUGIN_ID = '449817c2-7661-46fb-b302-bbd6dd3b0a01';

// The plugin's strings once loaded. Until then, and when they cannot load, a key reads as its fallback.
var strings = createTranslations({});
var stringsReady = null;

// t
// Reads the plugin's strings, in the viewer's language once loadStrings has finished.
//
// Output
// [object]: { tr(key, fallback), format(key, ...values), language }.
export var t = {
    tr: function (key, fallback) { return strings.tr(key, fallback); },
    format: function () { return strings.format.apply(strings, arguments); },
    get language() { return strings.language; }
};

// loadStrings
// Loads the plugin's strings in the viewer's language, once per page load.
//
// Output
// [Promise]: Resolves with the translations handle. It never rejects.
export function loadStrings() {
    if (!stringsReady) {
        stringsReady = loadTranslations('Scripts/Translations').then(function (loaded) {
            strings = loaded;
            return loaded;
        });
    }
    return stringsReady;
}

// applyStrings
// Translates a page's marked markup. data-str-rich marks text whose {0}, {1} stand for the element's own child
// elements in order, such as a code sample, which keep their markup.
//
// Inputs
// root [HTMLElement]: The page.
export function applyStrings(root) {
    strings.apply(root);
    root.querySelectorAll('[data-str-rich]').forEach(function (node) {
        var key = node.getAttribute('data-str-rich');
        if (!strings.has(key)) return;
        if (!node.richParts) node.richParts = Array.prototype.map.call(node.children, function (child) { return child; });
        var text = strings.tr(key);
        node.textContent = '';
        text.split(/(\{\d+\})/).forEach(function (part) {
            var match = /^\{(\d+)\}$/.exec(part);
            var child = match ? node.richParts[+match[1]] : null;
            node.appendChild(child || document.createTextNode(part));
        });
    });
}

// TABS
// The tab bar, in the viewer's language once the strings load.
//
// Output
// [Array]: { href, name } for each tab.
export function TABS() {
    return [
        { href: 'configurationpage?name=scripts_scripts', name: t.tr('TabScripts', 'Scripts') },
        { href: 'configurationpage?name=scripts_history', name: t.tr('TabHistory', 'History') },
        { href: 'configurationpage?name=scripts_settings', name: t.tr('Settings', 'Settings') }
    ];
}

// createScriptsPage
// Wraps Base's createConfigPage with the plugin ID and the tab bar, so each tab names only its index. bind and
// load wait for the plugin's strings, and the page's marked markup is translated before bind runs.
//
// Inputs
// view [HTMLElement]: The page the dashboard passes to the module.
// tabIndex [number]: The tab this page is, from 0.
// options [object]: The rest of createConfigPage's options, such as bind, load, and isDirty.
//
// Output
// [object]: The page handle from createConfigPage.
export function createScriptsPage(view, tabIndex, options) {
    var ready = loadStrings();
    return createConfigPage(view, Object.assign({}, options, {
        pluginId: PLUGIN_ID,
        tabsKey: 'scripts',
        tabIndex: tabIndex,
        tabs: TABS,
        bind: function (shared, page) {
            ready.then(function () {
                setTabs('scripts', tabIndex, TABS);
                applyStrings(view);
                if (options.bind) options.bind(shared, page);
            });
        },
        load: function (shared, page) {
            ready.then(function () {
                if (options.load) options.load(shared, page);
            });
        }
    }));
}

// Fields compared with "at least" and "at most".
export var NUMERIC_FIELDS = {
    Year: true, VideoHeight: true, CommunityRating: true, DaysSinceAdded: true, DaysSincePlayed: true, DaysSinceActive: true,
    ProgressPercent: true, ActiveStreams: true, ActiveTranscodes: true, Hour: true
};

// fieldGroups
// The condition fields, grouped for the field picker.
//
// Output
// [Array]: { label, fields: [{ key, label }] } for each group.
export function fieldGroups() {
    return [
        { label: t.tr('FieldGroupWho'), fields: [
            { key: 'User', label: t.tr('LabelUser') },
            { key: 'IsAdmin', label: t.tr('FieldIsAdmin') },
            { key: 'UsernameAttempted', label: t.tr('FieldUsernameAttempted') }
        ] },
        { label: t.tr('FieldGroupWhere'), fields: [
            { key: 'Client', label: t.tr('FieldClient') },
            { key: 'ClientVersion', label: t.tr('FieldClientVersion') },
            { key: 'DeviceName', label: t.tr('FieldDeviceName') },
            { key: 'DeviceId', label: t.tr('FieldDeviceId') },
            { key: 'RemoteAddress', label: t.tr('FieldRemoteAddress') },
            { key: 'IsLocal', label: t.tr('FieldIsLocal') }
        ] },
        { label: t.tr('FieldGroupWhat'), fields: [
            { key: 'Library', label: t.tr('TasksLibraryCategory') },
            { key: 'ItemName', label: t.tr('FieldItemName') },
            { key: 'SeriesName', label: t.tr('FieldSeriesName') },
            { key: 'ItemPath', label: t.tr('FieldItemPath') },
            { key: 'Genre', label: t.tr('Genre') },
            { key: 'Tag', label: t.tr('LabelTag') },
            { key: 'Studio', label: t.tr('Studio') },
            { key: 'OfficialRating', label: t.tr('ParentalRating') },
            { key: 'Year', label: t.tr('LabelYear') },
            { key: 'VideoHeight', label: t.tr('FieldVideoHeight') },
            { key: 'CommunityRating', label: t.tr('CommunityRating') },
            { key: 'DaysSinceAdded', label: t.tr('FieldDaysSinceAdded') },
            { key: 'HasSubtitles', label: t.tr('FieldHasSubtitles') },
            { key: 'CollectionName', label: t.tr('LabelCollection') }
        ] },
        { label: t.tr('FieldGroupForEachItem'), fields: [
            { key: 'PlayedByAnyone', label: t.tr('FieldPlayedByAnyone') },
            { key: 'DaysSincePlayed', label: t.tr('FieldDaysSincePlayed') }
        ] },
        { label: t.tr('FieldGroupForEachUser'), fields: [
            { key: 'DaysSinceActive', label: t.tr('FieldDaysSinceActive') },
            { key: 'IsDisabled', label: t.tr('FieldIsDisabled') }
        ] },
        { label: t.tr('TitlePlayback'), fields: [
            { key: 'PlayMethod', label: t.tr('LabelPlayMethod') },
            { key: 'ProgressPercent', label: t.tr('FieldProgressPercent') },
            { key: 'PlayedToCompletion', label: t.tr('FieldPlayedToCompletion') },
            { key: 'TranscodeReasons', label: t.tr('FieldTranscodeReasons') },
            { key: 'VideoCodec', label: t.tr('LabelVideoCodec') },
            { key: 'AudioCodec', label: t.tr('LabelAudioCodec') },
            { key: 'ActiveStreams', label: t.tr('FieldActiveStreams') },
            { key: 'ActiveTranscodes', label: t.tr('FieldActiveTranscodes') },
            { key: 'IsPlayed', label: t.tr('FieldIsPlayed') },
            { key: 'IsFavorite', label: t.tr('FieldIsFavorite') }
        ] },
        { label: t.tr('FieldGroupWhen'), fields: [
            { key: 'Hour', label: t.tr('FieldHour') },
            { key: 'DayOfWeek', label: t.tr('LabelAccessDay') }
        ] },
        { label: t.tr('LabelSystem'), fields: [
            { key: 'TaskKey', label: t.tr('LabelScheduledTask') },
            { key: 'TaskStatus', label: t.tr('FieldTaskStatus') },
            { key: 'TaskCategory', label: t.tr('FieldTaskCategory') },
            { key: 'PluginName', label: t.tr('FieldPluginName') },
            { key: 'Provider', label: t.tr('FieldProvider') },
            { key: 'UpdateReason', label: t.tr('FieldUpdateReason') },
            { key: 'ActivityType', label: t.tr('FieldActivityType') },
            { key: 'ActivitySeverity', label: t.tr('FieldActivitySeverity') }
        ] }
    ];
}

// fieldLabel
// The label of one condition field.
//
// Inputs
// key [string]: The field, such as DeviceName.
//
// Output
// [string]: The label, or an empty string for a field this version does not know.
export function fieldLabel(key) {
    var found = '';
    fieldGroups().forEach(function (group) {
        group.fields.forEach(function (f) { if (f.key === key) found = f.label; });
    });
    return found;
}

// operatorLabels
// The condition operators and how each reads.
//
// Output
// [object]: The label of each operator, by its name.
export function operatorLabels() {
    return {
        Is: t.tr('OperatorIs'),
        IsNot: t.tr('OperatorIsNot'),
        Contains: t.tr('OperatorContains'),
        DoesNotContain: t.tr('OperatorDoesNotContain'),
        StartsWith: t.tr('OperatorStartsWith'),
        MatchesRegex: t.tr('OperatorMatchesRegex'),
        AtLeast: t.tr('OperatorAtLeast'),
        AtMost: t.tr('OperatorAtMost')
    };
}

// operationTypes
// The step types, each with its label, its group in the step picker, and what the event must carry for the step
// to act. A step whose need is missing reports Skipped.
//
// Output
// [object]: { label, group, needs } for each step type, by its name.
export function operationTypes() {
    var run = t.tr('LabelRun');
    var sessions = t.tr('TriggerGroupSessions');
    var items = t.tr('Items');
    var users = t.tr('HeaderUsers');
    var server = t.tr('TabServer');
    return {
        RunCommand: { label: t.tr('StepRunCommand'), group: run, needs: null },
        HttpRequest: { label: t.tr('StepHttpRequest'), group: run, needs: null },
        RunScript: { label: t.tr('StepRunScript'), group: run, needs: null },
        Wait: { label: t.tr('StepWait'), group: run, needs: null },
        StopScript: { label: t.tr('StepStopScript'), group: run, needs: null },
        SendMessage: { label: t.tr('SendMessage'), group: sessions, needs: null },
        PausePlayback: { label: t.tr('StepPausePlayback'), group: sessions, needs: 'session' },
        StopPlayback: { label: t.tr('StopPlayback'), group: sessions, needs: 'session' },
        MarkPlayed: { label: t.tr('MarkPlayed'), group: items, needs: 'useritem' },
        MarkUnplayed: { label: t.tr('MarkUnplayed'), group: items, needs: 'useritem' },
        SetFavorite: { label: t.tr('StepSetFavorite'), group: items, needs: 'useritem' },
        AddTag: { label: t.tr('StepAddTag'), group: items, needs: 'item' },
        RemoveTag: { label: t.tr('StepRemoveTag'), group: items, needs: 'item' },
        AddToCollection: { label: t.tr('AddToCollection'), group: items, needs: 'item' },
        RemoveFromCollection: { label: t.tr('RemoveFromCollection'), group: items, needs: 'item' },
        RefreshMetadata: { label: t.tr('RefreshMetadata'), group: items, needs: 'item' },
        SetUserDisabled: { label: t.tr('StepSetUserDisabled'), group: users, needs: 'user' },
        SignOutUser: { label: t.tr('StepSignOutUser'), group: users, needs: 'user' },
        ScanLibraries: { label: t.tr('StepScanLibraries'), group: server, needs: null },
        RunScheduledTask: { label: t.tr('StepRunScheduledTask'), group: server, needs: null },
        WriteActivityLog: { label: t.tr('StepWriteActivityLog'), group: server, needs: null },
        AppendToFile: { label: t.tr('StepAppendToFile'), group: server, needs: null }
    };
}

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
        emptyText: t.tr('EmptyRunSteps'),
        statusLabels: { green: t.tr('StatusSucceeded'), gray: t.tr('StatusSkipped') },
        items: (run.Operations || []).map(function (step) {
            var types = operationTypes();
            return {
                title: types[step.Type] ? types[step.Type].label : step.Type,
                body: step.Message || '',
                pre: step.Output || '',
                status: STATUS_BADGES[step.Status] || 'gray'
            };
        })
    });
    return timeline.element;
}

// renderRunSummary
// Builds a run's result, trigger, start, and length as a Base Definition List in columns.
//
// Inputs
// shared [object]: The helper bag from createShared.
// run [object]: A run from the history or a test.
// details [Array]: More { label, value } items after the result, such as the trigger and the start [optional].
//
// Output
// [HTMLElement]: The list.
export function renderRunSummary(shared, run, details) {
    return createDefinitionList({
        layout: 'columns',
        items: [{ label: t.tr('LabelResult'), value: { badge: { label: run.Succeeded ? t.tr('StatusSucceeded') : t.tr('LabelFailed'), color: run.Succeeded ? 'green' : 'red' } } }]
            .concat(details || [])
            .concat([{ label: t.tr('LabelLength'), value: formatDuration(shared, run.DurationMs) }])
    }).element;
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
        empty.innerHTML = shared.emptySection(t.tr('EmptyVariables'), { inline: true });
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
    return ms < 1000 ? t.format('DurationMilliseconds', ms) : shared.formatDuration(Math.round(ms / 1000));
}
