using Jellyfin.Plugin.Scripts.Services;
using JPKribs.Jellyfin.Base;
using MediaBrowser.Controller;
using MediaBrowser.Controller.Events;
using MediaBrowser.Controller.Events.Authentication;
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
        serviceCollection.AddSingleton<AuthenticationConsumers>();
        serviceCollection.AddSingleton<IEventConsumer<AuthenticationResultEventArgs>>(provider => provider.GetRequiredService<AuthenticationConsumers>());
        serviceCollection.AddSingleton<IEventConsumer<AuthenticationRequestEventArgs>>(provider => provider.GetRequiredService<AuthenticationConsumers>());
    }
}
