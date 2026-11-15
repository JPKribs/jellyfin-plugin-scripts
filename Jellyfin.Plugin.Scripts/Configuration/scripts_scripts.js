// scripts_scripts.js
// The Scripts tab: the script picker and the editor for one script's triggers, filters, conditions, steps,
// secrets, and behavior, with Save, Test, and Run now.

import {
    createScriptsPage, createRepeater, createChipSelect, createCheckboxList, createUserMultiSelector, createSortableCardList,
    createChoiceGroup, createCopyField, createRecordList, createSaveBar, createSliderField, createDurationField,
    createToaster, confirmDialog, promptDialog, openDialog, generateGuid, downloadJson, pickJsonFile, setFieldState, SECRET_KEPT,
    FIELD_GROUPS, FIELD_LABELS, NUMERIC_FIELDS, OPERATOR_LABELS, OPERATION_TYPES, renderRunSummary, renderRunSteps
} from './configurationpage?name=scripts_common.js';
import { TEMPLATES, HTTP_PRESETS } from './configurationpage?name=scripts_templates.js';

export default function (view) {
    'use strict';

    var tracker = null;
    var page = createScriptsPage(view, 0, {
        bind: bind,
        load: load,
        isDirty: function () { return !!(tracker && tracker.isDirty()); }
    });
    var Shared = page.shared;

    var DAYS = ['Monday', 'Tuesday', 'Wednesday', 'Thursday', 'Friday', 'Saturday', 'Sunday'];
    var FIXED_VALUES = {
        PlayMethod: ['DirectPlay', 'DirectStream', 'Transcode'],
        IsLocal: ['true', 'false'],
        PlayedToCompletion: ['true', 'false'],
        IsAdmin: ['true', 'false'],
        IsPlayed: ['true', 'false'],
        IsFavorite: ['true', 'false'],
        HasSubtitles: ['true', 'false'],
        PlayedByAnyone: ['true', 'false'],
        IsDisabled: ['true', 'false'],
        TaskStatus: ['Completed', 'Failed', 'Cancelled', 'Aborted'],
        DayOfWeek: DAYS,
        UpdateReason: ['MetadataImport', 'ImageUpdate', 'MetadataDownload', 'MetadataEdit'],
        ActivitySeverity: ['Trace', 'Debug', 'Information', 'Warning', 'Error', 'Critical'],
        Hour: Array.from({ length: 24 }, function (_, h) { return String(h); })
    };
    var HTTP_METHODS = ['POST', 'GET', 'PUT', 'PATCH', 'DELETE'];
    var MESSAGE_TARGETS = [
        { value: 'EventSession', label: 'The session that raised the event' },
        { value: 'UserSessions', label: 'Every session of the event\'s users' },
        { value: 'AllSessions', label: 'Every active session' },
        { value: 'AdminSessions', label: 'Every administrator\'s session' }
    ];

    // Steps whose output a later step can use.
    var HAS_OUTPUT = { RunCommand: true, HttpRequest: true };
    // The script's numbers, each a Base Slider or Duration Field: the mount, the script property, what it shows
    // while the property is missing, and for a trigger's number the trigger and the panel that holds it.
    // A minutes property is edited as a duration and stored in whole minutes.
    var SCRIPT_NUMBERS = [
        { mount: 'pausedMinutesMount', key: 'PausedForMinutes', fallback: 10, trigger: 'PlaybackPausedFor', block: 'pausedOptions', minutes: true, min: 1, max: 1440 },
        { mount: 'playingMinutesMount', key: 'PlayingForMinutes', fallback: 60, trigger: 'PlaybackPlayingFor', block: 'playingOptions', minutes: true, min: 1, max: 1440 },
        { mount: 'progressPercentMount', key: 'ProgressPercentThreshold', fallback: 90, trigger: 'PlaybackProgressReached', block: 'progressOptions', slider: { min: 1, max: 99, suffix: '%', label: 'Percentage' } },
        { mount: 'streamCountMount', key: 'StreamCountThreshold', fallback: 3, trigger: 'StreamCountReached', block: 'streamOptions', slider: { min: 1, max: 100, label: 'Streams' } },
        { mount: 'transcodeCountMount', key: 'TranscodeCountThreshold', fallback: 2, trigger: 'TranscodeCountReached', block: 'transcodeOptions', slider: { min: 1, max: 100, label: 'Transcodes' } },
        { mount: 'forEachLimitMount', key: 'ForEachLimit', fallback: 50, slider: { min: 1, max: 500, label: 'Most runs per event' } },
        { mount: 'cooldownMount', key: 'CooldownSeconds', fallback: 0, units: ['h', 'm', 's'], min: 0, max: 86400 }
    ];
    // A step's numbers, each a Base Slider or Duration Field in the step editor.
    var STEP_NUMBERS = {
        TimeoutSeconds: { units: ['m', 's'], min: 0, max: 3600 },
        Retries: { slider: { min: 0, max: 5, label: 'Retries' } },
        DisplaySeconds: { slider: { min: 0, max: 300, suffix: ' s', label: 'Seconds shown' } },
        WaitSeconds: { units: ['m', 's'], min: 1, max: 3600 }
    };

    var catalog = null;
    var scripts = [];
    var selectedId = null;
    var triggerList = null;
    var itemKindSelect = null;
    var userSelect = null;
    var matchChoice = null;
    var operationList = null;
    var conditionRepeater = null;
    var conditionsQuiet = false;
    var secretRepeater = null;
    var variableList = null;
    var saveBar = null;
    var toaster = null;
    var numberControls = {};
    var editorControls = [];
    var webhookField = null;
    var suppressWidgetEvents = false;

    function el(id) { return view.querySelector('#' + id); }
    function esc(value) { return Shared.escapeHtml(value === null || value === undefined ? '' : String(value)); }
    function compactId(id) { return String(id || '').replace(/-/g, '').toLowerCase(); }

    function current() {
        return scripts.find(function (s) { return s.Id === selectedId; }) || null;
    }

    function triggerInfo(key) {
        return catalog.Triggers.find(function (t) { return t.Key === key; }) || null;
    }

    function fieldInfo(key) {
        return catalog.Fields.find(function (f) { return f.Key === key; }) || null;
    }

    function quoted(t) { return '"' + t.Label + '"'; }

    function selectedTriggers(script) {
        return (script.Triggers || []).map(triggerInfo).filter(Boolean);
    }

    function has(script, trigger) { return (script.Triggers || []).indexOf(trigger) >= 0; }

    // Running for each item or user only happens on an event with no item, or no user, of its own.
    function expands(script, t) {
        if (script.ForEach === 'Items') return !t.HasItem;
        if (script.ForEach === 'Users') return !t.HasUser;
        return false;
    }

    function carriesItem(script, t) { return t.HasItem || script.ForEach === 'Items'; }
    function carriesUser(script, t) { return t.HasUser || script.ForEach === 'Users'; }

    function supplies(script, t, info) {
        return info.Triggers.indexOf(t.Key) >= 0 || (expands(script, t) && (info.ForEach || []).indexOf(script.ForEach) >= 0);
    }

    function newToken() {
        var bytes = new Uint8Array(24);
        window.crypto.getRandomValues(bytes);
        return Array.from(bytes, function (b) { return ('0' + b.toString(16)).slice(-2); }).join('');
    }

    function ensureWebhookToken(script) {
        if (script && has(script, 'Webhook') && !script.WebhookToken) script.WebhookToken = newToken();
    }

    // Every edit goes through here: it refreshes the counts, the picker, and the Save Bar's unsaved line.
    function edited() {
        renderCounts();
        renderSelector();
        if (!tracker) return;
        var dirty = tracker.check();
        if (saveBar && saveBar.getState() !== 'saving') saveBar.setDirty(dirty);
    }

    // numberControl
    // Builds a Base Slider, or a Duration Field in seconds, for one number.
    //
    // Inputs
    // spec [object]: { slider } with the Slider's min, max, suffix, and label, or { units, min, max } for a duration.
    // onChange [function]: Called with the new value, in seconds for a duration.
    //
    // Output
    // [object]: The control handle.
    function numberControl(spec, onChange) {
        if (spec.slider) return createSliderField(Object.assign({ onChange: onChange }, spec.slider));
        return createDurationField({
            units: spec.minutes ? ['h', 'm'] : spec.units,
            min: spec.minutes ? spec.min * 60 : spec.min,
            max: spec.minutes ? spec.max * 60 : spec.max,
            required: true,
            onChange: onChange
        });
    }

    // createNumbers
    // Mounts the script's number controls. Each writes its script property as it changes.
    function createNumbers() {
        SCRIPT_NUMBERS.forEach(function (spec) {
            var control = numberControl(spec, function (value) {
                if (suppressWidgetEvents || !current()) return;
                var number = value === null || value === undefined ? 0 : value;
                current()[spec.key] = spec.minutes ? Math.round(number / 60) : number;
                edited();
            });
            numberControls[spec.key] = control;
            el(spec.mount).appendChild(control.element);
        });
    }

    // showNumbers
    // Shows the script's numbers in their controls, and each trigger's panel while its trigger is checked.
    function showNumbers() {
        var script = current();
        suppressWidgetEvents = true;
        SCRIPT_NUMBERS.forEach(function (spec) {
            var value = orDefault(script[spec.key], spec.fallback);
            numberControls[spec.key].setValue(spec.minutes ? value * 60 : value);
            if (spec.block) Shared.setVisible(spec.block, has(script, spec.trigger));
        });
        suppressWidgetEvents = false;
    }

    // The shared chip select adds on Enter only once an option is highlighted with the arrow keys. Here Enter
    // also picks the suggestion whose label matches the typed text, so a library or task typed by name stores
    // its ID, and with firstMatch the top suggestion when nothing matches exactly.
    function enterPicksSuggestion(chips, firstMatch) {
        chips.element.addEventListener('keydown', function (e) {
            if (e.key !== 'Enter' || e.target.tagName !== 'INPUT') return;
            var typed = e.target.value.trim().toLowerCase();
            var options = Array.from(chips.element.querySelectorAll('.base-combo-dropdown:not([hidden]) .base-combo-option'));
            if (!typed || !options.length || options.some(function (o) { return o.classList.contains('active'); })) return;
            var pick = options.find(function (o) { return o.textContent.trim().toLowerCase() === typed; }) || (firstMatch ? options[0] : null);
            if (!pick) return;
            e.preventDefault();
            e.stopPropagation();
            pick.dispatchEvent(new MouseEvent('mousedown', { bubbles: true, cancelable: true }));
        }, true);
        return chips;
    }

    // ---- Counts and selector ----

    function renderCounts() {
        var enabled = scripts.filter(function (s) { return s.Enabled; }).length;
        Shared.renderCards('scriptCounts', [
            { count: scripts.length, label: 'Scripts', color: 'blue' },
            { count: enabled, label: 'Enabled', color: 'green' },
            { count: scripts.length - enabled, label: 'Disabled', color: 'gray' }
        ]);
    }

    function renderSelector() {
        var select = el('selectScript');
        select.innerHTML = scripts.map(function (s) {
            return '<option value="' + esc(s.Id) + '">' + esc(s.Name || 'Untitled') + (s.Enabled ? '' : ' (disabled)') + '</option>';
        }).join('');
        if (selectedId) select.value = selectedId;
        el('emptyState').innerHTML = scripts.length ? '' : Shared.emptySection('No scripts yet. Select New to add one, or Import a file.');
        Shared.setVisible('emptyState', scripts.length === 0);
        Shared.setVisible('scriptEditor', scripts.length > 0);
        el('btnDeleteScript').disabled = scripts.length === 0;
        el('btnDuplicateScript').disabled = scripts.length === 0;
    }

    function selectScript(id) {
        selectedId = id;
        renderEditor();
    }

    // ---- Editor ----

    function renderEditor() {
        var script = current();
        if (!script) return;
        suppressWidgetEvents = true;
        el('scriptName').value = script.Name || '';
        el('scriptDescription').value = script.Description || '';
        el('scriptEnabled').checked = !!script.Enabled;
        triggerList.setValue(script.Triggers || []);
        itemKindSelect.setValue(kindItems(script.ItemKinds));
        userSelect.setValue((script.UserIds || []).map(compactId));
        matchChoice.set(script.Match || 'All');
        suppressWidgetEvents = false;
        el('forEachMode').value = script.ForEach || 'None';
        el('onFailureScript').innerHTML = '<option value="">Nothing</option>' + scripts.filter(function (s) { return s.Id !== script.Id; }).map(function (s) {
            return '<option value="' + esc(s.Id) + '">' + esc(s.Name || 'Untitled') + '</option>';
        }).join('');
        el('onFailureScript').value = compactId(script.OnFailureScriptId);
        renderTriggerOptions();
        renderConditions();
        renderOperations();
        renderSecrets();
        renderVariables();
        renderTestTriggers();
        renderWarnings();
    }

    function onTriggersChanged() {
        ensureWebhookToken(current());
        renderTriggerOptions();
        renderConditions();
        renderOperationCards();
        renderVariables();
        renderTestTriggers();
        renderWarnings();
        edited();
    }

    function kindItems(kinds) {
        return (kinds || []).map(function (k) {
            var match = catalog.ItemKinds.find(function (x) { return x.Value === k; });
            return { value: k, label: match ? match.Label : k };
        });
    }

    function renderWarnings() {
        var script = current();
        var triggers = selectedTriggers(script);

        var library = triggers.filter(function (t) { return t.Category === 'Library' && t.HasItem; });
        var kindWarn = library.length > 0 && (script.ItemKinds || []).length === 0;
        el('itemKindWarn').textContent = kindWarn
            ? 'With no item kinds, ' + library.map(quoted).join(' and ') +
              ' runs for every kind a scan touches, including people, genres, studios, and folders.'
            : '';
        Shared.setVisible('itemKindWarn', kindWarn);

        var noUser = triggers.filter(function (t) { return !carriesUser(script, t); });
        var userWarn = (script.UserIds || []).length > 0 && noUser.length > 0;
        el('userWarn').textContent = userWarn
            ? 'Ignored for ' + noUser.map(quoted).join(', ') + ', which ' +
              (noUser.length === 1 ? 'has' : 'have') + ' no user. The script runs on ' + (noUser.length === 1 ? 'that event' : 'those events') + ' whatever these users do.'
            : '';
        Shared.setVisible('userWarn', userWarn);

        var mode = script.ForEach || 'None';
        Shared.setVisible('forEachLimitRow', mode !== 'None');
        var eachWarn = '';
        if (mode === 'Items' && (script.ItemKinds || []).length === 0) {
            eachWarn = 'Choose at least one item kind above, such as Movie, so the script knows which items to go through.';
        } else if (mode !== 'None' && triggers.length && !triggers.some(function (t) { return expands(script, t); })) {
            eachWarn = 'Every selected trigger already has ' + (mode === 'Items' ? 'an item' : 'a user') + ', so the script runs once per event. Running for each applies to triggers such as On a schedule, Webhook received, and Run now.';
        }
        el('forEachWarn').textContent = eachWarn;
        Shared.setVisible('forEachWarn', !!eachWarn);
    }

    // ---- Trigger options ----

    function renderTriggerOptions() {
        var script = current();
        showNumbers();
        Shared.setVisible('scheduleOptions', has(script, 'Schedule'));
        Shared.setVisible('webhookOptions', has(script, 'Webhook'));
        if (has(script, 'Webhook')) renderWebhook();
        el('scheduleStart').textContent = has(script, 'Schedule') ? scheduleStart(script) : '';
        Shared.setVisible('scheduleStart', has(script, 'Schedule'));
    }

    // The schedule a script's task starts with, until it is changed under Scheduled Tasks.
    function scheduleStart(script) {
        var lead = 'Until you change it there, the task starts out running ';
        if (script.ScheduleMode === 'Interval') return lead + 'every ' + (script.ScheduleIntervalMinutes || 60) + ' minutes.';
        if (script.ScheduleMode === 'Monthly') return 'The task starts with no schedule of its own. Add one under Scheduled Tasks.';
        var days = script.ScheduleDays || [];
        return lead + (days.length ? 'on ' + days.join(', ') : 'every day') + ' at ' + (script.ScheduleTime || '03:00') + '.';
    }

    // Shows what the script holds, so a cleared field reads as empty rather than as a default it does not have.
    function orDefault(value, fallback) {
        return value === undefined || value === null ? fallback : value;
    }

    function renderWebhook() {
        var path = 'Scripts/Webhook/' + (current().WebhookToken || '');
        var url = window.ApiClient && window.ApiClient.getUrl ? window.ApiClient.getUrl(path) : '/' + path;
        webhookField.setValue(url);
        el('webhookExample').textContent =
            'curl -X POST "' + url + '" \\\n' +
            '  -H \'Authorization: MediaBrowser Token="YOUR_API_KEY"\' \\\n' +
            '  -H "Content-Type: application/json" \\\n' +
            '  -d \'{"title": "Example"}\'';
    }

    // ---- Conditions ----

    function valueOptions(field) {
        if (field === 'User') return catalog.Users.map(function (u) { return { value: u.Id, label: u.Name }; });
        if (field === 'Library') return catalog.Libraries.map(function (l) { return { value: l.Id, label: l.Name }; });
        if (field === 'TaskKey') return catalog.Tasks.map(function (t) { return { value: t.Key, label: t.Name }; });
        if (field === 'CollectionName') return catalog.Collections.map(function (c) { return { value: c.Name, label: c.Name }; });
        if (FIXED_VALUES[field]) return FIXED_VALUES[field].map(function (v) { return { value: v, label: v }; });
        return [];
    }

    function labelled(field, values) {
        var options = valueOptions(field);
        return (values || []).map(function (v) {
            var match = options.find(function (o) { return String(o.value).toLowerCase() === String(v).toLowerCase(); });
            return match || { value: v, label: v };
        });
    }

    function conditionWarning(script, condition) {
        var triggers = selectedTriggers(script);
        if (!triggers.length) return '';
        var info = fieldInfo(condition.Field);
        if (!info) return '';
        var missing = triggers.filter(function (t) { return !supplies(script, t, info); });
        if (!missing.length) return '';
        if (missing.length === triggers.length) return 'None of the selected triggers supply this value, so this condition never matches.';
        var names = missing.map(quoted).join(', ');
        return script.Match === 'Any'
            ? 'Never matches for ' + names + '.'
            : 'Never matches for ' + names + ', so with "all conditions" the script will not run on those.';
    }

    // conditionItems
    // Turns a script's conditions into Repeater rows. The value cell carries its field, so it knows which choices to offer.
    //
    // Inputs
    // conditions [Array]: The script's conditions.
    //
    // Output
    // [Array]: One row per condition.
    function conditionItems(conditions) {
        return (conditions || []).map(function (c) {
            return { Field: c.Field, Operator: c.Operator, Values: { field: c.Field, values: (c.Values || []).slice() } };
        });
    }

    // conditionValueField
    // The Repeater's value cell, a Base Chip Select offering the choices of the row's field.
    //
    // Inputs
    // value [object]: The cell value, as { field, values }.
    // change [function]: Tells the Repeater the cell changed.
    //
    // Output
    // [object]: The cell's element, input, getValue, setValue, and destroy.
    function conditionValueField(value, change) {
        var state = value || { field: null, values: [] };
        var chips = enterPicksSuggestion(createChipSelect({
            options: valueOptions(state.field),
            value: labelled(state.field, state.values),
            placeholder: 'Pick or type a value',
            allowCreate: true,
            onChange: function (values) {
                state.values = values;
                change();
            }
        }), false);
        return {
            element: chips.element,
            input: chips.element.querySelector('input'),
            getValue: function () { return { field: state.field, values: chips.getValue() }; },
            setValue: function (next) {
                state = next || { field: null, values: [] };
                chips.setOptions(valueOptions(state.field));
                chips.setValue(labelled(state.field, state.values));
            },
            destroy: function () { chips.destroy(); }
        };
    }

    // onConditionsChanged
    // Reads the Repeater back into the script, and clears a row's values when its field changes.
    //
    // Inputs
    // items [Array]: The Repeater's rows.
    function onConditionsChanged(items) {
        if (conditionsQuiet) return;
        var reset = false;
        items.forEach(function (item) {
            if (item.Values && item.Values.field === item.Field) return;
            item.Values = { field: item.Field, values: [] };
            if (NUMERIC_FIELDS[item.Field] && item.Operator === 'Is') item.Operator = 'AtLeast';
            if (!NUMERIC_FIELDS[item.Field] && (item.Operator === 'AtLeast' || item.Operator === 'AtMost')) item.Operator = 'Is';
            reset = true;
        });
        current().Conditions = items.map(function (item) {
            return { Field: item.Field, Operator: item.Operator, Values: item.Values.values };
        });
        renderConditionWarnings();
        edited();
        // A new field offers other choices, so its row is drawn again.
        if (reset) setTimeout(renderConditions, 0);
    }

    // renderConditionWarnings
    // Lists the conditions that a selected trigger never supplies a value for.
    function renderConditionWarnings() {
        var script = current();
        var host = el('conditionWarnings');
        host.innerHTML = '';
        script.Conditions.forEach(function (condition, index) {
            var warning = conditionWarning(script, condition);
            if (!warning) return;
            var note = document.createElement('div');
            note.className = 'base-warn';
            note.textContent = 'Condition ' + (index + 1) + '. ' + warning;
            host.appendChild(note);
        });
    }

    // renderConditions
    // Draws the script's conditions with Base's Repeater.
    function renderConditions() {
        var script = current();
        if (!conditionRepeater) {
            conditionRepeater = createRepeater({
                fields: [
                    {
                        key: 'Field', label: 'Field', type: 'select', width: '1fr',
                        options: FIELD_GROUPS.reduce(function (all, group) {
                            return all.concat(group.fields.map(function (f) { return { value: f.key, label: f.label }; }));
                        }, [])
                    },
                    {
                        key: 'Operator', label: 'Operator', type: 'select', width: '1fr',
                        options: Object.keys(OPERATOR_LABELS).map(function (o) { return { value: o, label: OPERATOR_LABELS[o] }; })
                    },
                    { key: 'Values', label: 'Value', type: 'custom', width: '2fr', render: conditionValueField }
                ],
                addText: 'Add condition',
                rowName: 'Condition',
                emptyText: 'No conditions. The script runs on every trigger that passes its filters.',
                onChange: onConditionsChanged
            });
            el('conditionList').appendChild(conditionRepeater.element);
        }
        conditionsQuiet = true;
        conditionRepeater.setValue(conditionItems(script.Conditions));
        conditionsQuiet = false;
        renderConditionWarnings();
    }

    // ---- Steps ----

    function operationSummary(op) {
        switch (op.Type) {
            case 'RunCommand': return op.Command || 'No command yet';
            case 'HttpRequest': return (op.Method || 'POST') + ' ' + (op.Url || 'no URL yet');
            case 'SendMessage': return op.Text || 'No message yet';
            case 'StopPlayback': return op.Text ? 'Message, then stop' : 'Stop the event\'s session';
            case 'AddTag': case 'RemoveTag': return op.Tag || 'No tag yet';
            case 'AddToCollection': case 'RemoveFromCollection': {
                var c = catalog.Collections.find(function (x) { return compactId(x.Id) === compactId(op.CollectionId); });
                return c ? c.Name : 'No collection chosen';
            }
            case 'RunScript': {
                var s = scripts.find(function (x) { return compactId(x.Id) === compactId(op.ScriptId); });
                return s ? s.Name : 'No script chosen';
            }
            case 'RunScheduledTask': {
                var t = catalog.Tasks.find(function (x) { return x.Key === op.TaskKey; });
                return t ? t.Name : 'No task chosen';
            }
            case 'AppendToFile': return op.FilePath || 'No file yet';
            case 'Wait': return (op.WaitSeconds || 0) + ' seconds';
            case 'SetFavorite': return op.Favorite ? 'Add the favorite' : 'Remove the favorite';
            case 'SetUserDisabled': return op.Disable ? 'Disable the user' : 'Enable the user';
            case 'WriteActivityLog': return op.Title || 'Entry named after the script';
            case 'RefreshMetadata': return op.FullRefresh ? 'Full refresh' : 'Standard refresh';
            case 'StopScript': return 'Skip the steps after this one';
            default: return '';
        }
    }

    function operationWarning(script, op) {
        var needs = OPERATION_TYPES[op.Type] && OPERATION_TYPES[op.Type].needs;
        if (op.Type === 'SendMessage') needs = op.Target === 'EventSession' ? 'session' : (op.Target === 'UserSessions' ? 'user' : null);
        var triggers = selectedTriggers(script);
        if (!needs || !triggers.length) return '';
        var lacks = function (t) {
            if (needs === 'session') return !t.HasSession;
            if (needs === 'item') return !carriesItem(script, t);
            if (needs === 'user') return !carriesUser(script, t);
            return !carriesItem(script, t) || !carriesUser(script, t);
        };
        var missing = triggers.filter(lacks);
        if (!missing.length) return '';
        var what = { session: 'session', item: 'item', user: 'user', useritem: 'user and item' }[needs];
        return 'Skipped for ' + missing.map(quoted).join(', ') + ', which ' +
            (missing.length === 1 ? 'has' : 'have') + ' no ' + what + '.';
    }

    function newOperation(type) {
        return {
            Type: type, ContinueOnError: false,
            Command: '', Arguments: '', RunThroughShell: false, WorkingDirectory: '', TimeoutSeconds: 0,
            Method: 'POST', Url: '', Headers: '', Body: '', ContentType: 'application/json', Retries: 0,
            Title: '', Text: '', Target: 'EventSession', DisplaySeconds: type === 'SendMessage' ? 10 : 0,
            TaskKey: '', FullRefresh: false, ReplaceAll: false, WaitSeconds: type === 'Wait' ? 5 : 0,
            Tag: '', CollectionId: '', Favorite: true, Disable: true, ScriptId: '', FilePath: '',
            OutputName: '', OutputJsonPath: '', OnlyIfVariable: '', OnlyIfOperator: 'Is', OnlyIfValue: ''
        };
    }

    function renderOperations() {
        var script = current();
        operationList.setItems(script.Operations);
        operationList.setSelected(-1);
        renderOperationCards();
    }

    function renderOperationCards() {
        var script = current();
        el('operationEmpty').innerHTML = script.Operations.length ? '' : Shared.emptySection('No steps yet. Add one below.', { inline: true });
        Shared.setVisible('operationEmpty', script.Operations.length === 0);
        operationList.render();
    }

    // A step's text field, or with opts.number a mount for the Base control STEP_NUMBERS names for its key.
    function input(op, key, label, opts) {
        opts = opts || {};
        var id = 'opField' + key;
        var value = op[key] === null || op[key] === undefined ? '' : op[key];
        var field = opts.number
            ? '<div data-number-mount="' + key + '"></div>'
            : opts.textarea
                ? '<textarea is="emby-textarea" class="emby-textarea base-mono" id="' + id + '" data-key="' + key + '" rows="' + (opts.rows || 3) + '"' +
                    (opts.placeholder ? ' placeholder="' + esc(opts.placeholder) + '"' : '') + '>' + esc(value) + '</textarea>'
                : '<input is="emby-input" type="text" id="' + id + '" data-key="' + key + '" value="' + esc(value) + '"' +
                    (opts.placeholder ? ' placeholder="' + esc(opts.placeholder) + '"' : '') + ' />';
        return '<div class="inputContainer"><label' + (opts.number ? '' : ' for="' + id + '"') + '>' + esc(label) + '</label>' + field +
            (opts.help ? '<div class="fieldDescription">' + opts.help + '</div>' : '') + '</div>';
    }

    // mountStepNumbers
    // Puts a Base control in each number mount of the open step's editor, writing the step as it changes.
    //
    // Inputs
    // op [object]: The open step.
    function mountStepNumbers(op) {
        el('operationEditor').querySelectorAll('[data-number-mount]').forEach(function (mount) {
            var key = mount.getAttribute('data-number-mount');
            var control = numberControl(STEP_NUMBERS[key], function (value) {
                op[key] = value === null || value === undefined ? 0 : value;
                edited();
            });
            control.setValue(op[key] || 0);
            mount.appendChild(control.element);
            editorControls.push(control);
        });
    }

    function checkbox(op, key, label, help) {
        return '<div class="checkboxContainer' + (help ? ' checkboxContainer-withDescription' : '') + '"><label class="emby-checkbox-label">' +
            '<input type="checkbox" is="emby-checkbox" data-key="' + key + '"' + (op[key] ? ' checked' : '') + ' />' +
            '<span class="checkboxLabel">' + esc(label) + '</span></label>' +
            (help ? '<div class="fieldDescription checkboxFieldDescription">' + help + '</div>' : '') + '</div>';
    }

    function select(op, key, label, options, opts) {
        opts = opts || {};
        return '<div class="selectContainer"><label>' + esc(label) + '</label>' +
            '<select is="emby-select" data-key="' + key + '"' + (opts.bool ? ' data-bool="1"' : '') + '>' +
            options.map(function (o) {
                return '<option value="' + esc(o.value) + '"' + (String(op[key]) === String(o.value) ? ' selected' : '') + '>' + esc(o.label) + '</option>';
            }).join('') + '</select>' + (opts.help ? '<div class="fieldDescription">' + opts.help + '</div>' : '') + '</div>';
    }

    function note(text) {
        return '<div class="fieldDescription">' + text + '</div>';
    }

    function operationFields(op) {
        switch (op.Type) {
            case 'RunCommand':
                return '<div class="base-choice-group" role="radiogroup" aria-label="How the command runs">' +
                        '<label class="base-choice"><input type="radio" name="commandMode" value="direct"' + (op.RunThroughShell ? '' : ' checked') + ' />' +
                            '<span class="base-choice-name">Run directly</span><span class="base-choice-text">Starts one program with its arguments. Event values fill arguments safely.</span></label>' +
                        '<label class="base-choice"><input type="radio" name="commandMode" value="shell"' + (op.RunThroughShell ? ' checked' : '') + ' />' +
                            '<span class="base-choice-name">Through the shell</span><span class="base-choice-text">One line for /bin/sh, for pipes and redirects. Event values come from the environment.</span></label>' +
                    '</div>' +
                    input(op, 'Command', op.RunThroughShell ? 'Shell line' : 'Command', {
                        placeholder: op.RunThroughShell ? '/scripts/notify.sh "$JF_ITEM_NAME" >> /tmp/log.txt' : '/scripts/notify.sh',
                        help: op.RunThroughShell
                            ? 'Placeholders are not filled here, so event values cannot inject shell syntax. Read them from the environment, such as "$JF_ITEM_NAME". On Windows the line runs through cmd /c, which expands %JF_...% before parsing it, so there read event values inside a script file instead.'
                            : 'A path to a script or program, or a name on the server\'s PATH such as curl. The server must be able to reach the path. In Docker, mount it into the container and use the path inside it.'
                    }) +
                    (op.RunThroughShell ? '' : '<button is="emby-button" type="button" class="raised base-button secondary small" id="btnCheckPath"><span>Check path</span></button>') +
                    (op.RunThroughShell ? '' : input(op, 'Arguments', 'Arguments, one per line', {
                        textarea: true,
                        placeholder: '--title\n{{ITEM_NAME}}',
                        help: 'Each line is passed as one argument, with placeholders filled. Spaces and quotes in a value never split it.'
                    })) +
                    input(op, 'WorkingDirectory', 'Working folder', { placeholder: 'The script\'s own folder', help: 'Optional. Defaults to the folder holding the script.' }) +
                    input(op, 'TimeoutSeconds', 'Timeout', { number: true, help: 'The command is stopped after this long. 0 uses the default from Settings.' });
            case 'HttpRequest':
                return select(op, '__preset', 'Start from a preset', [{ value: '', label: 'Keep the current request' }].concat(Object.keys(HTTP_PRESETS).map(function (k) {
                        return { value: k, label: HTTP_PRESETS[k].label };
                    })), { help: 'Fills the fields below for a common service. Replace the URL and put tokens in the script\'s secrets.' }) +
                    select(op, 'Method', 'Method', HTTP_METHODS.map(function (m) { return { value: m, label: m }; })) +
                    input(op, 'Url', 'URL', { placeholder: 'https://example.com/hook?item={{ITEM_NAME:url}}' }) +
                    input(op, 'Headers', 'Headers, one per line', { textarea: true, rows: 2, placeholder: 'Authorization: Bearer {{SECRET_TOKEN}}' }) +
                    input(op, 'ContentType', 'Content type', { placeholder: 'application/json' }) +
                    input(op, 'Body', 'Body', { textarea: true, rows: 5, placeholder: '{ "text": "{{USER_NAME:json}} started {{ITEM_NAME:json}}" }' }) +
                    input(op, 'Retries', 'Retries', { number: true, help: 'Tries again after no answer, a 5xx, or a 429, waiting longer each time. Up to 5.' });
            case 'SendMessage':
                return select(op, 'Target', 'Send to', MESSAGE_TARGETS) +
                    input(op, 'Title', 'Title') +
                    input(op, 'Text', 'Message', { textarea: true, rows: 2 }) +
                    input(op, 'DisplaySeconds', 'Show for', { number: true, help: '0 leaves it to the client.' });
            case 'StopPlayback':
                return input(op, 'Text', 'Message shown before stopping', { textarea: true, rows: 2, help: 'Optional.' }) +
                    input(op, 'Title', 'Message title', { help: 'Optional.' }) +
                    input(op, 'DisplaySeconds', 'Show for', { number: true });
            case 'PausePlayback':
                return note('Pauses playback on the session that raised the event.');
            case 'MarkPlayed':
            case 'MarkUnplayed':
                return note('Acts on the event\'s item for the event\'s user.');
            case 'SetFavorite':
                return select(op, 'Favorite', 'Favorite', [{ value: 'true', label: 'Add as a favorite' }, { value: 'false', label: 'Remove from favorites' }], { bool: true, help: 'Acts on the event\'s item for the event\'s user.' });
            case 'AddTag':
            case 'RemoveTag':
                return input(op, 'Tag', 'Tag', { placeholder: 'new', help: 'Placeholders are filled, such as {{LOCAL_DATE}}.' });
            case 'AddToCollection':
            case 'RemoveFromCollection':
                return select(op, 'CollectionId', 'Collection', [{ value: '', label: catalog.Collections.length ? 'Choose a collection' : 'No collections on this server' }].concat(catalog.Collections.map(function (c) {
                    return { value: c.Id, label: c.Name };
                })));
            case 'RefreshMetadata':
                return checkbox(op, 'FullRefresh', 'Full refresh', 'Fetch everything again rather than only what is missing.') +
                    checkbox(op, 'ReplaceAll', 'Replace existing metadata and images');
            case 'SetUserDisabled':
                return select(op, 'Disable', 'Action', [{ value: 'true', label: 'Disable the user' }, { value: 'false', label: 'Enable the user' }], { bool: true, help: 'Acts on the event\'s user. Administrators are never disabled.' });
            case 'SignOutUser':
                return note('Signs the event\'s user out of every device. Administrators are never signed out.');
            case 'ScanLibraries':
                return note('Queues a scan of every library, the same as the Scan All Libraries button.');
            case 'RunScript':
                return select(op, 'ScriptId', 'Script', [{ value: '', label: 'Choose a script' }].concat(scripts.filter(function (s) { return s.Id !== selectedId; }).map(function (s) {
                    return { value: s.Id, label: s.Name };
                })), { help: 'Runs that script\'s steps with this event, skipping its triggers and filters. Scripts may run each other up to 3 levels deep.' });
            case 'RunScheduledTask':
                return select(op, 'TaskKey', 'Scheduled task', [{ value: '', label: 'Choose a task' }].concat(catalog.Tasks.map(function (t) {
                    return { value: t.Key, label: t.Name };
                })));
            case 'WriteActivityLog':
                return input(op, 'Title', 'Entry name', { placeholder: 'The script\'s name' }) +
                    input(op, 'Text', 'Details', { textarea: true, rows: 2 });
            case 'AppendToFile':
                return input(op, 'FilePath', 'File', { placeholder: '/config/log/scripts.log', help: 'A full path whose folder exists. The file is created if missing.' }) +
                    input(op, 'Text', 'Line to append', { textarea: true, rows: 2, placeholder: '{{LOCAL_DATE}} {{LOCAL_TIME}} {{EVENT}} {{ITEM_NAME}}' });
            case 'Wait':
                return input(op, 'WaitSeconds', 'Wait for', { number: true });
            case 'StopScript':
                return note('Ends the run here: the steps after this one are skipped, and the run still counts as succeeded. Give it a condition below so it only stops when you want it to.');
            default:
                return '';
        }
    }

    // A Base Field for a Field Row: a label over a text box or a select.
    function rowField(id, label, control) {
        return '<div class="base-field"><label for="' + id + '">' + esc(label) + '</label>' + control + '</div>';
    }

    // What every step has beyond its own fields: a condition, and for a command or HTTP request a saved output,
    // each a Base Field Row under a Subsection Title.
    function stepExtras(op) {
        var condition = '<h3 class="base-subsection-title">Only run this step if</h3><div class="base-field-row">' +
            rowField('opFieldOnlyIfVariable', 'Variable', '<input is="emby-input" type="text" id="opFieldOnlyIfVariable" class="base-mono" data-key="OnlyIfVariable" data-upper="1" list="scriptsVariableNames" value="' + esc(op.OnlyIfVariable) + '" placeholder="Always, or a variable" />') +
            rowField('opFieldOnlyIfOperator', 'Operator', '<select is="emby-select" id="opFieldOnlyIfOperator" data-key="OnlyIfOperator">' + Object.keys(OPERATOR_LABELS).map(function (o) {
                return '<option value="' + o + '"' + (o === (op.OnlyIfOperator || 'Is') ? ' selected' : '') + '>' + esc(OPERATOR_LABELS[o]) + '</option>';
            }).join('') + '</select>') +
            rowField('opFieldOnlyIfValue', 'Value', '<input is="emby-input" type="text" id="opFieldOnlyIfValue" data-key="OnlyIfValue" value="' + esc(op.OnlyIfValue) + '" placeholder="Value, or empty for none" />') +
            '</div><div class="fieldDescription">Tests any variable, such as ITEM_KIND, a webhook field, or one an earlier step saved. A step whose condition does not match is skipped, and the run carries on.</div>';
        var output = HAS_OUTPUT[op.Type]
            ? '<h3 class="base-subsection-title">Save the ' + (op.Type === 'HttpRequest' ? 'response' : 'output') + ' for later steps</h3><div class="base-field-row">' +
                rowField('opFieldOutputName', 'Save as', '<input is="emby-input" type="text" id="opFieldOutputName" class="base-mono" data-key="OutputName" data-upper="1" value="' + esc(op.OutputName) + '" placeholder="Name, such as RESULT" />') +
                rowField('opFieldOutputJsonPath', 'JSON field', '<input is="emby-input" type="text" id="opFieldOutputJsonPath" data-key="OutputJsonPath" value="' + esc(op.OutputJsonPath) + '" placeholder="Everything, or a JSON field: data.items[0].name" />') +
                '</div><div class="fieldDescription">Later steps use it as <code>{{NAME}}</code>, and <code>{{NAME_STATUS}}</code> holds the ' +
                (op.Type === 'HttpRequest' ? 'HTTP status' : 'exit code') + '.</div>'
            : '';
        return condition + output;
    }

    function renderOperationEditor(index) {
        var script = current();
        var op = script.Operations[index];
        var editor = el('operationEditor');
        editorControls.forEach(function (control) { control.destroy(); });
        editorControls = [];
        if (!op) { editor.innerHTML = ''; return; }
        var warning = operationWarning(script, op);
        editor.innerHTML =
            (warning ? '<div class="base-warn">' + esc(warning) + '</div>' : '') +
            operationFields(op) +
            stepExtras(op) +
            checkbox(op, 'ContinueOnError', 'Continue on error', 'Run the following steps even if this one fails.') +
            '<button is="emby-button" type="button" class="raised base-button destructive small" id="btnRemoveOperation"><span>Remove step</span></button>';
        mountStepNumbers(op);
        var mode = editor.querySelector('.base-choice-group');
        if (mode) {
            createChoiceGroup(mode, {
                onChange: function (value) {
                    op.RunThroughShell = value === 'shell';
                    renderOperationEditor(index);
                    renderOperationCards();
                    edited();
                }
            });
        }
    }

    function selectedOperation() {
        var index = operationList.getSelected();
        return index >= 0 ? current().Operations[index] : null;
    }

    function readOperationField(target) {
        var op = selectedOperation();
        if (!op) return null;
        var key = target.getAttribute('data-key');
        if (key === '__preset') {
            if (target.value && HTTP_PRESETS[target.value]) {
                Object.assign(op, HTTP_PRESETS[target.value].fields);
                renderOperationEditor(operationList.getSelected());
            }
            return key;
        }

        if (target.getAttribute('data-upper')) {
            var caret = target.selectionStart;
            target.value = target.value.toUpperCase().replace(/^\{+|\}+$|^\$/g, '').replace(/^JF_/, '').replace(/[^A-Z0-9_]/g, '_');
            try { target.selectionStart = target.selectionEnd = caret; } catch (e) { /* not a text field */ }
        }

        if (target.type === 'checkbox') op[key] = target.checked;
        else if (target.getAttribute('data-bool')) op[key] = target.value === 'true';
        else op[key] = target.value;
        // A changed command has not been checked yet.
        if (key === 'Command') setFieldState(target, '', '');
        return key;
    }

    // checkPath
    // Asks the server whether the command's path runs, and shows the answer under the command as a Base Field Message.
    function checkPath() {
        var op = selectedOperation();
        var command = el('opFieldCommand');
        if (!op || !command) return;
        setFieldState(command, 'info', 'Checking…');
        Shared.apiRequest('Scripts/CheckPath?path=' + encodeURIComponent(op.Command || ''), 'GET').then(function (check) {
            var ok = check && check.Exists && !check.IsDirectory && check.IsExecutable;
            setFieldState(command, ok ? 'success' : 'error', check ? check.Message : 'No answer from the server.');
        }).catch(function () {
            setFieldState(command, 'error', 'Could not check the path.');
        });
    }

    // ---- Secrets ----

    // secretItems
    // Turns a script's secrets into Repeater rows. A stored value stays on the server, and its row holds the kept
    // sentinel. The hidden Previous field carries the name the secret was loaded with, so a renamed secret keeps it.
    //
    // Inputs
    // secrets [Array]: The script's secrets.
    //
    // Output
    // [Array]: One row per secret.
    function secretItems(secrets) {
        return (secrets || []).map(function (secret) {
            return { Name: secret.Name, Value: secret.Value, Previous: secret.PreviousName || '' };
        });
    }

    // previousField
    // The Repeater's Previous cell: no control, only the name the secret was loaded with.
    //
    // Inputs
    // value [string]: The loaded name, or empty for a new secret.
    //
    // Output
    // [object]: The cell's empty element, getValue, and setValue.
    function previousField(value) {
        var previous = value || '';
        return {
            element: document.createElement('span'),
            getValue: function () { return previous; },
            setValue: function (next) { previous = next || ''; }
        };
    }

    // secretNameProblem
    // Checks a secret's name: capital letters, digits, and underscores, and no other secret of the script with it.
    function secretNameProblem(value, item, index) {
        if (!value) return '';
        if (!/^[A-Z0-9_]{1,40}$/.test(value)) return 'Use capital letters, digits, and underscores.';
        var rows = secretRepeater ? secretRepeater.getValue() : [];
        var same = rows.filter(function (row) { return row.Name === value; }).length;
        return same > 1 ? 'Another secret has this name.' : '';
    }

    // renderSecrets
    // Draws the script's secrets with Base's Repeater, each a name and a Secret field.
    function renderSecrets() {
        if (!secretRepeater) {
            secretRepeater = createRepeater({
                fields: [
                    { key: 'Name', label: 'Name', placeholder: 'NAME', width: 'minmax(9rem, 1fr)', required: true, validate: secretNameProblem },
                    { key: 'Value', label: 'Value', type: 'secret', placeholder: 'Value', width: 'minmax(11rem, 2fr)' },
                    { key: 'Previous', label: '', type: 'custom', width: '0px', render: previousField }
                ],
                addText: 'Add secret',
                rowName: 'Secret',
                emptyText: 'No secrets.',
                onChange: onSecretsChanged
            });
            // Names are capital letters, digits, and underscores, so typing fixes them before the Repeater reads them.
            secretRepeater.element.addEventListener('input', function (e) {
                var target = e.target;
                if (target.tagName !== 'INPUT' || target.type !== 'text') return;
                var fixed = target.value.toUpperCase().replace(/[^A-Z0-9_]/g, '_');
                if (fixed === target.value) return;
                var caret = target.selectionStart;
                target.value = fixed;
                try { target.selectionStart = target.selectionEnd = caret; } catch (err) { /* not a text field */ }
            }, true);
            el('secretMount').appendChild(secretRepeater.element);
        }
        secretRepeater.setValue(secretItems(current().Secrets));
    }

    // onSecretsChanged
    // Reads the Repeater back into the script.
    //
    // Inputs
    // items [Array]: The Repeater's rows.
    function onSecretsChanged(items) {
        current().Secrets = items.map(function (item) {
            var secret = { Name: item.Name || '', Value: item.Value || '' };
            if (item.Previous) secret.PreviousName = item.Previous;
            return secret;
        });
        renderVariables();
        edited();
    }

    // ---- Variables and test ----

    function renderVariables() {
        var script = current();
        var triggers = selectedTriggers(script);
        if (!triggers.length) triggers = catalog.Triggers;
        var seen = {};
        var rows = [];
        var add = function (v) {
            if (seen[v.Name]) return;
            seen[v.Name] = true;
            rows.push(v);
        };
        triggers.forEach(function (t) { t.Variables.forEach(add); });
        if (script.ForEach === 'Items' || script.ForEach === 'Users') (catalog.ForEachVariables[script.ForEach] || []).forEach(add);
        (script.Operations || []).forEach(function (op, index) {
            if (!HAS_OUTPUT[op.Type] || !op.OutputName) return;
            add({ Name: op.OutputName, Description: 'What step ' + (index + 1) + ' saved.' });
            add({ Name: op.OutputName + '_STATUS', Description: 'Step ' + (index + 1) + '\'s ' + (op.Type === 'HttpRequest' ? 'HTTP status.' : 'exit code.') });
        });
        if (scripts.some(function (x) { return compactId(x.OnFailureScriptId) === script.Id; })) (catalog.FailureVariables || []).forEach(add);
        (script.Secrets || []).forEach(function (secret) {
            if (secret.Name) add({ Name: 'SECRET_' + secret.Name, Description: 'The script\'s secret ' + secret.Name + '.' });
        });
        variableList.setItems(rows.map(function (v) {
            return { id: v.Name, title: '{{' + v.Name + '}}', secondary: v.Description };
        }));
        var names = view.querySelector('#scriptsVariableNames');
        if (names) names.innerHTML = rows.map(function (v) { return '<option value="' + esc(v.Name) + '"></option>'; }).join('');
    }

    function renderTestTriggers() {
        var script = current();
        var triggers = selectedTriggers(script);
        if (!triggers.length) triggers = catalog.Triggers;
        var selectEl = el('testTrigger');
        var previous = selectEl.value;
        selectEl.innerHTML = triggers.map(function (t) {
            return '<option value="' + esc(t.Key) + '">' + esc(t.Label) + '</option>';
        }).join('');
        if (triggers.some(function (t) { return t.Key === previous; })) selectEl.value = previous;
    }

    function runTest() {
        var script = current();
        var problem = validate(script);
        if (problem) {
            Shared.setStatus('scriptStatus', problem, true);
            return;
        }

        var button = el('btnTestScript');
        button.disabled = true;
        var trigger = triggerInfo(el('testTrigger').value);
        var body = document.createElement('div');
        body.innerHTML = '<div class="base-info">Running with sample values for ' + esc(trigger ? trigger.Label.toLowerCase() : 'the trigger') + '…</div>';
        openDialog({
            title: 'Test: ' + (script.Name || 'Untitled'),
            body: body,
            buttons: [{ text: 'Close', className: 'base-button secondary', action: function (close) { close(); } }]
        });
        Shared.apiRequest('Scripts/Test', 'POST', { Script: script, Trigger: el('testTrigger').value }).then(function (run) {
            body.innerHTML = '';
            body.appendChild(renderRunSummary(Shared, run));
            body.appendChild(renderRunSteps(Shared, run));
        }).catch(function () {
            body.innerHTML = '<div class="base-error">The test could not run. Check the steps and try again.</div>';
        }).finally(function () {
            button.disabled = false;
        });
    }

    function runNow() {
        var script = current();
        if (!script) return;
        if (tracker && tracker.isDirty()) {
            Shared.setStatus('scriptStatus', 'Save first: Run now runs the saved script.', true);
            return;
        }

        var button = el('btnRunScript');
        button.disabled = true;
        Shared.apiRequest('Scripts/Run/' + script.Id, 'POST').then(function () {
            Shared.setStatus('scriptStatus', 'Started. The result is on the History tab.', false, { timeout: 8000 });
        }).catch(function () {
            Shared.setStatus('scriptStatus', 'Could not start the script. Save it and try again.', true);
        }).finally(function () {
            button.disabled = false;
        });
    }

    // ---- Save ----

    function validate(script) {
        if (!script.Name || !script.Name.trim()) return 'Every script needs a name.';
        var label = '"' + script.Name + '": ';
        for (var c = 0; c < script.Conditions.length; c++) {
            var condition = script.Conditions[c];
            if (!(condition.Values || []).length) return label + 'the ' + (FIELD_LABELS[condition.Field] || condition.Field).toLowerCase() + ' condition needs a value.';
            if ((condition.Operator === 'AtLeast' || condition.Operator === 'AtMost') && condition.Values.some(function (v) { return isNaN(parseFloat(v)); })) {
                return label + 'the ' + (FIELD_LABELS[condition.Field] || condition.Field).toLowerCase() + ' condition needs numbers for ' + OPERATOR_LABELS[condition.Operator] + '.';
            }
        }

        var secretNames = {};
        for (var s = 0; s < (script.Secrets || []).length; s++) {
            var name = script.Secrets[s].Name;
            if (!/^[A-Z0-9_]{1,40}$/.test(name)) return label + 'secret names use capital letters, digits, and underscores.';
            if (secretNames[name]) return label + 'two secrets are named ' + name + '.';
            secretNames[name] = true;
        }

        var outside = function (value, min, max) { return !(value >= min && value <= max); };
        if (outside(script.CooldownSeconds || 0, 0, 86400)) return label + 'the cooldown must be between 0 and 86400 seconds.';
        if (script.Triggers.indexOf('PlaybackPausedFor') >= 0 && outside(script.PausedForMinutes, 1, 1440)) return label + 'paused for must be between 1 and 1440 minutes.';
        if (script.Triggers.indexOf('PlaybackPlayingFor') >= 0 && outside(script.PlayingForMinutes, 1, 1440)) return label + 'playing for must be between 1 and 1440 minutes.';
        if (script.Triggers.indexOf('PlaybackProgressReached') >= 0 && outside(script.ProgressPercentThreshold, 1, 99)) return label + 'the playback percentage must be between 1 and 99.';
        if (script.Triggers.indexOf('StreamCountReached') >= 0 && outside(script.StreamCountThreshold, 1, 100)) return label + 'the stream count must be between 1 and 100.';
        if (script.Triggers.indexOf('TranscodeCountReached') >= 0 && outside(script.TranscodeCountThreshold, 1, 100)) return label + 'the transcode count must be between 1 and 100.';
        if (script.ForEach === 'Items' && !(script.ItemKinds || []).length) return label + 'running for each library item needs at least one item kind.';
        if (script.ForEach !== 'None' && outside(script.ForEachLimit, 1, 500)) return label + 'the most runs per event must be between 1 and 500.';

        var saved = {};
        for (var i = 0; i < script.Operations.length; i++) {
            var op = script.Operations[i];
            var step = label + 'step ' + (i + 1) + ' ';
            if (op.OutputName && HAS_OUTPUT[op.Type]) {
                if (!/^[A-Z0-9_]{1,40}$/.test(op.OutputName)) return step + 'saves its output under a name of capital letters, digits, and underscores.';
                if (saved[op.OutputName]) return step + 'saves its output as ' + op.OutputName + ', which step ' + saved[op.OutputName] + ' already uses.';
                saved[op.OutputName] = i + 1;
            }
            if (op.OnlyIfVariable && !/^[A-Z0-9_]{1,80}$/.test(op.OnlyIfVariable)) return step + 'has a condition on something that is not a variable name.';
            if (op.OnlyIfVariable && (op.OnlyIfOperator === 'AtLeast' || op.OnlyIfOperator === 'AtMost') && !/\{\{/.test(op.OnlyIfValue || '') && isNaN(parseFloat(op.OnlyIfValue))) {
                return step + 'has a condition that needs a number.';
            }
            if (op.Type === 'RunCommand' && !(op.Command || '').trim()) return step + 'needs a command.';
            if (op.Type === 'HttpRequest' && !/^https?:\/\//i.test((op.Url || '').trim())) return step + 'needs a URL that starts with http:// or https://.';
            if (op.Type === 'SendMessage' && !(op.Text || '').trim()) return step + 'needs message text.';
            if (op.Type === 'RunScheduledTask' && !op.TaskKey) return step + 'needs a scheduled task.';
            if ((op.Type === 'AddTag' || op.Type === 'RemoveTag') && !(op.Tag || '').trim()) return step + 'needs a tag.';
            if ((op.Type === 'AddToCollection' || op.Type === 'RemoveFromCollection') && !op.CollectionId) return step + 'needs a collection.';
            if (op.Type === 'RunScript' && !op.ScriptId) return step + 'needs a script to run.';
            if (op.Type === 'AppendToFile' && !/^(\/|[A-Za-z]:\\)/.test((op.FilePath || '').trim())) return step + 'needs a full file path.';
            if (op.Type === 'Wait' && outside(op.WaitSeconds, 1, 3600)) return step + 'must wait between 1 and 3600 seconds.';
            if (op.Type === 'RunCommand' && outside(op.TimeoutSeconds || 0, 0, 3600)) return step + 'timeout must be between 0 and 3600 seconds.';
            if (op.Type === 'HttpRequest' && outside(op.Retries || 0, 0, 5)) return step + 'retries must be between 0 and 5.';
            if ((op.Type === 'SendMessage' || op.Type === 'StopPlayback') && outside(op.DisplaySeconds || 0, 0, 300)) return step + 'show-for time must be between 0 and 300 seconds.';
        }

        return '';
    }

    // failSave
    // Rejects a save with a reason the Save Bar shows once it has drawn its own failed line.
    //
    // Inputs
    // text [string]: The reason.
    //
    // Output
    // [Promise]: A rejected promise.
    function failSave(text) {
        setTimeout(function () { if (saveBar) saveBar.setState('failed', text); }, 0);
        return Promise.reject(new Error(text));
    }

    // save
    // The Save Bar's save: checks every script, saves them, and reloads them.
    //
    // Output
    // [Promise]: Resolves once saved, and rejects with the reason it did not save.
    function save() {
        for (var i = 0; i < scripts.length; i++) {
            var problem = validate(scripts[i]);
            if (problem) {
                if (scripts[i].Id !== selectedId) {
                    selectScript(scripts[i].Id);
                    renderSelector();
                }
                return failSave(problem);
            }
        }

        return Shared.saveConfigWith(function (config) {
            // Only a command or an HTTP request has an output to save.
            config.Scripts = scripts.map(function (script) {
                var copy = JSON.parse(JSON.stringify(script));
                copy.Operations.forEach(function (op) { if (!HAS_OUTPUT[op.Type]) { op.OutputName = ''; op.OutputJsonPath = ''; } });
                return copy;
            });
        }).then(function () {
            // Reload so secrets come back as stored and new IDs settle.
            return reloadScripts();
        }, function () {
            return failSave('The server refused the save. Check each script and try again.');
        });
    }

    // ---- New, duplicate, export, import ----

    function blankScript(name) {
        return {
            Id: compactId(generateGuid()), Name: name || 'New script', Description: '', Enabled: true, Triggers: [], ItemKinds: [], UserIds: [],
            Match: 'All', Conditions: [], Operations: [], CooldownSeconds: 0, Secrets: [],
            PausedForMinutes: 10, PlayingForMinutes: 60, ScheduleMode: 'Daily', ScheduleIntervalMinutes: 60,
            ScheduleTime: '03:00', ScheduleDays: [], ScheduleMonthDay: 1,
            ForEach: 'None', ForEachLimit: 50, OnFailureScriptId: '', WebhookToken: '',
            StreamCountThreshold: 3, TranscodeCountThreshold: 2, ProgressPercentThreshold: 90
        };
    }

    // Fills a script from a template or an imported file, keeping only what this version knows, so a file from
    // a newer version or a hand edit cannot leave the editor unable to render.
    function sanitize(source, base) {
        var s = Object.assign(base, {
            Name: String(source.Name || base.Name),
            Description: String(source.Description || ''),
            Triggers: (source.Triggers || base.Triggers).filter(triggerInfo),
            ItemKinds: (source.ItemKinds || []).filter(function (k) { return catalog.ItemKinds.some(function (x) { return x.Value === k; }); }),
            UserIds: (source.UserIds || []).map(compactId),
            Match: source.Match === 'Any' ? 'Any' : 'All',
            Conditions: (source.Conditions || []).filter(function (c) {
                return c && FIELD_LABELS[c.Field] && OPERATOR_LABELS[c.Operator];
            }).map(function (c) {
                return { Field: c.Field, Operator: c.Operator, Values: Array.isArray(c.Values) ? c.Values.map(String) : [] };
            }),
            Operations: (source.Operations || []).filter(function (op) { return op && OPERATION_TYPES[op.Type]; }).map(function (op) {
                var filled = Object.assign(newOperation(op.Type), op);
                ['OutputName', 'OutputJsonPath', 'OnlyIfVariable', 'OnlyIfValue'].forEach(function (key) { filled[key] = filled[key] === null || filled[key] === undefined ? '' : String(filled[key]); });
                if (!OPERATOR_LABELS[filled.OnlyIfOperator]) filled.OnlyIfOperator = 'Is';
                return filled;
            }),
            Secrets: (source.Secrets || []).filter(function (x) { return x && x.Name; }).map(function (x) {
                return { Name: String(x.Name).toUpperCase(), Value: '' };
            })
        });
        ['CooldownSeconds', 'PausedForMinutes', 'PlayingForMinutes', 'ScheduleIntervalMinutes', 'ScheduleMonthDay',
            'ForEachLimit', 'StreamCountThreshold', 'TranscodeCountThreshold', 'ProgressPercentThreshold'].forEach(function (key) {
            if (source[key] !== undefined) s[key] = parseInt(source[key], 10) || 0;
        });
        if (['Interval', 'Daily', 'Monthly'].indexOf(source.ScheduleMode) >= 0) s.ScheduleMode = source.ScheduleMode;
        if (typeof source.ScheduleTime === 'string') s.ScheduleTime = source.ScheduleTime;
        if (Array.isArray(source.ScheduleDays)) s.ScheduleDays = source.ScheduleDays.filter(function (d) { return DAYS.indexOf(d) >= 0; });
        if (['None', 'Items', 'Users'].indexOf(source.ForEach) >= 0) s.ForEach = source.ForEach;
        s.OnFailureScriptId = /^[0-9a-f]{32}$/.test(compactId(source.OnFailureScriptId)) ? compactId(source.OnFailureScriptId) : '';
        // The webhook address is never carried over: a copy or an import gets its own.
        s.WebhookToken = '';
        ensureWebhookToken(s);
        return s;
    }

    function addScript(script, message) {
        scripts.push(script);
        selectedId = script.Id;
        edited();
        renderEditor();
        if (message) Shared.setStatus('transferStatus', message, false, { timeout: 0 });
    }

    function newScript() {
        promptDialog([
            { name: 'name', label: 'Name', value: '', placeholder: 'Named after the template when left blank' },
            { name: 'description', label: 'Description', value: '', placeholder: 'What the script is for' },
            { name: 'template', label: 'Start from', type: 'select', value: 'blank', options: TEMPLATES.map(function (t) { return { value: t.key, label: t.label }; }) }
        ], { title: 'New script', submitText: 'Create' }).then(function (values) {
            if (!values) return;
            var template = TEMPLATES.find(function (t) { return t.key === values.template; }) || TEMPLATES[0];
            var name = (values.name || '').trim() || (template.key === 'blank' ? 'New script' : template.label);
            var script = sanitize(Object.assign({ Name: name }, template.build()), blankScript(name));
            script.Name = name;
            script.Description = (values.description || '').trim().slice(0, 500);
            addScript(script, template.key === 'blank' ? '' : 'Started from "' + template.label + '". Review it and click Save.');
            el('scriptName').focus();
            el('scriptName').select();
        });
    }

    function duplicateScript() {
        var source = current();
        if (!source) return;
        var copy = sanitize(JSON.parse(JSON.stringify(source)), blankScript(source.Name + ' (copy)'));
        copy.Name = source.Name + ' (copy)';
        copy.Enabled = false;
        addScript(copy, 'Duplicated as a disabled copy. Secrets are not copied.');
    }

    function exportScripts() {
        if (!scripts.length) {
            Shared.setStatus('transferStatus', 'There are no scripts to export.', true);
            return;
        }

        var now = new Date();
        var stamp = now.getFullYear() + '-' + String(now.getMonth() + 1).padStart(2, '0') + '-' + String(now.getDate()).padStart(2, '0');
        var exported = JSON.parse(JSON.stringify(scripts)).map(function (s) {
            s.Secrets = (s.Secrets || []).map(function (x) { return { Name: x.Name, Value: '' }; });
            s.WebhookToken = '';
            return s;
        });
        downloadJson('jellyfin-scripts-' + stamp + '.json', { Format: 'jellyfin-plugin-scripts', Version: 1, Scripts: exported });
        Shared.setStatus('transferStatus', 'Exported ' + scripts.length + ' script' + (scripts.length === 1 ? '' : 's') +
            (tracker && tracker.isDirty() ? ', including unsaved edits' : '') + '. Secret values and webhook addresses are left out.', false);
    }

    // Accepts an export file, a bare array of scripts, or one script.
    function readImport(data) {
        var list = Array.isArray(data) ? data : (data && Array.isArray(data.Scripts) ? data.Scripts : (data && data.Name !== undefined ? [data] : null));
        if (!list) return null;
        return list.filter(function (s) { return s && typeof s === 'object'; }).map(function (s) {
            var id = /^[0-9a-f]{32}$/.test(compactId(s.Id)) ? compactId(s.Id) : compactId(generateGuid());
            var script = sanitize(s, blankScript(String(s.Name || 'Imported script')));
            script.Id = id;
            script.Enabled = false;
            return script;
        });
    }

    function importScripts() {
        pickJsonFile().then(function (data) {
            if (data === null) return;
            var incoming = readImport(data);
            if (!incoming || !incoming.length) {
                Shared.setStatus('transferStatus', 'That file has no scripts in it.', true);
                return;
            }

            var replacing = incoming.filter(function (s) {
                return scripts.some(function (e) { return compactId(e.Id) === compactId(s.Id); });
            });
            var message = 'Import ' + incoming.length + ' script' + (incoming.length === 1 ? '' : 's') + '?' +
                (replacing.length ? ' ' + replacing.length + ' will replace a script with the same ID: ' +
                    replacing.map(function (s) { return '"' + s.Name + '"'; }).join(', ') + '.' : '') +
                ' Imported scripts start disabled so you can review their steps, then save. Secret values are not in the file, so enter them again.';
            return confirmDialog(message, { title: 'Import scripts', confirmText: 'Import' }).then(function (ok) {
                if (!ok) return;
                incoming.forEach(function (s) {
                    var index = scripts.findIndex(function (e) { return compactId(e.Id) === compactId(s.Id); });
                    // A script replaced in place keeps the address other services already call.
                    if (index >= 0 && has(s, 'Webhook') && scripts[index].WebhookToken) s.WebhookToken = scripts[index].WebhookToken;
                    if (index >= 0) scripts[index] = s;
                    else scripts.push(s);
                });
                selectedId = incoming[0].Id;
                edited();
                renderEditor();
                Shared.setStatus('transferStatus', 'Imported ' + incoming.length + '. They are disabled and not saved yet: review each one, enable it, and click Save.', false, { timeout: 0 });
            });
        }).catch(function () {
            Shared.setStatus('transferStatus', 'That file is not valid JSON.', true);
        });
    }

    // ---- Wiring ----

    function bind() {
        el('selectScript').addEventListener('change', function () { selectScript(this.value); });
        el('btnNewScript').addEventListener('click', newScript);
        el('btnDuplicateScript').addEventListener('click', duplicateScript);
        el('btnExportScripts').addEventListener('click', exportScripts);
        el('btnImportScripts').addEventListener('click', importScripts);

        el('btnDeleteScript').addEventListener('click', function () {
            var script = current();
            if (!script) return;
            var name = script.Name || 'Untitled';
            confirmDialog('Delete "' + name + '"? This saves right away. Unsaved edits to other scripts stay unsaved.', { title: 'Delete script', confirmText: 'Delete', destructive: true }).then(function (ok) {
                if (!ok) return;
                // Only the deletion is saved: the server's own copy loses this script, and edits to other scripts
                // wait for Save as before.
                var wasClean = !(tracker && tracker.isDirty());
                Shared.saveConfigWith(function (config) {
                    config.Scripts = (config.Scripts || []).filter(function (s) { return compactId(s.Id) !== script.Id; });
                }, 'transferStatus', { success: 'Deleted "' + name + '".', error: 'The server refused to delete the script.' }).then(function () {
                    scripts = scripts.filter(function (s) { return s.Id !== script.Id; });
                    selectedId = scripts.length ? scripts[0].Id : null;
                    if (wasClean && tracker) tracker.markClean();
                    edited();
                    if (selectedId) renderEditor();
                }).catch(function () { /* the status line already says why */ });
            });
        });

        el('scriptName').addEventListener('input', function () { current().Name = this.value; edited(); });
        el('scriptDescription').addEventListener('input', function () { current().Description = this.value; edited(); });
        el('scriptEnabled').addEventListener('change', function () { current().Enabled = this.checked; edited(); });
        createNumbers();
        el('btnNewWebhook').addEventListener('click', function () {
            confirmDialog('Make a new webhook address? Anything still calling the old one stops working once you save.', { title: 'New webhook address', confirmText: 'Make a new address' }).then(function (ok) {
                if (!ok) return;
                current().WebhookToken = newToken();
                renderWebhook();
                edited();
            });
        });
        el('forEachMode').addEventListener('change', function () {
            current().ForEach = this.value;
            renderConditions();
            renderOperationCards();
            renderVariables();
            renderWarnings();
            edited();
        });
        el('onFailureScript').addEventListener('change', function () { current().OnFailureScriptId = this.value; edited(); });
        matchChoice = createChoiceGroup(el('conditionMatch'), {
            onChange: function (value) {
                if (suppressWidgetEvents) return;
                current().Match = value;
                renderConditions();
                edited();
            }
        });

        // Steps: one editor moves into whichever card is open.
        var groups = {};
        Object.keys(OPERATION_TYPES).forEach(function (t) { (groups[OPERATION_TYPES[t].group] = groups[OPERATION_TYPES[t].group] || []).push(t); });
        el('newOperationType').innerHTML = Object.keys(groups).map(function (g) {
            return '<optgroup label="' + esc(g) + '">' + groups[g].map(function (t) {
                return '<option value="' + t + '">' + esc(OPERATION_TYPES[t].label) + '</option>';
            }).join('') + '</optgroup>';
        }).join('');

        operationList = createSortableCardList(el('operationList'), {
            items: [],
            editor: el('operationEditor'),
            escapeHtml: Shared.escapeHtml,
            render: function (op) {
                var warning = operationWarning(current(), op);
                var badges = [];
                if (op.OnlyIfVariable) badges.push({ label: 'if ' + op.OnlyIfVariable + ' ' + (OPERATOR_LABELS[op.OnlyIfOperator] || 'is') + ' ' + (op.OnlyIfValue === '' ? 'empty' : op.OnlyIfValue), cls: 'blue' });
                if (op.OutputName && HAS_OUTPUT[op.Type]) badges.push({ label: 'saves ' + op.OutputName, cls: 'purple' });
                if (op.ContinueOnError) badges.push({ label: 'continues on error', cls: 'gray' });
                if (warning) badges.push({ label: 'sometimes skipped', cls: 'orange' });
                return {
                    title: OPERATION_TYPES[op.Type] ? OPERATION_TYPES[op.Type].label : op.Type,
                    subtitle: operationSummary(op),
                    badges: badges,
                    dot: 'none'
                };
            },
            onSelect: function (index) { renderOperationEditor(index); },
            onMove: function (from, to) {
                var ops = current().Operations;
                var moved = ops.splice(from, 1)[0];
                ops.splice(to, 0, moved);
                operationList.setItems(ops);
                edited();
            }
        });

        el('btnAddOperation').addEventListener('click', function () {
            var ops = current().Operations;
            ops.push(newOperation(el('newOperationType').value));
            operationList.setItems(ops);
            operationList.setSelected(ops.length - 1);
            renderOperationCards();
            edited();
        });

        el('operationEditor').addEventListener('input', function (e) {
            if (e.target.matches('[data-key]') && e.target.type !== 'checkbox' && e.target.tagName !== 'SELECT') {
                if (readOperationField(e.target) === 'OutputName') renderVariables();
                edited();
            }
        });

        el('operationEditor').addEventListener('change', function (e) {
            // A number leaves its control, so the step's card shows it.
            if (e.target.closest('[data-number-mount]')) { operationList.render(); return; }
            if (!e.target.matches('[data-key]')) return;
            readOperationField(e.target);
            operationList.render();
            edited();
        });

        el('operationEditor').addEventListener('click', function (e) {
            if (e.target.closest('#btnCheckPath')) { checkPath(); return; }
            if (e.target.closest('#btnRemoveOperation')) {
                var index = operationList.getSelected();
                var ops = current().Operations;
                ops.splice(index, 1);
                operationList.setItems(ops);
                operationList.setSelected(-1);
                renderOperationCards();
                edited();
            }
        });

        variableList = createRecordList({
            label: 'Variables',
            openLabel: 'Copy {value}',
            maxHeight: '60vh',
            emptyText: 'No variables.',
            onOpen: function (item) {
                Shared.copyToClipboard(item.title).then(function (ok) {
                    if (!toaster) {
                        toaster = createToaster({ timeout: 2000 });
                        page.addCleanup(function () { toaster.destroy(); toaster = null; });
                    }
                    if (ok) toaster.success('Copied ' + item.title + '.');
                    else toaster.error('Could not copy ' + item.title + '.', 4000);
                });
            }
        });
        el('variableMount').appendChild(variableList.element);

        saveBar = createSaveBar({ onSave: save });
        el('saveBarMount').appendChild(saveBar.element);
        el('btnTestScript').addEventListener('click', runTest);
        el('btnRunScript').addEventListener('click', runNow);
        webhookField = createCopyField({ label: 'Webhook address' });
        el('webhookUrlMount').appendChild(webhookField.element);

        var names = document.createElement('datalist');
        names.id = 'scriptsVariableNames';
        view.appendChild(names);
        Shared.initCollapsibles();
    }

    function createWidgets() {
        triggerList = createCheckboxList({
            items: catalog.Triggers,
            idKey: 'Key',
            placeholder: 'Search ' + catalog.Triggers.length + ' triggers…',
            searchKeys: ['Label', 'Description', 'Category'],
            renderRow: function (t) { return { label: t.Label, sublabel: t.Description, note: t.Category }; },
            onChange: function (ids) {
                if (suppressWidgetEvents) return;
                // Keep the catalog's order so the stored list reads the same however it was clicked.
                current().Triggers = catalog.Triggers.map(function (t) { return t.Key; }).filter(function (k) { return ids.indexOf(k) >= 0; });
                onTriggersChanged();
            }
        });
        el('triggerMount').appendChild(triggerList.element);

        itemKindSelect = enterPicksSuggestion(createChipSelect({
            options: catalog.ItemKinds.map(function (k) { return { value: k.Value, label: k.Label }; }),
            placeholder: 'Any kind. Type to add one, such as Movie or Episode',
            onChange: function (values) {
                if (suppressWidgetEvents) return;
                current().ItemKinds = values;
                renderWarnings();
                edited();
            }
        }), true);
        el('itemKindMount').appendChild(itemKindSelect.element);

        userSelect = createUserMultiSelector({
            showSelectAll: true,
            emptyMessage: 'No users on this server.',
            onChange: function (ids) {
                if (suppressWidgetEvents) return;
                current().UserIds = ids.map(compactId);
                renderWarnings();
                edited();
            }
        });
        el('userMount').appendChild(userSelect.element);

        tracker = Shared.createChangeTracker({
            serialize: function () { return JSON.stringify(scripts); }
        });
    }

    // Reads the scripts back from the server. The configuration holds only the kept sentinel for a stored
    // secret, never its value. PreviousName lets the server find a renamed secret's stored value.
    function reloadScripts() {
        return Shared.getConfig().then(function (config) {
            scripts = (config.Scripts || []).map(function (s) {
                var script = sanitize(s, blankScript(s.Name));
                script.Id = compactId(s.Id);
                script.Enabled = !!s.Enabled;
                script.UserIds = (s.UserIds || []).map(compactId);
                script.WebhookToken = s.WebhookToken || script.WebhookToken;
                script.Secrets = (s.Secrets || []).map(function (x) {
                    return { Name: x.Name, Value: x.Value ? SECRET_KEPT : '', PreviousName: x.Name };
                });
                return script;
            });

            if (!scripts.some(function (s) { return s.Id === selectedId; })) {
                selectedId = scripts.length ? scripts[0].Id : null;
            }

            if (tracker) tracker.markClean();
            edited();
            if (selectedId) renderEditor();
        });
    }

    function load() {
        Shared.apiRequest('Scripts/Catalog', 'GET').then(function (result) {
            catalog = result;
            if (!triggerList) createWidgets();
            return Promise.all([reloadScripts(), userSelect.ready]);
        }).then(function () {
            var script = current();
            if (script) {
                suppressWidgetEvents = true;
                userSelect.setValue((script.UserIds || []).map(compactId));
                suppressWidgetEvents = false;
            }
        }).catch(function () {
            Shared.setStatus('scriptStatus', 'Could not load the scripts.', true, { timeout: 0 });
        });
    }
}
