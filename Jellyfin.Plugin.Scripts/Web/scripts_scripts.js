// scripts_scripts.js
// The Scripts tab: the script picker and the editor for one script's triggers, filters, conditions, steps,
// secrets, and behavior, with Save, Test, and Run now.

import {
    createScriptsPage, createRepeater, createChipSelect, createTagInput, createComboBox, adornField, createCheckboxList, createUserMultiSelector, createSortableCardList,
    createChoiceGroup, createCopyField, createRecordList, createSaveBar, createSliderField, createDurationField,
    createToaster, confirmDialog, promptDialog, openDialog, generateGuid, downloadJson, pickJsonFile, setFieldState, SECRET_KEPT,
    fieldGroups, fieldLabel, NUMERIC_FIELDS, operatorLabels, operationTypes, renderRunSummary, renderRunSteps, t
} from './configurationpage?name=scripts_common.js';
import { templates, httpPresetLabels, HTTP_PRESETS } from './configurationpage?name=scripts_templates.js';

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

    // Who a Send message step reaches.
    function messageTargets() {
        return [
            { value: 'EventSession', label: t.tr('TargetEventSession') },
            { value: 'UserSessions', label: t.tr('TargetUserSessions') },
            { value: 'AllSessions', label: t.tr('TargetAllSessions') },
            { value: 'AdminSessions', label: t.tr('TargetAdminSessions') }
        ];
    }

    // A day of the week in the viewer's language.
    function dayLabel(day) {
        var labels = {
            Monday: t.tr('Monday'), Tuesday: t.tr('Tuesday'), Wednesday: t.tr('Wednesday'), Thursday: t.tr('Thursday'),
            Friday: t.tr('Friday'), Saturday: t.tr('Saturday'), Sunday: t.tr('Sunday')
        };
        return labels[day] || day;
    }

    // Steps whose output a later step can use.
    var HAS_OUTPUT = { RunCommand: true, HttpRequest: true };
    // The script's numbers, each a Base Slider or Duration Field: the mount, the script property, what it shows
    // while the property is missing, and for a trigger's number the trigger and the panel that holds it.
    // A minutes property is edited as a duration and stored in whole minutes.
    function scriptNumbers() {
        return [
            { mount: 'pausedMinutesMount', key: 'PausedForMinutes', fallback: 10, trigger: 'PlaybackPausedFor', block: 'pausedOptions', minutes: true, min: 1, max: 1440 },
            { mount: 'playingMinutesMount', key: 'PlayingForMinutes', fallback: 60, trigger: 'PlaybackPlayingFor', block: 'playingOptions', minutes: true, min: 1, max: 1440 },
            { mount: 'progressPercentMount', key: 'ProgressPercentThreshold', fallback: 90, trigger: 'PlaybackProgressReached', block: 'progressOptions', slider: { min: 1, max: 99, suffix: '%', label: t.tr('LabelPercentage') } },
            { mount: 'streamCountMount', key: 'StreamCountThreshold', fallback: 3, trigger: 'StreamCountReached', block: 'streamOptions', slider: { min: 1, max: 100, label: t.tr('LabelStreams') } },
            { mount: 'transcodeCountMount', key: 'TranscodeCountThreshold', fallback: 2, trigger: 'TranscodeCountReached', block: 'transcodeOptions', slider: { min: 1, max: 100, label: t.tr('LabelTranscodes') } },
            { mount: 'forEachLimitMount', key: 'ForEachLimit', fallback: 50, slider: { min: 1, max: 500, label: t.tr('LabelForEachLimit') } },
            { mount: 'cooldownMount', key: 'CooldownSeconds', fallback: 0, units: ['h', 'm', 's'], min: 0, max: 86400 }
        ];
    }
    // A step's numbers, each a Base Slider or Duration Field in the step editor.
    function stepNumbers() {
        return {
            TimeoutSeconds: { units: ['m', 's'], min: 0, max: 3600 },
            Retries: { slider: { min: 0, max: 5, label: t.tr('LabelRetries') } },
            DisplaySeconds: { slider: { min: 0, max: 300, suffix: ' s', label: t.tr('LabelSecondsShown') } },
            WaitSeconds: { units: ['m', 's'], min: 1, max: 3600 }
        };
    }

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
    // The script's variable names, offered by the step condition's variable field.
    var variableNames = [];
    var variableCombo = null;
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
        scriptNumbers().forEach(function (spec) {
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
        scriptNumbers().forEach(function (spec) {
            var value = orDefault(script[spec.key], spec.fallback);
            numberControls[spec.key].setValue(spec.minutes ? value * 60 : value);
            if (spec.block) Shared.setVisible(spec.block, has(script, spec.trigger));
        });
        suppressWidgetEvents = false;
    }

    // ---- Counts and selector ----

    function renderCounts() {
        var enabled = scripts.filter(function (s) { return s.Enabled; }).length;
        Shared.renderCards('scriptCounts', [
            { count: scripts.length, label: t.tr('TabScripts'), color: 'blue' },
            { count: enabled, label: t.tr('LabelEnabled'), color: 'green' },
            { count: scripts.length - enabled, label: t.tr('Disabled'), color: 'gray' }
        ]);
    }

    function renderSelector() {
        var select = el('selectScript');
        select.innerHTML = scripts.map(function (s) {
            var name = s.Name || t.tr('ScriptUntitled');
            return '<option value="' + esc(s.Id) + '">' + esc(s.Enabled ? name : t.format('ScriptNameDisabled', name)) + '</option>';
        }).join('');
        if (selectedId) select.value = selectedId;
        el('emptyState').innerHTML = scripts.length ? '' : Shared.emptySection(t.tr('EmptyScripts'));
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
        el('onFailureScript').innerHTML = '<option value="">' + esc(t.tr('OptionNothing')) + '</option>' + scripts.filter(function (s) { return s.Id !== script.Id; }).map(function (s) {
            return '<option value="' + esc(s.Id) + '">' + esc(s.Name || t.tr('ScriptUntitled')) + '</option>';
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
        el('itemKindWarn').textContent = kindWarn ? t.format('WarnNoItemKinds', library.map(quoted).join(', ')) : '';
        Shared.setVisible('itemKindWarn', kindWarn);

        var noUser = triggers.filter(function (t) { return !carriesUser(script, t); });
        var userWarn = (script.UserIds || []).length > 0 && noUser.length > 0;
        var ignored = noUser.map(quoted).join(', ');
        el('userWarn').textContent = !userWarn ? '' : noUser.length === 1 ? t.format('WarnUsersIgnoredOne', ignored) : t.format('WarnUsersIgnoredMany', ignored);
        Shared.setVisible('userWarn', userWarn);

        var mode = script.ForEach || 'None';
        Shared.setVisible('forEachLimitRow', mode !== 'None');
        var eachWarn = '';
        if (mode === 'Items' && (script.ItemKinds || []).length === 0) {
            eachWarn = t.tr('WarnForEachNoKinds');
        } else if (mode !== 'None' && triggers.length && !triggers.some(function (t) { return expands(script, t); })) {
            eachWarn = mode === 'Items' ? t.tr('WarnForEachItemsAlready') : t.tr('WarnForEachUsersAlready');
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
        if (script.ScheduleMode === 'Interval') return t.format('ScheduleStartInterval', script.ScheduleIntervalMinutes || 60);
        if (script.ScheduleMode === 'Monthly') return t.tr('ScheduleStartMonthly');
        var days = script.ScheduleDays || [];
        var time = script.ScheduleTime || '03:00';
        return days.length ? t.format('ScheduleStartDays', days.map(dayLabel).join(', '), time) : t.format('ScheduleStartDaily', time);
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
        if (field === 'DayOfWeek') return DAYS.map(function (d) { return { value: d, label: dayLabel(d) }; });
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
        if (missing.length === triggers.length) return t.tr('WarnConditionNoTrigger');
        var names = missing.map(quoted).join(', ');
        return script.Match === 'Any' ? t.format('WarnConditionNeverAny', names) : t.format('WarnConditionNeverAll', names);
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
    // The Repeater's value cell: a number box for a numeric field, a Base Chip Select for a field with choices, which
    // lists the choices that match as you type and shows their labels, such as user names, while storing their values,
    // and a Base Tag Input for any other field.
    //
    // Inputs
    // value [object]: The cell value, as { field, values }.
    // change [function]: Tells the Repeater the cell changed.
    //
    // Output
    // [object]: The cell's element, input, getValue, setValue, and destroy.
    function conditionValueField(value, change) {
        var state = value || { field: null, values: [] };
        var control = null;

        // Draws the control for the row's field, in place of the one before it.
        function build() {
            var before = control;
            if (NUMERIC_FIELDS[state.field]) {
                var input = document.createElement('input');
                input.setAttribute('is', 'emby-input');
                input.type = 'number';
                input.setAttribute('aria-label', t.tr('LabelValue'));
                input.value = state.values.length ? state.values[0] : '';
                input.addEventListener('input', function () {
                    state.values = input.value === '' ? [] : [input.value];
                    change();
                });
                var holder = document.createElement('div');
                holder.appendChild(input);
                var adorned = adornField(input);
                control = { element: holder.firstElementChild, input: input, destroy: function () { adorned.destroy(); } };
                swap(before);
                return;
            }
            var choices = valueOptions(state.field);
            if (choices.length) {
                var shown = labelled(state.field, state.values);
                // Each chip keeps the value as stored, so a saved condition reads back unchanged.
                var picker = createChipSelect({
                    value: state.values.map(function (v, i) { return { value: v, label: shown[i].label }; }),
                    options: choices,
                    placeholder: choices[0].label,
                    allowCreate: true,
                    pill: true,
                    onChange: function (values) {
                        state.values = values.map(String);
                        change();
                    }
                });
                var search = picker.element.querySelector('input');
                search.setAttribute('aria-label', t.tr('LabelValue'));
                control = { element: picker.element, input: search, destroy: function () { picker.destroy(); } };
                swap(before);
                return;
            }
            var tags = createTagInput({
                values: state.values,
                placeholder: t.tr('PlaceholderConditionValue'),
                inputLabel: t.tr('LabelValue'),
                onChange: function (values) {
                    state.values = values;
                    change();
                }
            });
            control = { element: tags.element, input: tags.input, destroy: function () { tags.destroy(); } };
            swap(before);
        }

        // Puts the new control where the old one was, so it stays the cell's own child.
        function swap(before) {
            if (!before) return;
            if (before.element.parentNode) before.element.parentNode.replaceChild(control.element, before.element);
            before.destroy();
        }

        build();
        return {
            get element() { return control.element; },
            get input() { return control.input; },
            getValue: function () { return { field: state.field, values: state.values.slice() }; },
            setValue: function (next) {
                state = next || { field: null, values: [] };
                state.values = (state.values || []).slice();
                build();
            },
            destroy: function () { if (control) control.destroy(); }
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
            note.textContent = t.format('WarnConditionNumbered', index + 1, warning);
            host.appendChild(note);
        });
    }

    // renderConditions
    // Draws the script's conditions with Base's Repeater.
    function renderConditions() {
        var script = current();
        if (!conditionRepeater) {
            var operators = operatorLabels();
            conditionRepeater = createRepeater({
                fields: [
                    {
                        key: 'Field', label: t.tr('LabelField'), type: 'select', width: '1fr',
                        options: fieldGroups().reduce(function (all, group) {
                            return all.concat(group.fields.map(function (f) { return { value: f.key, label: f.label }; }));
                        }, [])
                    },
                    {
                        key: 'Operator', label: t.tr('LabelOperator'), type: 'select', width: '1fr',
                        options: Object.keys(operators).map(function (o) { return { value: o, label: operators[o] }; })
                    },
                    { key: 'Values', label: t.tr('LabelValue'), type: 'custom', width: '2fr', render: conditionValueField }
                ],
                addText: t.tr('ButtonAddCondition'),
                rowName: t.tr('RowCondition'),
                emptyText: t.tr('EmptyConditions'),
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
            case 'RunCommand': return op.Command || t.tr('SummaryNoCommand');
            case 'HttpRequest': return (op.Method || 'POST') + ' ' + (op.Url || t.tr('SummaryNoUrl'));
            case 'SendMessage': return op.Text || t.tr('SummaryNoMessage');
            case 'StopPlayback': return op.Text ? t.tr('SummaryMessageThenStop') : t.tr('SummaryStopSession');
            case 'AddTag': case 'RemoveTag': return op.Tag || t.tr('SummaryNoTag');
            case 'AddToCollection': case 'RemoveFromCollection': {
                var c = catalog.Collections.find(function (x) { return compactId(x.Id) === compactId(op.CollectionId); });
                return c ? c.Name : t.tr('SummaryNoCollection');
            }
            case 'RunScript': {
                var s = scripts.find(function (x) { return compactId(x.Id) === compactId(op.ScriptId); });
                return s ? s.Name : t.tr('SummaryNoScript');
            }
            case 'RunScheduledTask': {
                var task = catalog.Tasks.find(function (x) { return x.Key === op.TaskKey; });
                return task ? task.Name : t.tr('SummaryNoTask');
            }
            case 'AppendToFile': return op.FilePath || t.tr('SummaryNoFile');
            case 'Wait': return t.format('ValueSeconds', op.WaitSeconds || 0);
            case 'SetFavorite': return op.Favorite ? t.tr('SummaryAddFavorite') : t.tr('SummaryRemoveFavorite');
            case 'SetUserDisabled': return op.Disable ? t.tr('OptionDisableTheUser') : t.tr('OptionEnableUser');
            case 'WriteActivityLog': return op.Title || t.tr('SummaryActivityEntry');
            case 'RefreshMetadata': return op.FullRefresh ? t.tr('LabelFullRefresh') : t.tr('SummaryStandardRefresh');
            case 'StopScript': return t.tr('SummaryStopScript');
            default: return '';
        }
    }

    function operationWarning(script, op) {
        var types = operationTypes();
        var needs = types[op.Type] && types[op.Type].needs;
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
        var names = missing.map(quoted).join(', ');
        var one = missing.length === 1;
        if (needs === 'session') return one ? t.format('WarnStepSkippedSessionOne', names) : t.format('WarnStepSkippedSessionMany', names);
        if (needs === 'item') return one ? t.format('WarnStepSkippedItemOne', names) : t.format('WarnStepSkippedItemMany', names);
        if (needs === 'user') return one ? t.format('WarnStepSkippedUserOne', names) : t.format('WarnStepSkippedUserMany', names);
        return one ? t.format('WarnStepSkippedUserItemOne', names) : t.format('WarnStepSkippedUserItemMany', names);
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
        el('operationEmpty').innerHTML = script.Operations.length ? '' : Shared.emptySection(t.tr('EmptySteps'), { inline: true });
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
        // A Base control sits in a plain field, since Jellyfin stretches every input inside an inputContainer.
        return '<div class="' + (opts.number ? 'base-field' : 'inputContainer') + '"><label' + (opts.number ? '' : ' for="' + id + '"') + '>' + esc(label) + '</label>' + field +
            (opts.help ? '<div class="fieldDescription">' + esc(opts.help) + '</div>' : '') + '</div>';
    }

    // mountStepNumbers
    // Puts a Base control in each number mount of the open step's editor, writing the step as it changes.
    //
    // Inputs
    // op [object]: The open step.
    function mountStepNumbers(op) {
        el('operationEditor').querySelectorAll('[data-number-mount]').forEach(function (mount) {
            var key = mount.getAttribute('data-number-mount');
            var control = numberControl(stepNumbers()[key], function (value) {
                op[key] = value === null || value === undefined ? 0 : value;
                edited();
            });
            control.setValue(op[key] || 0);
            mount.appendChild(control.element);
            editorControls.push(control);
        });
    }

    // mountVariableField
    // Puts a Base Combo Box for the step condition's variable in its mount, offering the script's variable names.
    //
    // Inputs
    // op [object]: The open step.
    function mountVariableField(op) {
        var combo = createComboBox({
            value: op.OnlyIfVariable || '',
            suggestions: variableNames,
            placeholder: t.tr('PlaceholderStepVariable'),
            onChange: function (value) {
                var fixed = upperName(value);
                if (fixed !== value) combo.setValue(fixed);
                op.OnlyIfVariable = fixed;
                edited();
            }
        });
        combo.input.id = 'opFieldOnlyIfVariable';
        combo.input.classList.add('base-mono');
        var mount = el('onlyIfVariableMount');
        mount.parentNode.replaceChild(combo.element, mount);
        editorControls.push(combo);
        variableCombo = combo;
    }

    // upperName
    // A variable name as the server keys it: capitals, digits, and underscores, without braces, $ or JF_.
    function upperName(text) {
        return String(text || '').toUpperCase().replace(/^\{+|\}+$|^\$/g, '').replace(/^JF_/, '').replace(/[^A-Z0-9_]/g, '_');
    }

    function checkbox(op, key, label, help) {
        return '<div class="checkboxContainer' + (help ? ' checkboxContainer-withDescription' : '') + '"><label class="emby-checkbox-label">' +
            '<input type="checkbox" is="emby-checkbox" data-key="' + key + '"' + (op[key] ? ' checked' : '') + ' />' +
            '<span class="checkboxLabel">' + esc(label) + '</span></label>' +
            (help ? '<div class="fieldDescription checkboxFieldDescription">' + esc(help) + '</div>' : '') + '</div>';
    }

    function select(op, key, label, options, opts) {
        opts = opts || {};
        return '<div class="selectContainer"><label>' + esc(label) + '</label>' +
            '<select is="emby-select" data-key="' + key + '"' + (opts.bool ? ' data-bool="1"' : '') + '>' +
            options.map(function (o) {
                return '<option value="' + esc(o.value) + '"' + (String(op[key]) === String(o.value) ? ' selected' : '') + '>' + esc(o.label) + '</option>';
            }).join('') + '</select>' + (opts.help ? '<div class="fieldDescription">' + esc(opts.help) + '</div>' : '') + '</div>';
    }

    function note(text) {
        return '<div class="fieldDescription">' + esc(text) + '</div>';
    }

    // richText
    // A string whose {0}, {1} stand for code samples, as HTML with each sample in a code element.
    //
    // Inputs
    // text [string]: The translated string.
    // codes [Array]: The code samples, in placeholder order.
    //
    // Output
    // [string]: The HTML.
    function richText(text, codes) {
        return esc(text).replace(/\{(\d+)\}/g, function (match, index) {
            return codes[+index] === undefined ? match : '<code>' + esc(codes[+index]) + '</code>';
        });
    }

    function operationFields(op) {
        switch (op.Type) {
            case 'RunCommand':
                return '<div class="base-choice-group" role="radiogroup" aria-label="' + esc(t.tr('LabelCommandMode')) + '">' +
                        '<label class="base-choice"><input type="radio" name="commandMode" value="direct"' + (op.RunThroughShell ? '' : ' checked') + ' />' +
                            '<span class="base-choice-name">' + esc(t.tr('ChoiceRunDirectly')) + '</span><span class="base-choice-text">' + esc(t.tr('HelpRunDirectly')) + '</span></label>' +
                        '<label class="base-choice"><input type="radio" name="commandMode" value="shell"' + (op.RunThroughShell ? ' checked' : '') + ' />' +
                            '<span class="base-choice-name">' + esc(t.tr('ChoiceThroughShell')) + '</span><span class="base-choice-text">' + esc(t.tr('HelpThroughShell')) + '</span></label>' +
                    '</div>' +
                    input(op, 'Command', op.RunThroughShell ? t.tr('LabelShellLine') : t.tr('LabelCommand'), {
                        placeholder: op.RunThroughShell ? '/scripts/notify.sh "$JF_ITEM_NAME" >> /tmp/log.txt' : '/scripts/notify.sh',
                        help: op.RunThroughShell
                            ? t.tr('HelpShellLine')
                            : t.tr('HelpCommand')
                    }) +
                    (op.RunThroughShell ? '' : '<button is="emby-button" type="button" class="raised base-button secondary small" id="btnCheckPath"><span>' + esc(t.tr('ButtonCheckPath')) + '</span></button>') +
                    (op.RunThroughShell ? '' : input(op, 'Arguments', t.tr('LabelArguments'), {
                        textarea: true,
                        placeholder: '--title\n{{ITEM_NAME}}',
                        help: t.tr('HelpArguments')
                    })) +
                    input(op, 'WorkingDirectory', t.tr('LabelWorkingFolder'), { placeholder: t.tr('PlaceholderWorkingFolder'), help: t.tr('HelpWorkingFolder') }) +
                    input(op, 'TimeoutSeconds', t.tr('LabelTimeout'), { number: true, help: t.tr('HelpStepTimeout') });
            case 'HttpRequest':
                var presets = httpPresetLabels();
                return select(op, '__preset', t.tr('LabelPreset'), [{ value: '', label: t.tr('OptionKeepRequest') }].concat(Object.keys(HTTP_PRESETS).map(function (k) {
                        return { value: k, label: presets[k] };
                    })), { help: t.tr('HelpPreset') }) +
                    select(op, 'Method', t.tr('LabelMethod'), HTTP_METHODS.map(function (m) { return { value: m, label: m }; })) +
                    input(op, 'Url', t.tr('LabelUrl'), { placeholder: 'https://example.com/hook?item={{ITEM_NAME:url}}' }) +
                    input(op, 'Headers', t.tr('LabelHeaders'), { textarea: true, rows: 2, placeholder: 'Authorization: Bearer {{SECRET_TOKEN}}' }) +
                    input(op, 'ContentType', t.tr('LabelContentType'), { placeholder: 'application/json' }) +
                    input(op, 'Body', t.tr('LabelBody'), { textarea: true, rows: 5, placeholder: '{ "text": "{{USER_NAME:json}} started {{ITEM_NAME:json}}" }' }) +
                    input(op, 'Retries', t.tr('LabelRetries'), { number: true, help: t.tr('HelpRetries') });
            case 'SendMessage':
                return select(op, 'Target', t.tr('LabelSendTo'), messageTargets()) +
                    input(op, 'Title', t.tr('LabelTitle')) +
                    input(op, 'Text', t.tr('LabelMessage'), { textarea: true, rows: 2 }) +
                    input(op, 'DisplaySeconds', t.tr('LabelShowFor'), { number: true, help: t.tr('HelpShowFor') });
            case 'StopPlayback':
                return input(op, 'Text', t.tr('LabelStopMessage'), { textarea: true, rows: 2, help: t.tr('HelpOptional') }) +
                    input(op, 'Title', t.tr('LabelMessageTitle'), { help: t.tr('HelpOptional') }) +
                    input(op, 'DisplaySeconds', t.tr('LabelShowFor'), { number: true });
            case 'PausePlayback':
                return note(t.tr('HelpPausePlayback'));
            case 'MarkPlayed':
            case 'MarkUnplayed':
                return note(t.tr('HelpEventItemForUser'));
            case 'SetFavorite':
                return select(op, 'Favorite', t.tr('Favorite'), [{ value: 'true', label: t.tr('OptionAddFavorite') }, { value: 'false', label: t.tr('OptionRemoveFavorite') }], { bool: true, help: t.tr('HelpEventItemForUser') });
            case 'AddTag':
            case 'RemoveTag':
                return input(op, 'Tag', t.tr('LabelTag'), { placeholder: t.tr('PlaceholderTag'), help: t.tr('HelpTag') });
            case 'AddToCollection':
            case 'RemoveFromCollection':
                return select(op, 'CollectionId', t.tr('LabelCollection'), [{ value: '', label: catalog.Collections.length ? t.tr('OptionChooseCollection') : t.tr('OptionNoCollections') }].concat(catalog.Collections.map(function (c) {
                    return { value: c.Id, label: c.Name };
                })));
            case 'RefreshMetadata':
                return checkbox(op, 'FullRefresh', t.tr('LabelFullRefresh'), t.tr('HelpFullRefresh')) +
                    checkbox(op, 'ReplaceAll', t.tr('LabelReplaceAll'));
            case 'SetUserDisabled':
                return select(op, 'Disable', t.tr('LabelAction'), [{ value: 'true', label: t.tr('OptionDisableTheUser') }, { value: 'false', label: t.tr('OptionEnableUser') }], { bool: true, help: t.tr('HelpSetUserDisabled') });
            case 'SignOutUser':
                return note(t.tr('HelpSignOutUser'));
            case 'ScanLibraries':
                return note(t.tr('HelpScanLibraries'));
            case 'RunScript':
                return select(op, 'ScriptId', t.tr('LabelScript'), [{ value: '', label: t.tr('OptionChooseScript') }].concat(scripts.filter(function (s) { return s.Id !== selectedId; }).map(function (s) {
                    return { value: s.Id, label: s.Name };
                })), { help: t.tr('HelpRunScript') });
            case 'RunScheduledTask':
                return select(op, 'TaskKey', t.tr('LabelScheduledTask'), [{ value: '', label: t.tr('OptionChooseTask') }].concat(catalog.Tasks.map(function (task) {
                    return { value: task.Key, label: task.Name };
                })));
            case 'WriteActivityLog':
                return input(op, 'Title', t.tr('LabelEntryName'), { placeholder: t.tr('PlaceholderEntryName') }) +
                    input(op, 'Text', t.tr('LabelDetails'), { textarea: true, rows: 2 });
            case 'AppendToFile':
                return input(op, 'FilePath', t.tr('File'), { placeholder: '/config/log/scripts.log', help: t.tr('HelpAppendFile') }) +
                    input(op, 'Text', t.tr('LabelLineToAppend'), { textarea: true, rows: 2, placeholder: '{{LOCAL_DATE}} {{LOCAL_TIME}} {{EVENT}} {{ITEM_NAME}}' });
            case 'Wait':
                return input(op, 'WaitSeconds', t.tr('LabelWaitFor'), { number: true });
            case 'StopScript':
                return note(t.tr('HelpStopScript'));
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
        var operators = operatorLabels();
        var http = op.Type === 'HttpRequest';
        var condition = '<h3 class="base-subsection-title">' + esc(t.tr('HeaderStepCondition')) + '</h3><div class="base-field-row">' +
            rowField('opFieldOnlyIfVariable', t.tr('LabelVariable'), '<div id="onlyIfVariableMount"></div>') +
            rowField('opFieldOnlyIfOperator', t.tr('LabelOperator'), '<select is="emby-select" id="opFieldOnlyIfOperator" data-key="OnlyIfOperator">' + Object.keys(operators).map(function (o) {
                return '<option value="' + o + '"' + (o === (op.OnlyIfOperator || 'Is') ? ' selected' : '') + '>' + esc(operators[o]) + '</option>';
            }).join('') + '</select>') +
            rowField('opFieldOnlyIfValue', t.tr('LabelValue'), '<input is="emby-input" type="text" id="opFieldOnlyIfValue" data-key="OnlyIfValue" value="' + esc(op.OnlyIfValue) + '" placeholder="' + esc(t.tr('PlaceholderStepValue')) + '" />') +
            '</div><div class="fieldDescription">' + esc(t.tr('HelpStepCondition')) + '</div>';
        var output = HAS_OUTPUT[op.Type]
            ? '<h3 class="base-subsection-title">' + esc(http ? t.tr('HeaderSaveResponse') : t.tr('HeaderSaveOutput')) + '</h3><div class="base-field-row">' +
                rowField('opFieldOutputName', t.tr('LabelSaveAs'), '<input is="emby-input" type="text" id="opFieldOutputName" class="base-mono" data-key="OutputName" data-upper="1" value="' + esc(op.OutputName) + '" placeholder="' + esc(t.tr('PlaceholderSaveAs')) + '" />') +
                rowField('opFieldOutputJsonPath', t.tr('LabelJsonField'), '<input is="emby-input" type="text" id="opFieldOutputJsonPath" data-key="OutputJsonPath" value="' + esc(op.OutputJsonPath) + '" placeholder="' + esc(t.tr('PlaceholderJsonField')) + '" />') +
                '</div><div class="fieldDescription">' + richText(http ? t.tr('HelpSaveResponse') : t.tr('HelpSaveOutput'), ['{{NAME}}', '{{NAME_STATUS}}']) + '</div>'
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
            checkbox(op, 'ContinueOnError', t.tr('LabelContinueOnError'), t.tr('HelpContinueOnError')) +
            '<button is="emby-button" type="button" class="raised base-button destructive small" id="btnRemoveOperation"><span>' + esc(t.tr('ButtonRemoveStep')) + '</span></button>';
        mountStepNumbers(op);
        mountVariableField(op);
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
            target.value = upperName(target.value);
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
        setFieldState(command, 'info', t.tr('StatusChecking'));
        Shared.apiRequest('Scripts/CheckPath?path=' + encodeURIComponent(op.Command || '') + '&language=' + encodeURIComponent(t.language), 'GET').then(function (check) {
            var ok = check && check.Exists && !check.IsDirectory && check.IsExecutable;
            setFieldState(command, ok ? 'success' : 'error', check ? check.Message : t.tr('ErrorNoAnswer'));
        }).catch(function () {
            setFieldState(command, 'error', t.tr('ErrorCheckPath'));
        });
    }

    // ---- Secrets ----

    // secretItems
    // Turns a script's secrets into Repeater rows. A stored value stays on the server, and its row holds the kept
    // sentinel. Each row keeps PreviousName, the name the secret was loaded with, so a renamed secret keeps its value.
    //
    // Inputs
    // secrets [Array]: The script's secrets.
    //
    // Output
    // [Array]: One row per secret.
    function secretItems(secrets) {
        return (secrets || []).map(function (secret) {
            return { Name: secret.Name, Value: secret.Value, PreviousName: secret.PreviousName || '' };
        });
    }

    // secretNameProblem
    // Checks a secret's name: capital letters, digits, and underscores, and no other secret of the script with it.
    function secretNameProblem(value, item, index) {
        if (!value) return '';
        if (!/^[A-Z0-9_]{1,40}$/.test(value)) return t.tr('ErrorSecretName');
        var rows = secretRepeater ? secretRepeater.getValue() : [];
        var same = rows.filter(function (row) { return row.Name === value; }).length;
        return same > 1 ? t.tr('ErrorSecretNameTaken') : '';
    }

    // renderSecrets
    // Draws the script's secrets with Base's Repeater, each a name and a Secret field.
    function renderSecrets() {
        if (!secretRepeater) {
            secretRepeater = createRepeater({
                fields: [
                    {
                        key: 'Name', label: t.tr('LabelName'), placeholder: t.tr('PlaceholderSecretName'), width: 'minmax(9rem, 1fr)', required: true, validate: secretNameProblem,
                        // Names are capital letters, digits, and underscores, so typing fixes them as they go.
                        transform: function (text) { return text.toUpperCase().replace(/[^A-Z0-9_]/g, '_'); }
                    },
                    { key: 'Value', label: t.tr('LabelValue'), type: 'secret', placeholder: t.tr('LabelValue'), width: 'minmax(11rem, 2fr)', clearable: true }
                ],
                addText: t.tr('ButtonAddSecret'),
                rowName: t.tr('RowSecret'),
                emptyText: t.tr('EmptySecrets'),
                onChange: onSecretsChanged
            });
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
            if (item.PreviousName) secret.PreviousName = item.PreviousName;
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
            add({ Name: op.OutputName, Description: t.format('VariableStepSaved', index + 1) });
            add({ Name: op.OutputName + '_STATUS', Description: op.Type === 'HttpRequest' ? t.format('VariableStepHttpStatus', index + 1) : t.format('VariableStepExitCode', index + 1) });
        });
        if (scripts.some(function (x) { return compactId(x.OnFailureScriptId) === script.Id; })) (catalog.FailureVariables || []).forEach(add);
        (script.Secrets || []).forEach(function (secret) {
            if (secret.Name) add({ Name: 'SECRET_' + secret.Name, Description: t.format('VariableSecret', secret.Name) });
        });
        variableList.setItems(rows.map(function (v) {
            return { id: v.Name, title: '{{' + v.Name + '}}', secondary: v.Description };
        }));
        variableNames = rows.map(function (v) { return v.Name; });
        if (variableCombo && variableCombo.element.isConnected) variableCombo.setSuggestions(variableNames);
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
        body.innerHTML = '<div class="base-info">' + esc(trigger ? t.format('TestRunning', trigger.Label) : t.tr('TestRunningNoTrigger')) + '</div>';
        openDialog({
            title: t.format('TitleTest', script.Name || t.tr('ScriptUntitled')),
            body: body,
            page: page,
            buttons: [{ text: t.tr('ButtonClose'), className: 'base-button secondary', action: function (close) { close(); } }]
        });
        Shared.apiRequest('Scripts/Test?language=' + encodeURIComponent(t.language), 'POST', { Script: script, Trigger: el('testTrigger').value }).then(function (run) {
            body.innerHTML = '';
            body.appendChild(renderRunSummary(Shared, run));
            body.appendChild(renderRunSteps(Shared, run));
        }).catch(function () {
            body.innerHTML = '<div class="base-error">' + esc(t.tr('ErrorTestFailed')) + '</div>';
        }).finally(function () {
            button.disabled = false;
        });
    }

    function runNow() {
        var script = current();
        if (!script) return;
        if (tracker && tracker.isDirty()) {
            Shared.setStatus('scriptStatus', t.tr('ErrorSaveFirst'), true);
            return;
        }

        var button = el('btnRunScript');
        button.disabled = true;
        Shared.apiRequest('Scripts/Run/' + script.Id + '?language=' + encodeURIComponent(t.language), 'POST').then(function () {
            Shared.setStatus('scriptStatus', t.tr('StatusRunStarted'), false, { timeout: 8000 });
        }).catch(function () {
            Shared.setStatus('scriptStatus', t.tr('ErrorRunFailed'), true);
        }).finally(function () {
            button.disabled = false;
        });
    }

    // ---- Save ----

    function validate(script) {
        if (!script.Name || !script.Name.trim()) return t.tr('ValidateNameMissing');
        var label = script.Name;
        for (var c = 0; c < script.Conditions.length; c++) {
            var condition = script.Conditions[c];
            var field = fieldLabel(condition.Field) || condition.Field;
            if (!(condition.Values || []).length) return t.format('ValidateConditionValue', label, field);
            if ((condition.Operator === 'AtLeast' || condition.Operator === 'AtMost') && condition.Values.some(function (v) { return isNaN(parseFloat(v)); })) {
                return t.format('ValidateConditionNumbers', label, field, operatorLabels()[condition.Operator]);
            }
        }

        var secretNames = {};
        for (var s = 0; s < (script.Secrets || []).length; s++) {
            var name = script.Secrets[s].Name;
            if (!/^[A-Z0-9_]{1,40}$/.test(name)) return t.format('ValidateSecretNames', label);
            if (secretNames[name]) return t.format('ValidateSecretTwice', label, name);
            secretNames[name] = true;
        }

        var outside = function (value, min, max) { return !(value >= min && value <= max); };
        if (outside(script.CooldownSeconds || 0, 0, 86400)) return t.format('ValidateCooldown', label);
        if (script.Triggers.indexOf('PlaybackPausedFor') >= 0 && outside(script.PausedForMinutes, 1, 1440)) return t.format('ValidatePausedFor', label);
        if (script.Triggers.indexOf('PlaybackPlayingFor') >= 0 && outside(script.PlayingForMinutes, 1, 1440)) return t.format('ValidatePlayingFor', label);
        if (script.Triggers.indexOf('PlaybackProgressReached') >= 0 && outside(script.ProgressPercentThreshold, 1, 99)) return t.format('ValidatePercentage', label);
        if (script.Triggers.indexOf('StreamCountReached') >= 0 && outside(script.StreamCountThreshold, 1, 100)) return t.format('ValidateStreamCount', label);
        if (script.Triggers.indexOf('TranscodeCountReached') >= 0 && outside(script.TranscodeCountThreshold, 1, 100)) return t.format('ValidateTranscodeCount', label);
        if (script.ForEach === 'Items' && !(script.ItemKinds || []).length) return t.format('ValidateForEachKinds', label);
        if (script.ForEach !== 'None' && outside(script.ForEachLimit, 1, 500)) return t.format('ValidateForEachLimit', label);

        var saved = {};
        for (var i = 0; i < script.Operations.length; i++) {
            var op = script.Operations[i];
            var n = i + 1;
            if (op.OutputName && HAS_OUTPUT[op.Type]) {
                if (!/^[A-Z0-9_]{1,40}$/.test(op.OutputName)) return t.format('ValidateStepOutputName', label, n);
                if (saved[op.OutputName]) return t.format('ValidateStepOutputTaken', label, n, op.OutputName, saved[op.OutputName]);
                saved[op.OutputName] = n;
            }
            if (op.OnlyIfVariable && !/^[A-Z0-9_]{1,80}$/.test(op.OnlyIfVariable)) return t.format('ValidateStepConditionName', label, n);
            if (op.OnlyIfVariable && (op.OnlyIfOperator === 'AtLeast' || op.OnlyIfOperator === 'AtMost') && !/\{\{/.test(op.OnlyIfValue || '') && isNaN(parseFloat(op.OnlyIfValue))) {
                return t.format('ValidateStepConditionNumber', label, n);
            }
            if (op.Type === 'RunCommand' && !(op.Command || '').trim()) return t.format('ValidateStepCommand', label, n);
            if (op.Type === 'HttpRequest' && !/^https?:\/\//i.test((op.Url || '').trim())) return t.format('ValidateStepUrl', label, n);
            if (op.Type === 'SendMessage' && !(op.Text || '').trim()) return t.format('ValidateStepMessage', label, n);
            if (op.Type === 'RunScheduledTask' && !op.TaskKey) return t.format('ValidateStepTask', label, n);
            if ((op.Type === 'AddTag' || op.Type === 'RemoveTag') && !(op.Tag || '').trim()) return t.format('ValidateStepTag', label, n);
            if ((op.Type === 'AddToCollection' || op.Type === 'RemoveFromCollection') && !op.CollectionId) return t.format('ValidateStepCollection', label, n);
            if (op.Type === 'RunScript' && !op.ScriptId) return t.format('ValidateStepScript', label, n);
            if (op.Type === 'AppendToFile' && !/^(\/|[A-Za-z]:\\)/.test((op.FilePath || '').trim())) return t.format('ValidateStepFile', label, n);
            if (op.Type === 'Wait' && outside(op.WaitSeconds, 1, 3600)) return t.format('ValidateStepWait', label, n);
            if (op.Type === 'RunCommand' && outside(op.TimeoutSeconds || 0, 0, 3600)) return t.format('ValidateStepTimeout', label, n);
            if (op.Type === 'HttpRequest' && outside(op.Retries || 0, 0, 5)) return t.format('ValidateStepRetries', label, n);
            if ((op.Type === 'SendMessage' || op.Type === 'StopPlayback') && outside(op.DisplaySeconds || 0, 0, 300)) return t.format('ValidateStepShowFor', label, n);
        }

        return '';
    }

    // save
    // The Save Bar's save: checks every script, saves them, and reloads them.
    //
    // Output
    // [Promise]: Resolves once saved, and rejects with the reason it did not save, which the Save Bar shows.
    function save() {
        for (var i = 0; i < scripts.length; i++) {
            var problem = validate(scripts[i]);
            if (problem) {
                if (scripts[i].Id !== selectedId) {
                    selectScript(scripts[i].Id);
                    renderSelector();
                }
                return Promise.reject(new Error(problem));
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
            throw new Error(t.tr('ErrorSaveRefused'));
        });
    }

    // ---- New, duplicate, export, import ----

    function blankScript(name) {
        return {
            Id: compactId(generateGuid()), Name: name || t.tr('ScriptNewName'), Description: '', Enabled: true, Triggers: [], ItemKinds: [], UserIds: [],
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
        var operators = operatorLabels();
        var types = operationTypes();
        var s = Object.assign(base, {
            Name: String(source.Name || base.Name),
            Description: String(source.Description || ''),
            Triggers: (source.Triggers || base.Triggers).filter(triggerInfo),
            ItemKinds: (source.ItemKinds || []).filter(function (k) { return catalog.ItemKinds.some(function (x) { return x.Value === k; }); }),
            UserIds: (source.UserIds || []).map(compactId),
            Match: source.Match === 'Any' ? 'Any' : 'All',
            Conditions: (source.Conditions || []).filter(function (c) {
                return c && fieldLabel(c.Field) && operators[c.Operator];
            }).map(function (c) {
                return { Field: c.Field, Operator: c.Operator, Values: Array.isArray(c.Values) ? c.Values.map(String) : [] };
            }),
            Operations: (source.Operations || []).filter(function (op) { return op && types[op.Type]; }).map(function (op) {
                var filled = Object.assign(newOperation(op.Type), op);
                ['OutputName', 'OutputJsonPath', 'OnlyIfVariable', 'OnlyIfValue'].forEach(function (key) { filled[key] = filled[key] === null || filled[key] === undefined ? '' : String(filled[key]); });
                if (!operators[filled.OnlyIfOperator]) filled.OnlyIfOperator = 'Is';
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
        var list = templates();
        promptDialog([
            { name: 'name', label: t.tr('LabelName'), value: '', placeholder: t.tr('PlaceholderNewName') },
            { name: 'description', label: t.tr('LabelDescription'), value: '', placeholder: t.tr('PlaceholderNewDescription') },
            { name: 'template', label: t.tr('LabelStartFrom'), type: 'select', value: 'blank', options: list.map(function (item) { return { value: item.key, label: item.label }; }) }
        ], { title: t.tr('ScriptNewName'), submitText: t.tr('Create') }).then(function (values) {
            if (!values) return;
            var template = list.find(function (item) { return item.key === values.template; }) || list[0];
            var name = (values.name || '').trim() || (template.key === 'blank' ? t.tr('ScriptNewName') : template.label);
            var script = sanitize(Object.assign({ Name: name }, template.build()), blankScript(name));
            script.Name = name;
            script.Description = (values.description || '').trim().slice(0, 500);
            addScript(script, template.key === 'blank' ? '' : t.format('StatusStartedFromTemplate', template.label));
            el('scriptName').focus();
            el('scriptName').select();
        });
    }

    function duplicateScript() {
        var source = current();
        if (!source) return;
        var copyName = t.format('ScriptCopyName', source.Name);
        var copy = sanitize(JSON.parse(JSON.stringify(source)), blankScript(copyName));
        copy.Name = copyName;
        copy.Enabled = false;
        addScript(copy, t.tr('StatusDuplicated'));
    }

    function exportScripts() {
        if (!scripts.length) {
            Shared.setStatus('transferStatus', t.tr('ErrorNoScriptsToExport'), true);
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
        var unsaved = !!(tracker && tracker.isDirty());
        var exported = scripts.length === 1
            ? (unsaved ? t.tr('StatusExportedOneUnsaved') : t.tr('StatusExportedOne'))
            : (unsaved ? t.format('StatusExportedManyUnsaved', scripts.length) : t.format('StatusExportedMany', scripts.length));
        Shared.setStatus('transferStatus', exported, false);
    }

    // Accepts an export file, a bare array of scripts, or one script.
    function readImport(data) {
        var list = Array.isArray(data) ? data : (data && Array.isArray(data.Scripts) ? data.Scripts : (data && data.Name !== undefined ? [data] : null));
        if (!list) return null;
        return list.filter(function (s) { return s && typeof s === 'object'; }).map(function (s) {
            var id = /^[0-9a-f]{32}$/.test(compactId(s.Id)) ? compactId(s.Id) : compactId(generateGuid());
            var script = sanitize(s, blankScript(String(s.Name || t.tr('ScriptImportedName'))));
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
                Shared.setStatus('transferStatus', t.tr('ErrorImportEmpty'), true);
                return;
            }

            var replacing = incoming.filter(function (s) {
                return scripts.some(function (e) { return compactId(e.Id) === compactId(s.Id); });
            });
            var replacingNames = replacing.map(function (s) { return '"' + s.Name + '"'; }).join(', ');
            var message = [
                incoming.length === 1 ? t.tr('ConfirmImportOne') : t.format('ConfirmImportMany', incoming.length),
                !replacing.length ? '' : replacing.length === 1 ? t.format('ConfirmImportReplacingOne', replacingNames) : t.format('ConfirmImportReplacingMany', replacing.length, replacingNames),
                t.tr('ConfirmImportReview')
            ].filter(Boolean).join(' ');
            return confirmDialog(message, { title: t.tr('DialogImportScripts'), confirmText: t.tr('ButtonImport') }).then(function (ok) {
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
                Shared.setStatus('transferStatus', t.format('StatusImported', incoming.length), false, { timeout: 0 });
            });
        }).catch(function () {
            Shared.setStatus('transferStatus', t.tr('ErrorImportJson'), true);
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
            var name = script.Name || t.tr('ScriptUntitled');
            confirmDialog(t.format('ConfirmDeleteScript', name), { title: t.tr('TitleDeleteScript'), confirmText: t.tr('Delete'), destructive: true }).then(function (ok) {
                if (!ok) return;
                // Only the deletion is saved: the server's own copy loses this script, and edits to other scripts
                // wait for Save as before.
                var wasClean = !(tracker && tracker.isDirty());
                Shared.saveConfigWith(function (config) {
                    config.Scripts = (config.Scripts || []).filter(function (s) { return compactId(s.Id) !== script.Id; });
                }, 'transferStatus', { success: t.format('StatusDeleted', name), error: t.tr('ErrorDeleteRefused') }).then(function () {
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
            confirmDialog(t.tr('ConfirmNewAddress'), { title: t.tr('TitleNewWebhookAddress'), confirmText: t.tr('ButtonMakeNewAddress') }).then(function (ok) {
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
        var types = operationTypes();
        Object.keys(types).forEach(function (type) { (groups[types[type].group] = groups[types[type].group] || []).push(type); });
        el('newOperationType').innerHTML = Object.keys(groups).map(function (g) {
            return '<optgroup label="' + esc(g) + '">' + groups[g].map(function (type) {
                return '<option value="' + type + '">' + esc(types[type].label) + '</option>';
            }).join('') + '</optgroup>';
        }).join('');

        operationList = createSortableCardList(el('operationList'), {
            items: [],
            editor: el('operationEditor'),
            escapeHtml: Shared.escapeHtml,
            render: function (op) {
                var warning = operationWarning(current(), op);
                var operators = operatorLabels();
                var stepTypes = operationTypes();
                var badges = [];
                if (op.OnlyIfVariable) badges.push({ label: t.format('BadgeStepCondition', op.OnlyIfVariable, operators[op.OnlyIfOperator] || operators.Is, op.OnlyIfValue === '' ? t.tr('BadgeEmpty') : op.OnlyIfValue), cls: 'blue' });
                if (op.OutputName && HAS_OUTPUT[op.Type]) badges.push({ label: t.format('BadgeSaves', op.OutputName), cls: 'purple' });
                if (op.ContinueOnError) badges.push({ label: t.tr('BadgeContinuesOnError'), cls: 'gray' });
                if (warning) badges.push({ label: t.tr('BadgeSometimesSkipped'), cls: 'orange' });
                return {
                    title: stepTypes[op.Type] ? stepTypes[op.Type].label : op.Type,
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
            label: t.tr('HeaderVariables'),
            openLabel: t.tr('LabelCopyValue'),
            maxHeight: '60vh',
            emptyText: t.tr('EmptyVariables'),
            onOpen: function (item) {
                Shared.copyToClipboard(item.title).then(function (ok) {
                    if (!toaster) {
                        toaster = createToaster({ timeout: 2000 });
                        page.addCleanup(function () { toaster.destroy(); toaster = null; });
                    }
                    if (ok) toaster.success(t.format('ToastCopied', item.title));
                    else toaster.error(t.format('ErrorCopy', item.title), 4000);
                });
            }
        });
        el('variableMount').appendChild(variableList.element);

        saveBar = createSaveBar({ fill: true, onSave: save });
        el('saveBarMount').appendChild(saveBar.element);
        el('btnTestScript').addEventListener('click', runTest);
        el('btnRunScript').addEventListener('click', runNow);
        webhookField = createCopyField({ label: t.tr('LabelWebhookAddress') });
        el('webhookUrlMount').appendChild(webhookField.element);

        Shared.initCollapsibles();
    }

    function createWidgets() {
        triggerList = createCheckboxList({
            items: catalog.Triggers,
            idKey: 'Key',
            placeholder: t.format('PlaceholderSearchTriggers', catalog.Triggers.length),
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

        itemKindSelect = createChipSelect({
            options: catalog.ItemKinds.map(function (k) { return { value: k.Value, label: k.Label }; }),
            placeholder: t.tr('PlaceholderItemKinds'),
            onChange: function (values) {
                if (suppressWidgetEvents) return;
                current().ItemKinds = values;
                renderWarnings();
                edited();
            }
        });
        el('itemKindMount').appendChild(itemKindSelect.element);

        userSelect = createUserMultiSelector({
            showSelectAll: true,
            emptyMessage: t.tr('EmptyUsers'),
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
        Shared.apiRequest('Scripts/Catalog?language=' + encodeURIComponent(t.language), 'GET').then(function (result) {
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
            Shared.setStatus('scriptStatus', t.tr('ErrorLoadScripts'), true, { timeout: 0 });
        });
    }
}
