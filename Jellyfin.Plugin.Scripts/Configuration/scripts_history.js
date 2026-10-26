import { createConfigPage, createPaginatedTable, confirmDialog, openDialog } from './configurationpage?name=scripts_jpkribs_shared.js';
import { PLUGIN_ID, TABS, renderRunSteps, renderRunVariables, formatDuration } from './configurationpage?name=scripts_common.js';

export default function (view) {
    'use strict';

    var page = createConfigPage(view, {
        pluginId: PLUGIN_ID,
        tabsKey: 'scripts',
        tabIndex: 1,
        tabs: TABS,
        bind: bind,
        load: load
    });
    var Shared = page.shared;

    var table = null;
    var triggerLabels = {};

    function el(id) { return view.querySelector('#' + id); }
    function esc(value) { return Shared.escapeHtml(value === null || value === undefined ? '' : String(value)); }
    function triggerLabel(key) { return triggerLabels[key] || key; }

    // Short enough for one line in the table: "Oct 25, 10:27:20 PM".
    function when(value) {
        var date = new Date(value);
        return isNaN(date) ? '' : date.toLocaleString(undefined, { month: 'short', day: 'numeric', hour: 'numeric', minute: '2-digit', second: '2-digit' });
    }

    function showRun(run) {
        openDialog({
            title: run.ScriptName + (run.IsTest ? ' (test)' : ''),
            body:
                '<div class="scripts-run-summary">' +
                    Shared.badge(run.Succeeded ? 'green' : 'red', run.Succeeded ? 'Succeeded' : 'Failed') +
                    '<span>' + esc(triggerLabel(run.Trigger)) + '</span>' +
                    '<span>' + esc(when(run.StartedUtc)) + '</span>' +
                    '<span>' + esc(formatDuration(Shared, run.DurationMs)) + '</span>' +
                '</div>' +
                (run.Summary ? '<div class="fieldDescription">' + esc(run.Summary) + '</div>' : '') +
                '<div class="jpk-subsection-title">Steps</div>' + renderRunSteps(Shared, run) +
                '<div class="jpk-subsection-title">Variables</div>' + renderRunVariables(Shared, run),
            buttons: [{ text: 'Close', className: 'jpk-secondary', action: function (close) { close(); } }]
        });
    }

    function createTable(scripts) {
        var filters = [
            { value: 'failed', label: 'Failed' },
            { value: 'test', label: 'Tests' }
        ].concat(scripts.map(function (s) { return { value: s.Id, label: s.Name }; }));

        table = createPaginatedTable(view, Shared, {
            containerId: 'historyTable',
            endpoint: 'Scripts/History',
            selection: { enabled: false, idKey: 'Id' },
            search: { enabled: true, placeholder: 'Search scripts, events, users, items…' },
            filters: { options: filters },
            emptyState: { message: 'No runs yet.' },
            columns: [
                {
                    key: 'ScriptName', type: 'custom', className: 'scripts-col-run', render: function (run) {
                        return '<div class="jpk-table-item-info"><div class="jpk-table-item-title">' + esc(run.ScriptName) + (run.IsTest ? ' (test)' : '') + '</div>' +
                            '<div class="jpk-table-item-sub">' + esc(triggerLabel(run.Trigger)) + (run.Summary ? ' · ' + esc(run.Summary) : '') + '</div></div>';
                    }
                },
                { key: 'StartedUtc', type: 'custom', className: 'scripts-col-when', render: function (run) { return esc(when(run.StartedUtc)); } },
                { key: 'DurationMs', type: 'custom', className: 'scripts-col-duration', render: function (run) { return esc(formatDuration(Shared, run.DurationMs)); } },
                {
                    key: 'Succeeded', type: 'custom', className: 'scripts-col-result', render: function (run) {
                        return Shared.badge(run.Succeeded ? 'green' : 'red', run.Succeeded ? 'Succeeded' : 'Failed');
                    }
                }
            ],
            actions: { onRowClick: showRun }
        });
        page.addCleanup(function () { table.destroy(); table = null; });
    }

    function bind() {
        el('btnClearHistory').addEventListener('click', function () {
            confirmDialog('Delete every recorded run?', { title: 'Clear history', confirmText: 'Clear', destructive: true }).then(function (ok) {
                if (!ok) return;
                Shared.apiRequest('Scripts/History', 'DELETE').then(function () {
                    Shared.setStatus('historyStatus', 'History cleared.', false);
                    if (table) table.reload();
                }).catch(function () {
                    Shared.setStatus('historyStatus', 'Could not clear the history.', true);
                });
            });
        });
    }

    function load() {
        Promise.all([Shared.getConfig(), Shared.apiRequest('Scripts/Catalog', 'GET')]).then(function (results) {
            (results[1].Triggers || []).forEach(function (t) { triggerLabels[t.Key] = t.Label; });
            createTable(results[0].Scripts || []);
            return table.load();
        }).catch(function () {
            Shared.setStatus('historyStatus', 'Could not load the history.', true, { timeout: 0 });
        });
    }
}
