import { createConfigPage, createPaginatedTable, confirmDialog } from './configurationpage?name=scripts_jpkribs_shared.js';
import { PLUGIN_ID, TABS, renderRunSteps } from './configurationpage?name=scripts_common.js';

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

    function el(id) { return view.querySelector('#' + id); }
    function esc(value) { return Shared.escapeHtml(value === null || value === undefined ? '' : String(value)); }

    function duration(ms) {
        return ms < 1000 ? ms + ' ms' : Shared.formatDuration(Math.round(ms / 1000));
    }

    function showRun(run) {
        el('runDetailTitle').textContent = run.ScriptName + ' · ' + run.Trigger + (run.IsTest ? ' (test)' : '');
        el('runDetailSummary').innerHTML =
            Shared.badge(run.Succeeded ? 'green' : 'red', run.Succeeded ? 'Succeeded' : 'Failed') +
            '<span>' + esc(Shared.formatDate(run.StartedUtc)) + '</span>' +
            '<span>' + esc(duration(run.DurationMs)) + '</span>' +
            (run.Summary ? '<span>' + esc(run.Summary) + '</span>' : '');
        el('runDetailSteps').innerHTML = renderRunSteps(Shared, run);

        var variables = run.Variables || {};
        var names = Object.keys(variables).sort();
        el('runDetailVariables').innerHTML = names.length
            ? '<dl class="scripts-run-variables">' + names.map(function (name) {
                return '<dt>JF_' + esc(name) + '</dt><dd>' + esc(variables[name]) + '</dd>';
            }).join('') + '</dl>'
            : Shared.emptySection('No variables.');

        Shared.setVisible('runDetail', true);
        el('runDetail').scrollIntoView({ behavior: 'smooth', block: 'nearest' });
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
                    key: 'ScriptName', type: 'custom', render: function (run) {
                        return '<div class="jpk-table-item-info"><div class="jpk-table-item-title">' + esc(run.ScriptName) + (run.IsTest ? ' (test)' : '') + '</div>' +
                            '<div class="jpk-table-item-sub">' + esc(run.Trigger) + (run.Summary ? ' · ' + esc(run.Summary) : '') + '</div></div>';
                    }
                },
                { key: 'StartedUtc', type: 'custom', render: function (run) { return esc(Shared.formatDate(run.StartedUtc)); } },
                { key: 'DurationMs', type: 'custom', render: function (run) { return esc(duration(run.DurationMs)); } },
                {
                    key: 'Succeeded', type: 'custom', render: function (run) {
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
                    Shared.setVisible('runDetail', false);
                    Shared.setStatus('historyStatus', 'History cleared.', false);
                    if (table) table.reload();
                }).catch(function () {
                    Shared.setStatus('historyStatus', 'Could not clear the history.', true);
                });
            });
        });
        Shared.initCollapsibles();
    }

    function load() {
        Shared.setVisible('runDetail', false);
        Shared.getConfig().then(function (config) {
            createTable(config.Scripts || []);
            return table.load();
        }).catch(function () {
            Shared.setStatus('historyStatus', 'Could not load the history.', true, { timeout: 0 });
        });
    }
}
