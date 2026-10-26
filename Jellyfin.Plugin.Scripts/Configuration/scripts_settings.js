import { createConfigPage } from './configurationpage?name=scripts_jpkribs_shared.js';
import { PLUGIN_ID, TABS } from './configurationpage?name=scripts_common.js';

export default function (view) {
    'use strict';

    var Shared = createConfigPage(view, {
        pluginId: PLUGIN_ID,
        tabsKey: 'scripts',
        tabIndex: 2,
        tabs: TABS,
        bind: bind,
        load: load
    }).shared;

    function inRange(value, min, max) {
        return !isNaN(value) && value >= min && value <= max;
    }

    function save() {
        var concurrent = Shared.getIntValue('maxConcurrentRuns', NaN);
        var timeout = Shared.getIntValue('defaultTimeout', NaN);
        var history = Shared.getIntValue('historySize', NaN);
        if (!inRange(concurrent, 1, 32)) return Shared.setStatus('settingsStatus', 'Runs at once must be between 1 and 32.', true);
        if (!inRange(timeout, 1, 3600)) return Shared.setStatus('settingsStatus', 'The timeout must be between 1 and 3600 seconds.', true);
        if (!inRange(history, 0, 5000)) return Shared.setStatus('settingsStatus', 'The history size must be between 0 and 5000.', true);

        Shared.saveConfigWith(function (config) {
            config.MaxConcurrentRuns = concurrent;
            config.DefaultTimeoutSeconds = timeout;
            config.HistorySize = history;
        }, 'settingsStatus').catch(function () { /* reported on the status line */ });
    }

    function bind() {
        Shared.bindClick('btnSaveSettings', save);
    }

    function load() {
        Shared.getConfig().then(function (config) {
            Shared.setValue('maxConcurrentRuns', config.MaxConcurrentRuns);
            Shared.setValue('defaultTimeout', config.DefaultTimeoutSeconds);
            Shared.setValue('historySize', config.HistorySize);
        }).catch(function () {
            Shared.setStatus('settingsStatus', 'Could not load the settings.', true, { timeout: 0 });
        });
    }
}
