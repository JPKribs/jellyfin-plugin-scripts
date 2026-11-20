// scripts_history.js
// The History tab: recent runs in a table, a run's steps and variables in a dialog, and Clear history.

import {
    createScriptsPage, createDataTable, confirmDialog, openDialog, renderRunSummary, renderRunSteps, renderRunVariables, formatDuration, t
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
    function runName(run) { return run.IsTest ? t.format('RunNameTest', run.ScriptName) : run.ScriptName; }

    // Short enough for one line in the table: "Oct 25, 10:27:20 PM".
    function when(value) {
        var date = new Date(value);
        return isNaN(date) ? '' : date.toLocaleString(t.language, { month: 'short', day: 'numeric', hour: 'numeric', minute: '2-digit', second: '2-digit' });
    }

    // Opens one run's result, steps, and variables in a dialog.
    function showRun(run) {
        var body = document.createElement('div');
        body.appendChild(renderRunSummary(Shared, run, [
            { label: t.tr('LabelTrigger'), value: triggerLabel(run.Trigger) },
            { label: t.tr('LabelStarted'), value: when(run.StartedUtc) }
        ]));
        if (run.Summary) {
            var summary = document.createElement('div');
            summary.className = 'fieldDescription';
            summary.textContent = run.Summary;
            body.appendChild(summary);
        }
        [[t.tr('HeaderSteps'), renderRunSteps(Shared, run)], [t.tr('HeaderVariables'), renderRunVariables(Shared, run)]].forEach(function (part) {
            var title = document.createElement('h3');
            title.className = 'base-subsection-title';
            title.textContent = part[0];
            body.appendChild(title);
            body.appendChild(part[1]);
        });
        openDialog({
            title: runName(run),
            body: body,
            page: page,
            buttons: [{ text: t.tr('ButtonClose'), className: 'base-button secondary', action: function (close) { close(); } }]
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
        confirmDialog(t.tr('ConfirmClearHistory'), { title: t.tr('TitleClearHistory'), confirmText: t.tr('ButtonClear'), destructive: true }).then(function (ok) {
            if (!ok) return;
            Shared.apiRequest('Scripts/History', 'DELETE').then(function () {
                Shared.setStatus('historyStatus', t.tr('StatusHistoryCleared'), false);
                if (table) table.reload();
            }).catch(function () {
                Shared.setStatus('historyStatus', t.tr('ErrorClearHistory'), true);
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
            label: t.tr('LabelRuns'),
            fetch: fetchRuns,
            rowKey: 'Id',
            pageSize: 50,
            height: 'fill',
            search: { placeholder: t.tr('PlaceholderSearchRuns') },
            filters: [{
                name: 'run',
                label: t.tr('LabelRuns'),
                options: [
                    { value: 'failed', label: t.tr('LabelFailed') },
                    { value: 'test', label: t.tr('FilterTests') }
                ].concat(scripts.map(function (s) { return { value: s.Id, label: s.Name }; }))
            }],
            actions: [{ id: 'clear', label: t.tr('TitleClearHistory'), icon: 'delete', variant: 'destructive', onClick: clearHistory }],
            emptyText: t.tr('EmptyRuns'),
            widthKey: 'scripts-history',
            columns: [
                { key: 'ScriptName', label: t.tr('LabelRun'), width: '2fr', sortable: false, render: runName },
                { key: 'Trigger', label: t.tr('LabelTrigger'), width: '1.5fr', sortable: false, render: function (run) { return triggerLabel(run.Trigger); } },
                { key: 'StartedUtc', label: t.tr('LabelStarted'), width: '1.5fr', sortable: false, render: function (run) { return when(run.StartedUtc); } },
                { key: 'DurationMs', label: t.tr('LabelDuration'), align: 'end', sortable: false, render: function (run) { return formatDuration(Shared, run.DurationMs); } },
                {
                    key: 'Succeeded', label: t.tr('LabelResult'), width: '8rem', sortable: false, render: function (run) {
                        var cell = document.createElement('span');
                        cell.innerHTML = Shared.badge(run.Succeeded ? 'green' : 'red', run.Succeeded ? t.tr('StatusSucceeded') : t.tr('LabelFailed'));
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
        Promise.all([Shared.getConfig(), Shared.apiRequest('Scripts/Catalog?language=' + encodeURIComponent(t.language), 'GET')]).then(function (results) {
            (results[1].Triggers || []).forEach(function (t) { triggerLabels[t.Key] = t.Label; });
            // The catalog lists only triggers a script can pick, so a Run now run takes its label here.
            if (!triggerLabels.Manual) triggerLabels.Manual = t.tr('TriggerManual');
            createTable(results[0].Scripts || []);
        }).catch(function () {
            Shared.setStatus('historyStatus', t.tr('ErrorLoadHistory'), true, { timeout: 0 });
        });
    }
}
