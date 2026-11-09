using System;
using System.IO;
using System.Net.Http;
using Jellyfin.Plugin.Scripts.Services;
using JPKribs.Jellyfin.Base;
using MediaBrowser.Common.Configuration;
using MediaBrowser.Controller.Collections;
using MediaBrowser.Controller.Library;
using MediaBrowser.Controller.Providers;
using MediaBrowser.Controller.Session;
using MediaBrowser.Model.Activity;
using MediaBrowser.Model.IO;
using MediaBrowser.Model.Tasks;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;

namespace Jellyfin.Plugin.Scripts.Tests;

// A runner and its step executor wired to substitutes for the server, with the history and the secret vault in a
// temporary folder. Without a Data Protection provider the protector keeps secrets in plaintext.
internal sealed class TestServices : IDisposable
{
    public TestServices()
    {
        Folder = Path.Combine(Path.GetTempPath(), "scripts-tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Folder);
        var paths = Substitute.For<IApplicationPaths>();
        paths.PluginConfigurationsPath.Returns(Folder);

        ActivityManager = Substitute.For<IActivityManager>();
        SessionManager = Substitute.For<ISessionManager>();
        LibraryManager = Substitute.For<ILibraryManager>();
        var activityLogger = new ActivityLogger(ActivityManager, NullLogger<ActivityLogger>.Instance);
        Executor = new OperationExecutor(
            Substitute.For<IHttpClientFactory>(),
            SessionManager,
            Substitute.For<IProviderManager>(),
            Substitute.For<ITaskManager>(),
            Substitute.For<IFileSystem>(),
            activityLogger,
            Substitute.For<IUserManager>(),
            Substitute.For<IUserDataManager>(),
            LibraryManager,
            Substitute.For<ICollectionManager>(),
            NullLogger<OperationExecutor>.Instance);
        History = new HistoryStore(new ScriptPaths(paths), NullLogger<HistoryStore>.Instance);
        Runner = new ScriptRunner(
            Executor,
            History,
            activityLogger,
            new SecretProtector("test", NullLogger.Instance),
            new SecretVault(Path.Combine(Folder, "secrets.json"), NullLogger.Instance),
            NullLogger<ScriptRunner>.Instance);
    }

    public string Folder { get; }

    public IActivityManager ActivityManager { get; }

    public ISessionManager SessionManager { get; }

    public ILibraryManager LibraryManager { get; }

    public OperationExecutor Executor { get; }

    public HistoryStore History { get; }

    public ScriptRunner Runner { get; }

    public void Dispose()
    {
        Runner.Dispose();
        History.Dispose();
        try
        {
            Directory.Delete(Folder, true);
        }
        catch (IOException)
        {
            // A temp folder left behind is harmless.
        }
    }
}
