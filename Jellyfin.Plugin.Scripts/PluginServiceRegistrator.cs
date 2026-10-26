using System;
using Jellyfin.Data.Events.System;
using Jellyfin.Data.Events.Users;
using Jellyfin.Plugin.Scripts.Services;
using JPKribs.Jellyfin.Base;
using MediaBrowser.Controller;
using MediaBrowser.Controller.Events;
using MediaBrowser.Controller.Events.Authentication;
using MediaBrowser.Controller.Events.Updates;
using MediaBrowser.Controller.Plugins;
using Microsoft.Extensions.DependencyInjection;

namespace Jellyfin.Plugin.Scripts;

/// <summary>
/// Registers plugin services with the Jellyfin DI container.
/// </summary>
public class PluginServiceRegistrator : IPluginServiceRegistrator
{
    /// <inheritdoc />
    public void RegisterServices(IServiceCollection serviceCollection, IServerApplicationHost applicationHost)
    {
        // Writes script failures and the activity log step to Jellyfin's activity log, which is where the
        // dashboard surfaces plugin events to administrators.
        serviceCollection.AddActivityLogger();

        // Encrypts script secrets at rest. Keys live in a fixed directory under the Jellyfin data folder with a
        // pinned application name, so moving the host does not leave stored secrets undecryptable.
        serviceCollection.AddPluginSecretProtector(Plugin.SecretsName);

        serviceCollection.AddSingleton<SecretVault>();
        serviceCollection.AddSingleton<ScriptPaths>();
        serviceCollection.AddSingleton<HistoryStore>();
        serviceCollection.AddSingleton<EventContextBuilder>();
        serviceCollection.AddSingleton<OperationExecutor>();

        // One runner owns the queue, so it is both the hosted service and the instance the dispatcher and
        // the test endpoint call.
        serviceCollection.AddSingleton<ScriptRunner>();
        serviceCollection.AddHostedService(provider => provider.GetRequiredService<ScriptRunner>());
        serviceCollection.AddSingleton<ScriptDispatcher>();

        serviceCollection.AddHostedService<EventSubscriber>();
        // One instance answers every event the server publishes through its event bus.
        serviceCollection.AddSingleton<ServerEventConsumers>();
        AddConsumer<AuthenticationResultEventArgs>(serviceCollection);
        AddConsumer<AuthenticationRequestEventArgs>(serviceCollection);
        AddConsumer<UserCreatedEventArgs>(serviceCollection);
        AddConsumer<UserDeletedEventArgs>(serviceCollection);
        AddConsumer<UserUpdatedEventArgs>(serviceCollection);
        AddConsumer<UserLockedOutEventArgs>(serviceCollection);
        AddConsumer<UserPasswordChangedEventArgs>(serviceCollection);
        AddConsumer<PluginInstalledEventArgs>(serviceCollection);
        AddConsumer<PluginUpdatedEventArgs>(serviceCollection);
        AddConsumer<PluginUninstalledEventArgs>(serviceCollection);
        AddConsumer<PluginInstallingEventArgs>(serviceCollection);
        AddConsumer<PluginInstallationCancelledEventArgs>(serviceCollection);
        AddConsumer<PendingRestartEventArgs>(serviceCollection);
    }

    private static void AddConsumer<T>(IServiceCollection serviceCollection)
        where T : EventArgs
        => serviceCollection.AddSingleton<IEventConsumer<T>>(provider => (IEventConsumer<T>)provider.GetRequiredService<ServerEventConsumers>());
}
