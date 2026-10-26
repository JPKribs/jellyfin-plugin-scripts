using System;
using System.Threading.Tasks;
using Jellyfin.Data.Events.System;
using Jellyfin.Data.Events.Users;
using Jellyfin.Plugin.Scripts.Models;
using MediaBrowser.Controller.Events;
using MediaBrowser.Controller.Events.Authentication;
using MediaBrowser.Controller.Events.Updates;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.Scripts.Services;

/// <summary>
/// Hands the events the server publishes through its event bus, rather than as C# events, to the dispatcher:
/// sign ins, account changes, plugin changes, and pending restarts.
/// </summary>
public class ServerEventConsumers :
    IEventConsumer<AuthenticationResultEventArgs>,
    IEventConsumer<AuthenticationRequestEventArgs>,
    IEventConsumer<UserCreatedEventArgs>,
    IEventConsumer<UserDeletedEventArgs>,
    IEventConsumer<UserUpdatedEventArgs>,
    IEventConsumer<UserLockedOutEventArgs>,
    IEventConsumer<UserPasswordChangedEventArgs>,
    IEventConsumer<PluginInstalledEventArgs>,
    IEventConsumer<PluginUpdatedEventArgs>,
    IEventConsumer<PluginUninstalledEventArgs>,
    IEventConsumer<PluginInstallingEventArgs>,
    IEventConsumer<PluginInstallationCancelledEventArgs>,
    IEventConsumer<PendingRestartEventArgs>
{
    private readonly EventContextBuilder _builder;
    private readonly ScriptDispatcher _dispatcher;
    private readonly ILogger<ServerEventConsumers> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="ServerEventConsumers"/> class.
    /// </summary>
    /// <param name="builder">The event context builder.</param>
    /// <param name="dispatcher">The script dispatcher.</param>
    /// <param name="logger">The logger.</param>
    public ServerEventConsumers(EventContextBuilder builder, ScriptDispatcher dispatcher, ILogger<ServerEventConsumers> logger)
    {
        _builder = builder;
        _dispatcher = dispatcher;
        _logger = logger;
    }

    /// <inheritdoc />
    public Task OnEvent(AuthenticationResultEventArgs eventArgs)
        => Handle(ScriptTrigger.AuthenticationSuccess, () => _builder.FromAuthenticationSuccess(eventArgs));

    /// <inheritdoc />
    public Task OnEvent(AuthenticationRequestEventArgs eventArgs)
        => Handle(ScriptTrigger.AuthenticationFailure, () => _builder.FromAuthenticationFailure(eventArgs));

    /// <inheritdoc />
    public Task OnEvent(UserCreatedEventArgs eventArgs)
        => Handle(ScriptTrigger.UserCreated, () => _builder.FromUser(ScriptTrigger.UserCreated, eventArgs.Argument));

    /// <inheritdoc />
    public Task OnEvent(UserDeletedEventArgs eventArgs)
        => Handle(ScriptTrigger.UserDeleted, () => _builder.FromUser(ScriptTrigger.UserDeleted, eventArgs.Argument));

    /// <inheritdoc />
    public Task OnEvent(UserUpdatedEventArgs eventArgs)
        => UserEvents.ShouldDispatch(eventArgs.Argument.Id)
            ? Handle(ScriptTrigger.UserUpdated, () => _builder.FromUser(ScriptTrigger.UserUpdated, eventArgs.Argument))
            : Task.CompletedTask;

    /// <inheritdoc />
    public Task OnEvent(UserLockedOutEventArgs eventArgs)
        => Handle(ScriptTrigger.UserLockedOut, () => _builder.FromUser(ScriptTrigger.UserLockedOut, eventArgs.Argument));

    /// <inheritdoc />
    public Task OnEvent(UserPasswordChangedEventArgs eventArgs)
        => Handle(ScriptTrigger.UserPasswordChanged, () => _builder.FromUser(ScriptTrigger.UserPasswordChanged, eventArgs.Argument));

    /// <inheritdoc />
    public Task OnEvent(PluginInstalledEventArgs eventArgs)
        => Handle(ScriptTrigger.PluginInstalled, () => _builder.FromPlugin(ScriptTrigger.PluginInstalled, eventArgs.Argument.Id, eventArgs.Argument.Name, eventArgs.Argument.Version));

    /// <inheritdoc />
    public Task OnEvent(PluginUpdatedEventArgs eventArgs)
        => Handle(ScriptTrigger.PluginUpdated, () => _builder.FromPlugin(ScriptTrigger.PluginUpdated, eventArgs.Argument.Id, eventArgs.Argument.Name, eventArgs.Argument.Version));

    /// <inheritdoc />
    public Task OnEvent(PluginUninstalledEventArgs eventArgs)
        => Handle(ScriptTrigger.PluginUninstalled, () => _builder.FromPlugin(ScriptTrigger.PluginUninstalled, eventArgs.Argument.Id, eventArgs.Argument.Name, eventArgs.Argument.Version));

    /// <inheritdoc />
    public Task OnEvent(PluginInstallingEventArgs eventArgs)
        => Handle(ScriptTrigger.PluginInstalling, () => _builder.FromPlugin(ScriptTrigger.PluginInstalling, eventArgs.Argument.Id, eventArgs.Argument.Name, eventArgs.Argument.Version));

    /// <inheritdoc />
    public Task OnEvent(PluginInstallationCancelledEventArgs eventArgs)
        => Handle(ScriptTrigger.PluginInstallationCancelled, () => _builder.FromPlugin(ScriptTrigger.PluginInstallationCancelled, eventArgs.Argument.Id, eventArgs.Argument.Name, eventArgs.Argument.Version));

    /// <inheritdoc />
    public Task OnEvent(PendingRestartEventArgs eventArgs)
        => Handle(ScriptTrigger.PendingRestart, () => _builder.FromSystem(ScriptTrigger.PendingRestart));

    private Task Handle(ScriptTrigger trigger, Func<EventContext> build)
    {
        try
        {
            if (ScriptDispatcher.HasScriptsFor(trigger))
            {
                _dispatcher.Dispatch(build());
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Scripts: could not handle {Trigger}", trigger);
        }

        return Task.CompletedTask;
    }
}
