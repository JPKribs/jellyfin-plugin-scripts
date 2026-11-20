// scripts_settings.js
// The Settings tab: runs at once, the default command timeout, and how many runs the history keeps, with Save.

import { createScriptsPage, createSliderField, createDurationField, createSaveBar, t } from './configurationpage?name=scripts_common.js';

export default function (view) {
    'use strict';

    var page = createScriptsPage(view, 2, {
        bind: bind,
        load: load,
        isDirty: function () { return !!(snapshot !== null && snapshot !== serialize()); }
    });
    var Shared = page.shared;

    var concurrent = null;
    var timeout = null;
    var history = null;
    var bar = null;
    var snapshot = null;

    function el(id) { return view.querySelector('#' + id); }

    // serialize
    // The three settings as text, to tell whether they changed since the last load or save.
    //
    // Output
    // [string]: The settings.
    function serialize() {
        return JSON.stringify([concurrent.getValue(), timeout.getValue(), history.getValue()]);
    }

    function changed() {
        if (bar && snapshot !== null) bar.setDirty(serialize() !== snapshot);
    }

    // mount
    // Places a Base control under its label.
    //
    // Inputs
    // id [string]: The prefix of the mount's ID.
    // control [object]: The Base control handle.
    //
    // Output
    // [object]: The control handle.
    function mount(id, control) {
        el(id + 'Mount').appendChild(control.element);
        return control;
    }

    function save() {
        return Shared.saveConfigWith(function (config) {
            config.MaxConcurrentRuns = concurrent.getValue();
            config.DefaultTimeoutSeconds = timeout.getValue();
            config.HistorySize = history.getValue();
        }).then(function () {
            snapshot = serialize();
        });
    }

    function bind() {
        concurrent = mount('maxConcurrentRuns', createSliderField({ min: 1, max: 32, label: t.tr('LabelRunsAtOnce'), onChange: changed }));
        timeout = mount('defaultTimeout', createDurationField({ units: ['m', 's'], min: 1, max: 3600, required: true, onChange: changed }));
        history = mount('historySize', createSliderField({ min: 0, max: 5000, label: t.tr('LabelHistorySize'), onChange: changed }));
        bar = createSaveBar({ form: el('ScriptsSettingsForm'), fill: true, onSave: save });
        el('saveBarMount').appendChild(bar.element);
    }

    function load() {
        Shared.getConfig().then(function (config) {
            concurrent.setValue(config.MaxConcurrentRuns);
            timeout.setValue(config.DefaultTimeoutSeconds);
            history.setValue(config.HistorySize);
            snapshot = serialize();
            bar.setDirty(false);
        }).catch(function () {
            bar.setState('failed', t.tr('ErrorLoadSettings'));
        });
    }
}
