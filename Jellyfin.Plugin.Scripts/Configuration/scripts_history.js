// scripts_history.js
// The History tab: recent runs in a table, a run's steps and variables in a dialog, and Clear history.

import {
    createScriptsPage, createPaginatedTable, confirmDialog, openDialog, renderRunSteps, renderRunVariables, formatDuration
} from './configurationpage?name=scripts_common.js';

export default function (view) {
    'use strict';

    var page = createScriptsPage(view, 1, {
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

    // Opens one run's steps and variables in a dialog.
    function showRun(run) {
        var body = document.createElement('div');
        body.innerHTML =
            '<div class="scripts-run-summary">' +
                Shared.badge(run.Succeeded ? 'green' : 'red', run.Succeeded ? 'Succeeded' : 'Failed') +
                '<span>' + esc(triggerLabel(run.Trigger)) + '</span>' +
                '<span>' + esc(when(run.StartedUtc)) + '</span>' +
                '<span>' + esc(formatDuration(Shared, run.DurationMs)) + '</span>' +
            '</div>' +
            (run.Summary ? '<div class="fieldDescription">' + esc(run.Summary) + '</div>' : '') +
            '<div class="base-subsection-title">Steps</div>';
        body.appendChild(renderRunSteps(Shared, run));
        var title = document.createElement('div');
        title.className = 'base-subsection-title';
        title.textContent = 'Variables';
        body.appendChild(title);
        body.appendChild(renderRunVariables(Shared, run));
        openDialog({
            title: run.ScriptName + (run.IsTest ? ' (test)' : ''),
            body: body,
            buttons: [{ text: 'Close', className: 'base-button secondary', action: function (close) { close(); } }]
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
            search: { enabled: true, placeholder: 'Search runs' },
            filters: { options: filters },
            emptyState: { message: 'No runs yet.' },
            columns: [
                {
                    key: 'ScriptName', type: 'custom', className: 'scripts-col-run', render: function (run) {
                        return '<div class="base-table-item-info"><div class="base-table-item-title">' + esc(run.ScriptName) + (run.IsTest ? ' (test)' : '') + '</div>' +
                            '<div class="base-table-item-sub">' + esc(triggerLabel(run.Trigger)) + (run.Summary ? ' · ' + esc(run.Summary) : '') + '</div></div>';
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
            confirmDialog('Clear every recorded run? This cannot be undone.', { title: 'Clear history', confirmText: 'Clear', destructive: true }).then(function (ok) {
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
