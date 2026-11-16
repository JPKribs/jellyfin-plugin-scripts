// scripts_history.js
// The History tab: recent runs in a table, a run's steps and variables in a dialog, and Clear history.

import {
    createScriptsPage, createDataTable, confirmDialog, openDialog, renderRunSummary, renderRunSteps, renderRunVariables, formatDuration
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
    function triggerLabel(key) { return triggerLabels[key] || key; }

    // Short enough for one line in the table: "Oct 25, 10:27:20 PM".
    function when(value) {
        var date = new Date(value);
        return isNaN(date) ? '' : date.toLocaleString(undefined, { month: 'short', day: 'numeric', hour: 'numeric', minute: '2-digit', second: '2-digit' });
    }

    // Opens one run's result, steps, and variables in a dialog.
    function showRun(run) {
        var body = document.createElement('div');
        body.appendChild(renderRunSummary(Shared, run, [
            { label: 'Trigger', value: triggerLabel(run.Trigger) },
            { label: 'Started', value: when(run.StartedUtc) }
        ]));
        if (run.Summary) {
            var summary = document.createElement('div');
            summary.className = 'fieldDescription';
            summary.textContent = run.Summary;
            body.appendChild(summary);
        }
        [['Steps', renderRunSteps(Shared, run)], ['Variables', renderRunVariables(Shared, run)]].forEach(function (part) {
            var title = document.createElement('h3');
            title.className = 'base-subsection-title';
            title.textContent = part[0];
            body.appendChild(title);
            body.appendChild(part[1]);
        });
        openDialog({
            title: run.ScriptName + (run.IsTest ? ' (test)' : ''),
            body: body,
            page: page,
            buttons: [{ text: 'Close', className: 'base-button secondary', action: function (close) { close(); } }]
        });
    }

    // fetchRuns
    // Reads one page of runs for the table from the History route.
    //
    // Inputs
    // query [object]: The table's search, filters, page, and page size.
    //
    // Output
    // [Promise]: The page, as { Items, TotalCount }.
    function fetchRuns(query) {
        var filter = (query.filters.run || [])[0] || '';
        var params = '?Skip=' + ((query.page - 1) * query.pageSize) + '&Take=' + query.pageSize
            + (query.search ? '&Search=' + encodeURIComponent(query.search) : '')
            + (filter ? '&Filter=' + encodeURIComponent(filter) : '');
        return Shared.apiRequest('Scripts/History' + params, 'GET');
    }

    // clearHistory
    // Asks first, then deletes every recorded run.
    function clearHistory() {
        confirmDialog('Clear every recorded run? This cannot be undone.', { title: 'Clear history', confirmText: 'Clear', destructive: true }).then(function (ok) {
            if (!ok) return;
            Shared.apiRequest('Scripts/History', 'DELETE').then(function () {
                Shared.setStatus('historyStatus', 'History cleared.', false);
                if (table) table.reload();
            }).catch(function () {
                Shared.setStatus('historyStatus', 'Could not clear the history.', true);
            });
        });
    }

    // createTable
    // Draws the runs with Base's Data Table, which fills the window and scrolls inside.
    //
    // Inputs
    // scripts [Array]: The scripts, offered as filters.
    function createTable(scripts) {
        if (table) table.destroy();
        var host = el('historyTable');
        host.innerHTML = '';
        table = createDataTable({
            label: 'Runs',
            fetch: fetchRuns,
            rowKey: 'Id',
            pageSize: 50,
            height: 'fill',
            search: { placeholder: 'Search runs' },
            filters: [{
                name: 'run',
                label: 'Runs',
                options: [
                    { value: 'failed', label: 'Failed' },
                    { value: 'test', label: 'Tests' }
                ].concat(scripts.map(function (s) { return { value: s.Id, label: s.Name }; }))
            }],
            actions: [{ id: 'clear', label: 'Clear history', icon: 'delete', variant: 'destructive', onClick: clearHistory }],
            emptyText: 'No runs yet.',
            widthKey: 'scripts-history',
            columns: [
                { key: 'ScriptName', label: 'Run', width: '2fr', sortable: false, render: function (run) { return run.ScriptName + (run.IsTest ? ' (test)' : ''); } },
                { key: 'Trigger', label: 'Trigger', width: '1.5fr', sortable: false, render: function (run) { return triggerLabel(run.Trigger); } },
                { key: 'StartedUtc', label: 'Started', width: '1.5fr', sortable: false, render: function (run) { return when(run.StartedUtc); } },
                { key: 'DurationMs', label: 'Duration', align: 'end', sortable: false, render: function (run) { return formatDuration(Shared, run.DurationMs); } },
                {
                    key: 'Succeeded', label: 'Result', width: '8rem', sortable: false, render: function (run) {
                        var cell = document.createElement('span');
                        cell.innerHTML = Shared.badge(run.Succeeded ? 'green' : 'red', run.Succeeded ? 'Succeeded' : 'Failed');
                        return cell;
                    }
                }
            ],
            onOpen: showRun
        });
        host.appendChild(table.element);

        // A phone keeps the run and its result. The dialog shows the trigger, the start, and the length.
        var phone = window.matchMedia('(max-width: 600px)');
        function fit() {
            if (!table) return;
            table.setColumnHidden('Trigger', phone.matches);
            table.setColumnHidden('StartedUtc', phone.matches);
            table.setColumnHidden('DurationMs', phone.matches);
        }
        fit();
        phone.addEventListener('change', fit);
        page.addCleanup(function () { phone.removeEventListener('change', fit); });
    }

    function bind() {
        page.addCleanup(function () { if (table) { table.destroy(); table = null; } });
    }

    function load() {
        Promise.all([Shared.getConfig(), Shared.apiRequest('Scripts/Catalog', 'GET')]).then(function (results) {
            (results[1].Triggers || []).forEach(function (t) { triggerLabels[t.Key] = t.Label; });
            createTable(results[0].Scripts || []);
        }).catch(function () {
            Shared.setStatus('historyStatus', 'Could not load the history.', true, { timeout: 0 });
        });
    }
}
