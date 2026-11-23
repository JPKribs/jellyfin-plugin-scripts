// A stand-in ApiClient, LibraryMenu, and Dashboard with generic sample data for the Scripts doc screenshots.
window.__errs = [];
window.addEventListener('error', function (e) { __errs.push(String(e.message)); });
window.addEventListener('unhandledrejection', function (e) { __errs.push('rejection: ' + String(e.reason && e.reason.stack || e.reason)); });
(function () { var ce = console.error; console.error = function () { __errs.push([].join.call(arguments, ' ')); ce.apply(console, arguments); }; })();

// The plugin's strings come from server.py, which serves them as the plugin's Translations route does.
(function () {
    var real = window.fetch;
    window.fetch = function (url, options) {
        var text = String(url);
        var at = text.indexOf('Scripts/Translations');
        return real.call(window, at >= 0 ? '/' + text.slice(at) : url, options);
    };
})();

var USERS = [
    { Id: '3f0c9a1e5b7d4c2a8e6f1b3d5a7c9e01', Name: 'alex', Policy: { IsAdministrator: true } },
    { Id: '7a2e4c6b8d0f41e3a5c7e9b1d3f5a702', Name: 'jordan', Policy: { IsAdministrator: false } },
    { Id: 'b4d6f8a0c2e443a5b7d9f1a3c5e7b903', Name: 'sam', Policy: { IsAdministrator: false } }
];
var catalog = null;
var catalogReady = fetch('/catalog.json').then(function (r) { return r.json(); }).then(function (c) {
    c.Users = USERS.map(function (u) { return { Id: u.Id, Name: u.Name }; });
    c.Libraries = [{ Id: '1a1a1a1a1a1a41a1a1a1a1a1a1a1a101', Name: 'Movies' }, { Id: '2b2b2b2b2b2b42b2b2b2b2b2b2b2b202', Name: 'Music' }, { Id: '3c3c3c3c3c3c43c3c3c3c3c3c3c3c303', Name: 'Shows' }];
    c.Collections = [{ Id: '4d4d4d4d4d4d44d4d4d4d4d4d4d4d401', Name: 'Favorites' }, { Id: '5e5e5e5e5e5e45e5e5e5e5e5e5e5e502', Name: 'Holiday Movies' }];
    c.Tasks = [
        { Key: 'RefreshChapterImages', Name: 'Extract chapter images', Category: 'Library' },
        { Key: 'RefreshLibrary', Name: 'Scan media library', Category: 'Library' },
        { Key: 'DownloadSubtitles', Name: 'Download missing subtitles', Category: 'Library' },
        { Key: 'CleanActivityLog', Name: 'Clean activity log', Category: 'Maintenance' }
    ];
    catalog = c;
    return c;
});

function op(o) {
    return Object.assign({ Type: 'RunCommand', Command: '', Arguments: '', RunThroughShell: false, WorkingDirectory: '', TimeoutSeconds: 0, OutputName: '', OutputJsonPath: '',
        OnlyIfVariable: '', OnlyIfOperator: 'Is', OnlyIfValue: '', ContinueOnError: false }, o);
}
function script(o) {
    return Object.assign({ Description: '', Enabled: true, Triggers: [], ItemKinds: [], UserIds: [], Match: 'All', Conditions: [], Operations: [], CooldownSeconds: 0, Secrets: [],
        PausedForMinutes: 30, PlayingForMinutes: 180, ScheduleMode: 'Daily', ScheduleIntervalMinutes: 60, ScheduleTime: '03:00', ScheduleDays: [], ScheduleMonthDay: 1,
        ForEach: 'None', ForEachLimit: 50, OnFailureScriptId: '', WebhookToken: '', StreamCountThreshold: 3, TranscodeCountThreshold: 3, ProgressPercentThreshold: 90 }, o);
}
var KEPT = '__JPK_SECRET_KEPT__';
var config = {
    MaxConcurrentRuns: 4, DefaultTimeoutSeconds: 60, HistorySize: 200,
    Scripts: [
        script({ Id: 'a0000000000000000000000000000001', Name: 'Limit transcodes', Description: 'Stops a fourth transcode so the server keeps up.', Triggers: ['PlaybackStart'],
            Conditions: [
                { Field: 'PlayMethod', Operator: 'Is', Values: ['Transcode'] },
                { Field: 'ActiveTranscodes', Operator: 'AtLeast', Values: ['4'] },
                { Field: 'User', Operator: 'IsNot', Values: ['3f0c9a1e5b7d4c2a8e6f1b3d5a7c9e01'] },
                { Field: 'Client', Operator: 'Contains', Values: ['Android TV', 'Roku'] }
            ],
            Operations: [
                op({ Type: 'SendMessage', Target: 'EventSession', Title: 'Too many transcodes', Text: 'Too many streams are converting right now. Try a lower quality or direct play.', DisplaySeconds: 10 }),
                op({ Type: 'StopPlayback', Text: '' })
            ], CooldownSeconds: 300 }),
        script({ Id: 'a0000000000000000000000000000002', Name: 'Nightly scan', Description: 'Scans every library at 3:00.', Triggers: ['Schedule'], ScheduleMode: 'Daily', ScheduleTime: '03:00',
            Operations: [op({ Type: 'ScanLibraries' })] }),
        script({ Id: 'a0000000000000000000000000000003', Name: 'Scan after downloads', Description: 'Scans every library when the download manager calls the webhook.', Triggers: ['Webhook'],
            WebhookToken: '9c1f4e7a2b8d40c6a3e5f7b9d1c3e5a7', Operations: [op({ Type: 'ScanLibraries' })] }),
        script({ Id: 'a0000000000000000000000000000004', Name: 'Health check', Description: 'Messages the administrators when the service stops answering.', Triggers: ['Schedule'], ScheduleMode: 'Interval', ScheduleIntervalMinutes: 5,
            Operations: [
                op({ Type: 'HttpRequest', Method: 'GET', Url: 'https://status.example.com/health', Headers: '', ContentType: '', Body: '', Retries: 0, OutputName: 'HEALTH', ContinueOnError: true }),
                op({ Type: 'StopScript', OnlyIfVariable: 'HEALTH_STATUS', OnlyIfOperator: 'Is', OnlyIfValue: '200' }),
                op({ Type: 'SendMessage', Target: 'AdminSessions', Title: 'Service down', Text: 'status.example.com answered {{HEALTH_STATUS}}.', DisplaySeconds: 0 })
            ] }),
        script({ Id: 'a0000000000000000000000000000005', Name: 'Tag unwatched movies', Description: 'Tags every movie nobody has played in 90 days.', Triggers: ['Schedule'], ScheduleMode: 'Daily', ScheduleDays: [0], ScheduleTime: '04:00',
            ItemKinds: ['Movie'], ForEach: 'Items', ForEachLimit: 100,
            Conditions: [{ Field: 'DaysSincePlayed', Operator: 'AtLeast', Values: ['90'] }],
            Operations: [op({ Type: 'AddTag', Tag: 'unwatched' })] }),
        script({ Id: 'a0000000000000000000000000000006', Name: 'Post new movies', Description: 'Posts each new movie to the server channel.', Triggers: ['ItemAdded'], ItemKinds: ['Movie'],
            Operations: [op({ Type: 'HttpRequest', Method: 'POST', Url: 'https://discord.com/api/webhooks/{{SECRET_DISCORD_WEBHOOK}}', Headers: '', ContentType: 'application/json',
                Body: '{ "content": "New: {{ITEM_NAME:json}} ({{YEAR}})" }', Retries: 2 })],
            Secrets: [{ Name: 'DISCORD_WEBHOOK', Value: KEPT }, { Name: 'NTFY_TOPIC', Value: '' }] }),
        script({ Id: 'a0000000000000000000000000000007', Name: 'Stop long pauses', Enabled: false, Triggers: ['PlaybackPausedFor'], PausedForMinutes: 30,
            Operations: [op({ Type: 'StopPlayback', Text: 'Stopped after 30 minutes paused.' })] }),
        script({ Id: 'a0000000000000000000000000000008', Name: 'Still watching', Enabled: false, Triggers: ['PlaybackPlayingFor'], PlayingForMinutes: 180,
            Operations: [op({ Type: 'SendMessage', Target: 'EventSession', Title: 'Still watching?', Text: 'You have been watching for 3 hours.', DisplaySeconds: 0 })] })
    ]
};

var now = Date.now();
function ago(min) { return new Date(now - min * 60000).toISOString(); }
var ITEM_VARS = {
    EVENT: 'ItemAdded', TIMESTAMP: '2026-11-15T21:04:12Z', LOCAL_DATE: '2026-11-15', LOCAL_TIME: '21:04:12', HOUR: '21', DAY_OF_WEEK: 'Sunday', SCRIPT_NAME: 'Post new movies',
    SERVER_NAME: 'Home', SERVER_ID: '00000000000000000000000000000001', SERVER_VERSION: '12.4.0', ITEM_ID: '6f6f6f6f6f6f46f6f6f6f6f6f6f6f601', ITEM_NAME: 'Sample Movie', ITEM_KIND: 'Movie',
    ITEM_PATH: '/media/movies/Sample Movie (2024)/Sample Movie (2024).mkv', LIBRARY_ID: '1a1a1a1a1a1a41a1a1a1a1a1a1a1a101', LIBRARY_NAME: 'Movies', YEAR: '2024',
    RUNTIME_SECONDS: '7260', GENRES: 'Adventure, Science Fiction', TAGS: '', STUDIOS: 'Example Studios', OFFICIAL_RATING: 'PG-13', COMMUNITY_RATING: '7.4',
    VIDEO_WIDTH: '1920', VIDEO_HEIGHT: '1080', DATE_ADDED: '2026-11-15T21:03:40Z', DAYS_SINCE_ADDED: '0', HAS_SUBTITLES: 'true',
    PROVIDER_IMDB: 'tt0000001', PROVIDER_TMDB: '100001'
};
var runs = [
    { Id: 'r01', ScriptName: 'Limit transcodes', Trigger: 'PlaybackStart', StartedUtc: ago(3), DurationMs: 42, Succeeded: true, IsTest: false, Summary: '',
      Operations: [{ Type: 'SendMessage', Status: 'Succeeded', Message: 'Sent to 1 session.' }, { Type: 'StopPlayback', Status: 'Succeeded', Message: 'Stopped playback on Living Room TV.' }], Variables: {} },
    { Id: 'r02', ScriptName: 'Health check', Trigger: 'Schedule', StartedUtc: ago(9), DurationMs: 318, Succeeded: true, IsTest: false, Summary: '',
      Operations: [{ Type: 'HttpRequest', Status: 'Succeeded', Message: 'GET status.example.com returned 200.' }, { Type: 'StopScript', Status: 'Succeeded', Message: 'Stopped here.' }, { Type: 'SendMessage', Status: 'Skipped', Message: 'Skipped by Stop here.' }], Variables: {} },
    { Id: 'r03', ScriptName: 'Post new movies', Trigger: 'ItemAdded', StartedUtc: ago(27), DurationMs: 108, Succeeded: false, IsTest: true, Summary: 'Test run with sample values',
      Operations: [{ Type: 'HttpRequest', Status: 'Failed', Message: 'POST discord.com returned 400.', Output: '{"message": "Invalid Webhook Token", "code": 50027}' }], Variables: ITEM_VARS },
    { Id: 'r04', ScriptName: 'Scan after downloads', Trigger: 'Webhook', StartedUtc: ago(64), DurationMs: 12, Succeeded: true, IsTest: false, Summary: '',
      Operations: [{ Type: 'ScanLibraries', Status: 'Succeeded', Message: 'Queued a scan of every library.' }], Variables: {} },
    { Id: 'r05', ScriptName: 'Tag unwatched movies', Trigger: 'Schedule', StartedUtc: ago(140), DurationMs: 25, Succeeded: true, IsTest: false, Summary: '',
      Operations: [{ Type: 'AddTag', Status: 'Succeeded', Message: 'Tagged Sample Movie unwatched.' }], Variables: {} },
    { Id: 'r06', ScriptName: 'Nightly scan', Trigger: 'Schedule', StartedUtc: ago(1100), DurationMs: 9, Succeeded: true, IsTest: false, Summary: '',
      Operations: [{ Type: 'ScanLibraries', Status: 'Succeeded', Message: 'Queued a scan of every library.' }], Variables: {} },
    { Id: 'r07', ScriptName: 'Limit transcodes', Trigger: 'PlaybackStart', StartedUtc: ago(1260), DurationMs: 4, Succeeded: true, IsTest: true, Summary: 'Test run with sample values',
      Operations: [{ Type: 'SendMessage', Status: 'Skipped', Message: 'This event has no session to send a message to.' }, { Type: 'StopPlayback', Status: 'Skipped', Message: 'This event has no session to stop.' }], Variables: {} },
    { Id: 'r08', ScriptName: 'Health check', Trigger: 'Schedule', StartedUtc: ago(1500), DurationMs: 5021, Succeeded: false, IsTest: false, Summary: '',
      Operations: [{ Type: 'HttpRequest', Status: 'Failed', Message: 'GET status.example.com timed out.' }], Variables: {} }
];
var testRun = { Id: 't1', ScriptName: 'Limit transcodes', Trigger: 'PlaybackStart', StartedUtc: ago(0), DurationMs: 4, Succeeded: true, IsTest: true,
    Operations: runs[6].Operations, Variables: {} };

window.__saved = [];
function clone(o) { return JSON.parse(JSON.stringify(o)); }
// historyDays
// Fourteen days of sample run counts for the History chart, oldest first, ending today.
function historyDays() {
    var ok = [6, 9, 4, 7, 12, 8, 5, 10, 7, 11, 6, 9, 13, 8], bad = [0, 1, 0, 0, 2, 0, 0, 1, 0, 0, 3, 0, 1, 2], days = [];
    for (var i = 13; i >= 0; i--) {
        var d = new Date(Date.now() - i * 86400000);
        days.push(d.getFullYear() + '-' + String(d.getMonth() + 1).padStart(2, '0') + '-' + String(d.getDate()).padStart(2, '0'));
    }
    return { Days: days, Succeeded: ok, Failed: bad };
}
function history(url) {
    var q = new URLSearchParams(url.split('?')[1] || '');
    var search = (q.get('Search') || '').toLowerCase(), filter = q.get('Filter') || '';
    var list = runs.filter(function (r) {
        if (search && r.ScriptName.toLowerCase().indexOf(search) < 0) return false;
        if (filter === 'failed') return !r.Succeeded;
        if (filter === 'test') return r.IsTest;
        return true;
    });
    var skip = +(q.get('Skip') || 0), take = +(q.get('Take') || 50);
    return { Items: list.slice(skip, skip + take), TotalCount: list.length };
}
window.ApiClient = {
    serverAddress: function () { return 'https://jellyfin.example.com'; },
    getUrl: function (p) { return 'https://jellyfin.example.com/' + String(p).replace(/^\//, ''); },
    getCurrentUserId: function () { return USERS[0].Id; },
    getPluginConfiguration: function () { return Promise.resolve(clone(config)); },
    updatePluginConfiguration: function (id, c) { window.__saved.push(clone(c)); config = clone(c); return Promise.resolve({}); },
    getUsers: function () { return Promise.resolve(clone(USERS)); },
    getScheduledTasks: function () { return Promise.resolve([]); },
    getJSON: function (url) { return ApiClient.fetch({ url: url, type: 'GET' }); },
    ajax: function (o) { return ApiClient.fetch(o); },
    fetch: function (o) {
        var url = o.url || '';
        return catalogReady.then(function () {
            var body = url.indexOf('Catalog') >= 0 ? catalog
                : url.indexOf('History/Days') >= 0 ? historyDays()
                : url.indexOf('History') >= 0 ? (o.type === 'DELETE' ? {} : history(url))
                : url.indexOf('CheckPath') >= 0 ? { Exists: true, Executable: true, Message: 'Found, and executable.' }
                : url.indexOf('Scripts/Test') >= 0 ? testRun
                : url.indexOf('Status') >= 0 ? {} : {};
            return clone(body);
        });
    },
    getUserImageUrl: function () { return ''; },
    sendMessage: function () {}, sendWebSocketMessage: function () {}
};
window.Dashboard = { alert: function (m) { console.error('Dashboard.alert used: ' + m); }, processPluginConfigurationUpdateResult: function () {}, showLoadingMsg: function () {}, hideLoadingMsg: function () {} };

// Draws the dashboard header and its tabs, as Jellyfin's LibraryMenu.setTabs does for a plugin page.
window.LibraryMenu = {
    setTabs: function (key, index, getTabs) {
        var tabs = typeof getTabs === 'function' ? getTabs() : getTabs;
        var host = document.getElementById('jfTabs');
        if (!host) return;
        host.innerHTML = (tabs || []).map(function (t, i) {
            return '<a class="jfTab' + (i === index ? ' jfTabActive' : '') + '">' + t.name + '</a>';
        }).join('');
    }
};
