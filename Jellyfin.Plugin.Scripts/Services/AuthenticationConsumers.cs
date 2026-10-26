using System;
using System.Threading.Tasks;
using Jellyfin.Plugin.Scripts.Models;
using MediaBrowser.Controller.Events;
using MediaBrowser.Controller.Events.Authentication;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.Scripts.Services;

/// <summary>
/// Hands sign in events to the dispatcher. They are published through the server's event bus rather than
/// as C# events, so they arrive here instead of in <see cref="EventSubscriber"/>.
/// </summary>
public class AuthenticationConsumers : IEventConsumer<AuthenticationResultEventArgs>, IEventConsumer<AuthenticationRequestEventArgs>
{
    private readonly EventContextBuilder _builder;
    private readonly ScriptDispatcher _dispatcher;
    private readonly ILogger<AuthenticationConsumers> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="AuthenticationConsumers"/> class.
    /// </summary>
    /// <param name="builder">The event context builder.</param>
    /// <param name="dispatcher">The script dispatcher.</param>
    /// <param name="logger">The logger.</param>
    public AuthenticationConsumers(EventContextBuilder builder, ScriptDispatcher dispatcher, ILogger<AuthenticationConsumers> logger)
    {
        _builder = builder;
        _dispatcher = dispatcher;
        _logger = logger;
    }

    /// <inheritdoc />
    public Task OnEvent(AuthenticationResultEventArgs eventArgs)
    {
        ArgumentNullException.ThrowIfNull(eventArgs);
        Handle(ScriptTrigger.AuthenticationSuccess, () => _builder.FromAuthenticationSuccess(eventArgs));
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task OnEvent(AuthenticationRequestEventArgs eventArgs)
    {
        ArgumentNullException.ThrowIfNull(eventArgs);
        Handle(ScriptTrigger.AuthenticationFailure, () => _builder.FromAuthenticationFailure(eventArgs));
        return Task.CompletedTask;
    }

    private void Handle(ScriptTrigger trigger, Func<EventContext> build)
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
    }
}
