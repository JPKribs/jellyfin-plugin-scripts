import { createConfigPage, createChipSelect, confirmDialog, generateGuid, downloadJson, pickJsonFile } from './configurationpage?name=scripts_jpkribs_shared.js';
import { PLUGIN_ID, TABS, FIELD_LABELS, OPERATOR_LABELS, OPERATION_TYPES, renderRunSteps } from './configurationpage?name=scripts_common.js';

export default function (view) {
    'use strict';

    var page = createConfigPage(view, {
        pluginId: PLUGIN_ID,
        tabsKey: 'scripts',
        tabIndex: 0,
        tabs: TABS,
        bind: bind,
        load: load,
        isDirty: function () { return loaded && JSON.stringify(scripts) !== savedJson; }
    });
    var Shared = page.shared;

    var FIXED_VALUES = {
        PlayMethod: ['DirectPlay', 'DirectStream', 'Transcode'],
        IsLocal: ['true', 'false'],
        PlayedToCompletion: ['true', 'false'],
        TaskStatus: ['Completed', 'Failed', 'Cancelled', 'Aborted']
    };
    var HTTP_METHODS = ['POST', 'GET', 'PUT', 'PATCH', 'DELETE'];

    var catalog = null;
    var scripts = [];
    var savedJson = '[]';
    var selectedId = null;
    var loaded = false;
    var itemKindSelect = null;
    var userSelect = null;
    var conditionSelects = [];

    function el(id) { return view.querySelector('#' + id); }
    function esc(value) { return Shared.escapeHtml(value === null || value === undefined ? '' : String(value)); }

    // The shared chip select adds on Enter only once an option is highlighted with the arrow keys. Here Enter
    // also picks the suggestion whose label matches the typed text, so a library or task typed by name
    // stores its ID, and with firstMatch the top suggestion when nothing matches exactly.
    function enterPicksSuggestion(chips, firstMatch) {
        chips.element.addEventListener('keydown', function (e) {
            if (e.key !== 'Enter' || e.target.tagName !== 'INPUT') return;
            var typed = e.target.value.trim().toLowerCase();
            var options = Array.from(chips.element.querySelectorAll('.jpk-combo-dropdown:not(.hidden) .jpk-combo-option'));
            if (!typed || !options.length || options.some(function (o) { return o.classList.contains('active'); })) return;
            var pick = options.find(function (o) { return o.textContent.trim().toLowerCase() === typed; }) || (firstMatch ? options[0] : null);
            if (!pick) return;
            e.preventDefault();
            e.stopPropagation();
            pick.dispatchEvent(new MouseEvent('mousedown', { bubbles: true, cancelable: true }));
        }, true);
        return chips;
    }

    function duration(ms) {
        return ms < 1000 ? ms + ' ms' : Shared.formatDuration(Math.round(ms / 1000));
    }

    // IDs may arrive with or without dashes; the catalog uses the compact form.
    function compactId(id) {
        return String(id || '').replace(/-/g, '').toLowerCase();
    }

    function userItems(ids) {
        return (ids || []).map(function (id) {
            var key = compactId(id);
            var user = catalog.Users.find(function (u) { return compactId(u.Id) === key; });
            return { value: key, label: user ? user.Name : 'Unknown user (' + key.slice(0, 8) + ')' };
        });
    }

    function current() {
        return scripts.find(function (s) { return s.Id === selectedId; }) || null;
    }

    function triggerInfo(key) {
        return catalog.Triggers.find(function (t) { return t.Key === key; }) || null;
    }

    function fieldInfo(key) {
        return catalog.Fields.find(function (f) { return f.Key === key; }) || null;
    }

    function selectedTriggers(script) {
        return (script.Triggers || []).map(triggerInfo).filter(Boolean);
    }

    function changed() {
        renderCounts();
        renderSelector();
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
        Shared.setVisible('emptyState', scripts.length === 0);
        Shared.setVisible('scriptEditor', scripts.length > 0);
        el('btnDeleteScript').disabled = scripts.length === 0;
    }

    function selectScript(id) {
        selectedId = id;
        el('testResult').innerHTML = '';
        renderEditor();
    }

    // ---- Editor ----

    function renderEditor() {
        var script = current();
        if (!script) return;
        el('scriptName').value = script.Name || '';
        el('scriptEnabled').checked = !!script.Enabled;
        el('conditionMatch').value = script.Match || 'All';
        el('scriptCooldown').value = script.CooldownSeconds || 0;
        itemKindSelect.setValue(script.ItemKinds || []);
        userSelect.setValue(userItems(script.UserIds));
        renderTriggers();
        renderConditions();
        renderOperations();
        renderVariables();
        renderTestTriggers();
        renderItemKindWarning();
        renderUserWarning();
    }

    function renderTriggers() {
        var script = current();
        var groups = {};
        catalog.Triggers.forEach(function (t) {
            (groups[t.Category] = groups[t.Category] || []).push(t);
        });
        el('triggerList').innerHTML = Object.keys(groups).map(function (category) {
            return '<div class="scripts-trigger-group"><div class="scripts-trigger-category">' + esc(category) + '</div>' +
                groups[category].map(function (t) {
                    var checked = script.Triggers.indexOf(t.Key) >= 0 ? ' checked' : '';
                    return '<label class="emby-checkbox-label scripts-trigger">' +
                        '<input type="checkbox" is="emby-checkbox" data-trigger="' + esc(t.Key) + '"' + checked + ' />' +
                        '<span class="checkboxLabel">' + esc(t.Label) + '</span></label>';
                }).join('') + '</div>';
        }).join('');
    }

    function onTriggersChanged() {
        renderUserWarning();
        renderConditions();
        renderOperations();
        renderVariables();
        renderTestTriggers();
        renderItemKindWarning();
    }

    function renderItemKindWarning() {
        var script = current();
        var library = selectedTriggers(script).filter(function (t) { return t.Category === 'Library'; });
        var warn = library.length > 0 && (script.ItemKinds || []).length === 0;
        var box = el('itemKindWarn');
        box.textContent = warn
            ? 'With no item kinds, ' + library.map(function (t) { return t.Label.toLowerCase(); }).join(' and ') +
              ' runs for every kind a scan touches, including people, genres, studios, and folders.'
            : '';
        Shared.setVisible(box, warn);
    }

    function renderUserWarning() {
        var script = current();
        var triggers = selectedTriggers(script);
        var ignored = triggers.filter(function (t) { return !t.HasUser; });
        var warn = (script.UserIds || []).length > 0 && ignored.length > 0;
        var box = el('userWarn');
        box.textContent = warn
            ? 'Ignored for ' + ignored.map(function (t) { return t.Label.toLowerCase(); }).join(', ') + ', which ' +
              (ignored.length === 1 ? 'has' : 'have') + ' no user. The script runs on ' + (ignored.length === 1 ? 'that event' : 'those events') + ' whatever these users do.'
            : '';
        Shared.setVisible(box, warn);
    }

    // ---- Export and import ----

    function exportScripts() {
        if (!scripts.length) {
            Shared.setStatus('transferStatus', 'There are no scripts to export.', true);
            return;
        }

        var now = new Date();
        var stamp = now.getFullYear() + '-' + String(now.getMonth() + 1).padStart(2, '0') + '-' + String(now.getDate()).padStart(2, '0');
        downloadJson('jellyfin-scripts-' + stamp + '.json', { Format: 'jellyfin-plugin-scripts', Version: 1, Scripts: scripts });
        Shared.setStatus('transferStatus', 'Exported ' + scripts.length + ' script' + (scripts.length === 1 ? '' : 's') +
            (JSON.stringify(scripts) !== savedJson ? ', including unsaved edits.' : '.'), false);
    }

    // Accepts an export file, a bare array of scripts, or one script.
    function readImport(data) {
        var list = Array.isArray(data) ? data : (data && Array.isArray(data.Scripts) ? data.Scripts : (data && data.Name !== undefined ? [data] : null));
        if (!list) return null;
        return list.filter(function (s) { return s && typeof s === 'object'; }).map(function (s) {
            return {
                Id: /^[0-9a-f]{32}$/.test(compactId(s.Id)) ? compactId(s.Id) : generateGuid(),
                Name: String(s.Name || 'Imported script'),
                Enabled: false,
                Triggers: (Array.isArray(s.Triggers) ? s.Triggers : []).filter(triggerInfo),
                ItemKinds: (Array.isArray(s.ItemKinds) ? s.ItemKinds : []).filter(function (k) { return catalog.ItemKinds.indexOf(k) >= 0; }),
                UserIds: Array.isArray(s.UserIds) ? s.UserIds.map(compactId) : [],
                Match: s.Match === 'Any' ? 'Any' : 'All',
                // Only fields, operators, and steps this version knows survive, so a file from a newer
                // version or a hand edit cannot leave the editor unable to render.
                Conditions: (Array.isArray(s.Conditions) ? s.Conditions : []).filter(function (c) {
                    return c && FIELD_LABELS[c.Field] && OPERATOR_LABELS[c.Operator];
                }).map(function (c) {
                    return { Field: c.Field, Operator: c.Operator, Values: Array.isArray(c.Values) ? c.Values.map(String) : [] };
                }),
                Operations: (Array.isArray(s.Operations) ? s.Operations : []).filter(function (op) {
                    return op && OPERATION_TYPES[op.Type];
                }).map(function (op) {
                    return Object.assign(newOperation(op.Type || 'RunCommand'), op);
                }),
                CooldownSeconds: parseInt(s.CooldownSeconds, 10) || 0
            };
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
                ' Imported scripts start disabled so you can review their steps, then save.';
            return confirmDialog(message, { title: 'Import scripts', confirmText: 'Import' }).then(function (ok) {
                if (!ok) return;
                incoming.forEach(function (s) {
                    var index = scripts.findIndex(function (e) { return compactId(e.Id) === compactId(s.Id); });
                    if (index >= 0) scripts[index] = s;
                    else scripts.push(s);
                });
                selectedId = incoming[0].Id;
                changed();
                renderEditor();
                Shared.setStatus('transferStatus', 'Imported ' + incoming.length + '. They are disabled and not saved yet: review each one, enable it, and click Save.', false, { timeout: 0 });
            });
        }).catch(function () {
            Shared.setStatus('transferStatus', 'That file is not valid JSON.', true);
        });
    }

    // ---- Conditions ----

    function valueOptions(field) {
        if (field === 'User') return catalog.Users.map(function (u) { return { value: u.Id, label: u.Name }; });
        if (field === 'Library') return catalog.Libraries.map(function (l) { return { value: l.Id, label: l.Name }; });
        if (field === 'TaskKey') return catalog.Tasks.map(function (t) { return { value: t.Key, label: t.Name }; });
        if (FIXED_VALUES[field]) return FIXED_VALUES[field].map(function (v) { return { value: v, label: v }; });
        return [];
    }

    function labelled(field, values) {
        var options = valueOptions(field);
        return (values || []).map(function (v) {
            var match = options.find(function (o) { return o.value.toLowerCase() === String(v).toLowerCase(); });
            return match || { value: v, label: v };
        });
    }

    function conditionWarning(script, condition) {
        var triggers = selectedTriggers(script);
        if (!triggers.length) return '';
        var info = fieldInfo(condition.Field);
        if (!info) return '';
        var missing = triggers.filter(function (t) { return info.Triggers.indexOf(t.Key) < 0; });
        if (!missing.length) return '';
        if (missing.length === triggers.length) return 'None of the selected triggers supply this value, so this condition never matches.';
        var names = missing.map(function (t) { return t.Label.toLowerCase(); }).join(', ');
        return script.Match === 'Any'
            ? 'Never matches for ' + names + '.'
            : 'Never matches for ' + names + ', so with "all conditions" the script will not run on those.';
    }

    function renderConditions() {
        var script = current();
        conditionSelects.forEach(function (c) { c.destroy(); });
        conditionSelects = [];
        var list = el('conditionList');
        list.innerHTML = '';

        script.Conditions.forEach(function (condition, index) {
            var row = document.createElement('div');
            row.className = 'scripts-condition';
            row.innerHTML =
                '<div class="scripts-condition-line">' +
                    '<select is="emby-select" data-cond-field="' + index + '" aria-label="Field">' +
                        Object.keys(FIELD_LABELS).map(function (f) {
                            return '<option value="' + f + '"' + (f === condition.Field ? ' selected' : '') + '>' + esc(FIELD_LABELS[f]) + '</option>';
                        }).join('') +
                    '</select>' +
                    '<select is="emby-select" data-cond-op="' + index + '" aria-label="Operator">' +
                        Object.keys(OPERATOR_LABELS).map(function (o) {
                            return '<option value="' + o + '"' + (o === condition.Operator ? ' selected' : '') + '>' + esc(OPERATOR_LABELS[o]) + '</option>';
                        }).join('') +
                    '</select>' +
                    '<div class="scripts-condition-values"></div>' +
                    '<button type="button" class="jpk-row-btn" data-cond-del="' + index + '" title="Remove condition" aria-label="Remove condition">' +
                        '<span class="material-icons" aria-hidden="true">close</span></button>' +
                '</div>';

            var warning = conditionWarning(script, condition);
            if (warning) {
                var note = document.createElement('div');
                note.className = 'jpk-warn scripts-inline-warn';
                note.textContent = warning;
                row.appendChild(note);
            }

            var chips = enterPicksSuggestion(createChipSelect({
                options: valueOptions(condition.Field),
                value: labelled(condition.Field, condition.Values),
                placeholder: condition.Operator === 'MatchesRegex' ? 'Type a pattern, press Enter' : 'Pick or type a value, press Enter',
                allowCreate: true,
                onChange: function (values) {
                    condition.Values = values;
                }
            }), false);
            row.querySelector('.scripts-condition-values').appendChild(chips.element);
            conditionSelects.push(chips);
            list.appendChild(row);
        });
    }

    // ---- Steps ----

    function input(op, index, key, label, opts) {
        opts = opts || {};
        var id = 'op' + index + key;
        var value = op[key] === null || op[key] === undefined ? '' : op[key];
        var field;
        if (opts.textarea) {
            field = '<textarea is="emby-textarea" class="emby-textarea scripts-textarea" id="' + id + '" data-op="' + index + '" data-key="' + key + '" rows="' + (opts.rows || 3) + '"' +
                (opts.placeholder ? ' placeholder="' + esc(opts.placeholder) + '"' : '') + '>' + esc(value) + '</textarea>';
        } else {
            field = '<input is="emby-input" type="' + (opts.number ? 'number' : 'text') + '" id="' + id + '" data-op="' + index + '" data-key="' + key + '"' +
                (opts.number ? ' data-number="1" min="0"' : '') + ' value="' + esc(value) + '"' +
                (opts.placeholder ? ' placeholder="' + esc(opts.placeholder) + '"' : '') + ' />';
        }

        return '<div class="inputContainer"><label for="' + id + '">' + esc(label) + '</label>' + field +
            (opts.help ? '<div class="fieldDescription">' + opts.help + '</div>' : '') + '</div>';
    }

    function checkbox(op, index, key, label, help) {
        return '<div class="checkboxContainer' + (help ? ' checkboxContainer-withDescription' : '') + '"><label class="emby-checkbox-label">' +
            '<input type="checkbox" is="emby-checkbox" data-op="' + index + '" data-key="' + key + '"' + (op[key] ? ' checked' : '') + ' />' +
            '<span class="checkboxLabel">' + esc(label) + '</span></label>' +
            (help ? '<div class="fieldDescription checkboxFieldDescription">' + help + '</div>' : '') + '</div>';
    }

    function select(op, index, key, label, options) {
        return '<div class="selectContainer"><label>' + esc(label) + '</label>' +
            '<select is="emby-select" data-op="' + index + '" data-key="' + key + '">' +
            options.map(function (o) {
                return '<option value="' + esc(o.value) + '"' + (String(op[key]) === String(o.value) ? ' selected' : '') + '>' + esc(o.label) + '</option>';
            }).join('') + '</select></div>';
    }

    function operationFields(op, index) {
        switch (op.Type) {
            case 'RunCommand':
                return input(op, index, 'Command', op.RunThroughShell ? 'Shell line' : 'Command', {
                    placeholder: op.RunThroughShell ? '/scripts/notify.sh "$JF_ITEM_NAME" >> /tmp/log.txt' : '/scripts/notify.sh',
                    help: op.RunThroughShell
                        ? 'Runs through /bin/sh -c. Placeholders are not filled here, so event values cannot inject shell syntax; read them from the environment, such as "$JF_ITEM_NAME". On Windows the line runs through cmd /c, which expands %JF_...% before parsing it, so there read event values inside a script file instead.'
                        : 'A path to a script or program, or a name on the server\'s PATH such as curl. The server must be able to reach the path. In Docker, mount it into the container and use the path inside it.'
                }) +
                    (op.RunThroughShell ? '' : '<div class="scripts-path-check"><button is="emby-button" type="button" class="raised jpk-button-small" data-check-path="' + index + '"><span>Check path</span></button>' +
                        '<span class="scripts-path-result" id="pathResult' + index + '"></span></div>') +
                    checkbox(op, index, 'RunThroughShell', 'Run through the shell', 'Use this for pipes, redirects, or several commands in one line.') +
                    (op.RunThroughShell ? '' : input(op, index, 'Arguments', 'Arguments, one per line', {
                        textarea: true,
                        placeholder: '--title\n{{ITEM_NAME}}',
                        help: 'Each line is passed as one argument, with placeholders filled. Spaces and quotes in a value never split it.'
                    })) +
                    input(op, index, 'WorkingDirectory', 'Working folder', { placeholder: 'The script\'s own folder', help: 'Optional. Defaults to the folder holding the script.' }) +
                    input(op, index, 'TimeoutSeconds', 'Timeout in seconds', { number: true, help: 'The command is stopped after this long. 0 uses the default from Settings.' });
            case 'HttpRequest':
                return select(op, index, 'Method', 'Method', HTTP_METHODS.map(function (m) { return { value: m, label: m }; })) +
                    input(op, index, 'Url', 'URL', { placeholder: 'https://example.com/hook?item={{ITEM_NAME:url}}' }) +
                    input(op, index, 'Headers', 'Headers, one per line', { textarea: true, rows: 2, placeholder: 'Authorization: Bearer token' }) +
                    input(op, index, 'ContentType', 'Content type', { placeholder: 'application/json' }) +
                    input(op, index, 'Body', 'Body', { textarea: true, rows: 5, placeholder: '{ "text": "{{USER_NAME:json}} started {{ITEM_NAME:json}}" }' });
            case 'SendMessage':
                return input(op, index, 'Title', 'Title') +
                    input(op, index, 'Text', 'Message', { textarea: true, rows: 2 }) +
                    input(op, index, 'DisplaySeconds', 'Show for seconds', { number: true, help: '0 leaves it to the client.' });
            case 'StopPlayback':
                return input(op, index, 'Text', 'Message shown before stopping', { textarea: true, rows: 2, help: 'Optional.' }) +
                    input(op, index, 'Title', 'Message title', { help: 'Optional.' }) +
                    input(op, index, 'DisplaySeconds', 'Show for seconds', { number: true });
            case 'RefreshMetadata':
                return checkbox(op, index, 'FullRefresh', 'Full refresh', 'Fetch everything again rather than only what is missing.') +
                    checkbox(op, index, 'ReplaceAll', 'Replace existing metadata and images');
            case 'RunScheduledTask':
                return select(op, index, 'TaskKey', 'Scheduled task', [{ value: '', label: 'Choose a task' }].concat(catalog.Tasks.map(function (t) {
                    return { value: t.Key, label: t.Name };
                })));
            case 'WriteActivityLog':
                return input(op, index, 'Title', 'Entry name', { placeholder: 'The script\'s name' }) +
                    input(op, index, 'Text', 'Details', { textarea: true, rows: 2 });
            case 'Wait':
                return input(op, index, 'WaitSeconds', 'Seconds', { number: true });
            default:
                return '';
        }
    }

    function operationWarning(script, op) {
        var needs = OPERATION_TYPES[op.Type] && OPERATION_TYPES[op.Type].needs;
        var triggers = selectedTriggers(script);
        if (!needs || !triggers.length) return '';
        var key = needs === 'session' ? 'HasSession' : 'HasItem';
        var missing = triggers.filter(function (t) { return !t[key]; });
        if (!missing.length) return '';
        return 'Skipped for ' + missing.map(function (t) { return t.Label.toLowerCase(); }).join(', ') +
            ', which ' + (missing.length === 1 ? 'has' : 'have') + ' no ' + (needs === 'session' ? 'playback session' : 'item') + '.';
    }

    function renderOperations() {
        var script = current();
        var list = el('operationList');
        if (!script.Operations.length) {
            list.innerHTML = Shared.emptySection('No steps yet. Add one below.');
            return;
        }

        list.innerHTML = script.Operations.map(function (op, index) {
            var warning = operationWarning(script, op);
            return '<div class="scripts-op">' +
                '<div class="scripts-op-head">' +
                    '<span class="jpk-step-number">' + (index + 1) + '</span>' +
                    '<strong>' + esc(OPERATION_TYPES[op.Type] ? OPERATION_TYPES[op.Type].label : op.Type) + '</strong>' +
                    '<span class="scripts-op-spacer"></span>' +
                    '<button type="button" class="jpk-row-btn" data-op-move="-1" data-index="' + index + '" title="Move up"' + (index === 0 ? ' disabled' : '') + '><span class="material-icons">keyboard_arrow_up</span></button>' +
                    '<button type="button" class="jpk-row-btn" data-op-move="1" data-index="' + index + '" title="Move down"' + (index === script.Operations.length - 1 ? ' disabled' : '') + '><span class="material-icons">keyboard_arrow_down</span></button>' +
                    '<button type="button" class="jpk-row-btn" data-op-del="' + index + '" title="Remove step"><span class="material-icons">delete</span></button>' +
                '</div>' +
                (warning ? '<div class="jpk-warn scripts-inline-warn">' + esc(warning) + '</div>' : '') +
                '<div class="scripts-op-body">' + operationFields(op, index) +
                    checkbox(op, index, 'ContinueOnError', 'Continue on error', 'Run the following steps even if this one fails.') +
                '</div></div>';
        }).join('');
    }

    function newOperation(type) {
        return {
            Type: type, ContinueOnError: false,
            Command: '', Arguments: '', RunThroughShell: false, WorkingDirectory: '', TimeoutSeconds: 0,
            Method: 'POST', Url: '', Headers: '', Body: '', ContentType: 'application/json',
            Title: '', Text: '', DisplaySeconds: type === 'SendMessage' ? 10 : 0,
            TaskKey: '', FullRefresh: false, ReplaceAll: false, WaitSeconds: type === 'Wait' ? 5 : 0
        };
    }

    function checkPath(index) {
        var op = current().Operations[index];
        var result = el('pathResult' + index);
        result.textContent = 'Checking…';
        result.className = 'scripts-path-result';
        Shared.apiRequest('Scripts/CheckPath?path=' + encodeURIComponent(op.Command || ''), 'GET').then(function (check) {
            var ok = check && check.Exists && !check.IsDirectory && check.IsExecutable;
            result.textContent = check ? check.Message : 'No answer from the server.';
            result.classList.add(ok ? 'jpk-status-ok' : 'jpk-status-bad');
        }).catch(function () {
            result.textContent = 'Could not check the path.';
            result.classList.add('jpk-status-bad');
        });
    }

    // ---- Variables and test ----

    function renderVariables() {
        var script = current();
        var triggers = selectedTriggers(script);
        if (!triggers.length) triggers = catalog.Triggers;
        var seen = {};
        var rows = [];
        triggers.forEach(function (t) {
            t.Variables.forEach(function (v) {
                if (seen[v.Name]) return;
                seen[v.Name] = true;
                rows.push(v);
            });
        });
        el('variableList').innerHTML = rows.map(function (v) {
            return '<button type="button" class="scripts-variable" data-copy-var="' + esc(v.Name) + '" title="Copy {{' + esc(v.Name) + '}}">' +
                '<code>{{' + esc(v.Name) + '}}</code><span>' + esc(v.Description) + '</span></button>';
        }).join('');
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
        el('testResult').innerHTML = '<div class="jpk-info">Running…</div>';
        Shared.apiRequest('Scripts/Test', 'POST', { Script: script, Trigger: el('testTrigger').value }).then(function (run) {
            el('testResult').innerHTML =
                '<div class="scripts-run-summary">' + Shared.badge(run.Succeeded ? 'green' : 'red', run.Succeeded ? 'Succeeded' : 'Failed') +
                '<span>' + esc(duration(run.DurationMs)) + '</span></div>' +
                renderRunSteps(Shared, run);
        }).catch(function () {
            el('testResult').innerHTML = '<div class="jpk-error">The test could not run. Check the steps and try again.</div>';
        }).finally(function () {
            button.disabled = false;
        });
    }

    // ---- Save ----

    function validate(script) {
        if (!script.Name || !script.Name.trim()) return 'Every script needs a name.';
        for (var c = 0; c < script.Conditions.length; c++) {
            if (!(script.Conditions[c].Values || []).length) {
                return '"' + script.Name + '": the ' + FIELD_LABELS[script.Conditions[c].Field].toLowerCase() + ' condition needs a value.';
            }
        }

        for (var i = 0; i < script.Operations.length; i++) {
            var op = script.Operations[i];
            var label = '"' + script.Name + '" step ' + (i + 1) + ' ';
            if (op.Type === 'RunCommand' && !(op.Command || '').trim()) return label + 'needs a command.';
            if (op.Type === 'HttpRequest' && !/^https?:\/\//i.test((op.Url || '').trim())) return label + 'needs a URL that starts with http:// or https://.';
            if (op.Type === 'SendMessage' && !(op.Text || '').trim()) return label + 'needs message text.';
            if (op.Type === 'RunScheduledTask' && !op.TaskKey) return label + 'needs a scheduled task.';
            if (op.Type === 'Wait' && !(op.WaitSeconds >= 1 && op.WaitSeconds <= 3600)) return label + 'must wait between 1 and 3600 seconds.';
        }

        return '';
    }

    function save() {
        for (var i = 0; i < scripts.length; i++) {
            var problem = validate(scripts[i]);
            if (problem) {
                if (scripts[i].Id !== selectedId) {
                    selectScript(scripts[i].Id);
                    renderSelector();
                }
                Shared.setStatus('scriptStatus', problem, true);
                return;
            }
        }

        Shared.saveConfigWith(function (config) {
            config.Scripts = scripts;
        }, 'scriptStatus', { success: 'Saved.', error: 'The server refused the save. Check each script and try again.' }).then(function () {
            savedJson = JSON.stringify(scripts);
        }).catch(function () { /* reported on the status line */ });
    }

    // ---- Wiring ----

    function bind() {
        el('selectScript').addEventListener('change', function () { selectScript(this.value); });

        el('btnNewScript').addEventListener('click', function () {
            var script = {
                Id: generateGuid(), Name: 'New script', Enabled: true, Triggers: [], ItemKinds: [], UserIds: [],
                Match: 'All', Conditions: [], Operations: [], CooldownSeconds: 0
            };
            scripts.push(script);
            selectedId = script.Id;
            changed();
            renderEditor();
            el('scriptName').focus();
            el('scriptName').select();
        });

        el('btnExportScripts').addEventListener('click', exportScripts);
        el('btnImportScripts').addEventListener('click', importScripts);

        el('btnDeleteScript').addEventListener('click', function () {
            var script = current();
            if (!script) return;
            confirmDialog('Delete "' + (script.Name || 'Untitled') + '"? This saves right away.', { title: 'Delete script', confirmText: 'Delete', destructive: true }).then(function (ok) {
                if (!ok) return;
                scripts = scripts.filter(function (s) { return s.Id !== script.Id; });
                selectedId = scripts.length ? scripts[0].Id : null;
                changed();
                if (selectedId) renderEditor();
                save();
            });
        });

        el('scriptName').addEventListener('input', function () { current().Name = this.value; renderSelector(); });
        el('scriptEnabled').addEventListener('change', function () { current().Enabled = this.checked; changed(); });
        el('conditionMatch').addEventListener('change', function () { current().Match = this.value; renderConditions(); });
        el('scriptCooldown').addEventListener('input', function () { current().CooldownSeconds = parseInt(this.value, 10) || 0; });

        el('triggerList').addEventListener('change', function (e) {
            var box = e.target.closest('[data-trigger]');
            if (!box) return;
            var script = current();
            var key = box.getAttribute('data-trigger');
            script.Triggers = script.Triggers.filter(function (t) { return t !== key; });
            if (box.checked) script.Triggers.push(key);
            onTriggersChanged();
        });

        el('btnAddCondition').addEventListener('click', function () {
            current().Conditions.push({ Field: 'User', Operator: 'Is', Values: [] });
            renderConditions();
        });

        el('conditionList').addEventListener('change', function (e) {
            var script = current();
            var field = e.target.closest('[data-cond-field]');
            if (field) {
                var condition = script.Conditions[parseInt(field.getAttribute('data-cond-field'), 10)];
                condition.Field = field.value;
                condition.Values = [];
                renderConditions();
                return;
            }

            var op = e.target.closest('[data-cond-op]');
            if (op) {
                script.Conditions[parseInt(op.getAttribute('data-cond-op'), 10)].Operator = op.value;
                renderConditions();
            }
        });

        el('conditionList').addEventListener('click', function (e) {
            var del = e.target.closest('[data-cond-del]');
            if (!del) return;
            current().Conditions.splice(parseInt(del.getAttribute('data-cond-del'), 10), 1);
            renderConditions();
        });

        el('newOperationType').innerHTML = Object.keys(OPERATION_TYPES).map(function (t) {
            return '<option value="' + t + '">' + esc(OPERATION_TYPES[t].label) + '</option>';
        }).join('');

        el('btnAddOperation').addEventListener('click', function () {
            current().Operations.push(newOperation(el('newOperationType').value));
            renderOperations();
        });

        function readOperationField(target) {
            var index = parseInt(target.getAttribute('data-op'), 10);
            var key = target.getAttribute('data-key');
            var op = current().Operations[index];
            if (target.type === 'checkbox') op[key] = target.checked;
            else if (target.getAttribute('data-number')) op[key] = parseInt(target.value, 10) || 0;
            else op[key] = target.value;
            return key;
        }

        el('operationList').addEventListener('input', function (e) {
            if (e.target.matches('[data-op][data-key]') && e.target.type !== 'checkbox' && e.target.tagName !== 'SELECT') readOperationField(e.target);
        });

        el('operationList').addEventListener('change', function (e) {
            if (!e.target.matches('[data-op][data-key]')) return;
            var key = readOperationField(e.target);
            // Switching to the shell swaps the command field's label and hides the arguments.
            if (key === 'RunThroughShell') renderOperations();
        });

        el('operationList').addEventListener('click', function (e) {
            var script = current();
            var move = e.target.closest('[data-op-move]');
            if (move) {
                var from = parseInt(move.getAttribute('data-index'), 10);
                var to = from + parseInt(move.getAttribute('data-op-move'), 10);
                var moved = script.Operations.splice(from, 1)[0];
                script.Operations.splice(to, 0, moved);
                renderOperations();
                return;
            }

            var del = e.target.closest('[data-op-del]');
            if (del) {
                script.Operations.splice(parseInt(del.getAttribute('data-op-del'), 10), 1);
                renderOperations();
                return;
            }

            var check = e.target.closest('[data-check-path]');
            if (check) checkPath(parseInt(check.getAttribute('data-check-path'), 10));
        });

        el('variableList').addEventListener('click', function (e) {
            var button = e.target.closest('[data-copy-var]');
            if (!button) return;
            var text = '{{' + button.getAttribute('data-copy-var') + '}}';
            Shared.copyToClipboard(text).then(function (ok) {
                Shared.setStatus('scriptStatus', ok ? 'Copied ' + text : 'Copy failed.', !ok);
            });
        });

        el('btnSaveScripts').addEventListener('click', save);
        el('btnTestScript').addEventListener('click', runTest);
        Shared.initCollapsibles();
    }

    function load() {
        Promise.all([Shared.getConfig(), Shared.apiRequest('Scripts/Catalog', 'GET')]).then(function (results) {
            var config = results[0];
            catalog = results[1];
            scripts = (config.Scripts || []).map(function (s) {
                s.Triggers = s.Triggers || [];
                s.ItemKinds = s.ItemKinds || [];
                s.UserIds = (s.UserIds || []).map(compactId);
                s.Conditions = s.Conditions || [];
                s.Operations = s.Operations || [];
                return s;
            });
            savedJson = JSON.stringify(scripts);
            loaded = true;

            if (!itemKindSelect) {
                itemKindSelect = enterPicksSuggestion(createChipSelect({
                    options: catalog.ItemKinds.map(function (k) { return { value: k, label: k }; }),
                    placeholder: 'Any kind. Type to add one, such as Movie or Episode',
                    onChange: function (values) {
                        current().ItemKinds = values;
                        renderItemKindWarning();
                    }
                }), true);
                el('itemKindMount').appendChild(itemKindSelect.element);
            }

            if (!userSelect) {
                userSelect = enterPicksSuggestion(createChipSelect({
                    options: catalog.Users.map(function (u) { return { value: compactId(u.Id), label: u.Name }; }),
                    placeholder: 'Everyone. Type a name to add a user',
                    onChange: function (values) {
                        current().UserIds = values;
                        renderUserWarning();
                    }
                }), true);
                el('userMount').appendChild(userSelect.element);
            }

            if (!scripts.some(function (s) { return s.Id === selectedId; })) {
                selectedId = scripts.length ? scripts[0].Id : null;
            }

            changed();
            if (selectedId) renderEditor();
        }).catch(function () {
            Shared.setStatus('scriptStatus', 'Could not load the scripts.', true, { timeout: 0 });
        });
    }
}
