namespace Lavalink4NET.DSharpPlus;

using System;
using System.Collections.Immutable;
using System.Threading;
using System.Threading.Tasks;
using global::DSharpPlus;
using global::DSharpPlus.AsyncEvents;
using global::DSharpPlus.Entities;
using global::DSharpPlus.EventArgs;
using global::DSharpPlus.Exceptions;
using global::DSharpPlus.Net.Abstractions;
using Lavalink4NET.Clients;
using L4N = Clients.Events;
using Lavalink4NET.Events;
using Microsoft.Extensions.Logging;
using System.Collections.Concurrent;

/// <summary>
/// Wraps a <see cref="DiscordClient"/> instance.
/// </summary>
public sealed class DiscordClientWrapper : IDiscordClientWrapper
{
    /// <inheritdoc/>
    public event AsyncEventHandler<L4N.VoiceServerUpdatedEventArgs>? VoiceServerUpdated;

    /// <inheritdoc/>
    public event AsyncEventHandler<L4N.VoiceStateUpdatedEventArgs>? VoiceStateUpdated;

    private readonly DiscordClient _client; // sharded clients are now also managed by the same DiscordClient type
    private readonly ILogger<DiscordClientWrapper> _logger;
    private readonly TaskCompletionSource<ClientInformation> _readyTaskCompletionSource;

    /// <summary>
    /// Creates a new instance of <see cref="DiscordClientWrapper"/>.
    /// </summary>
    /// <param name="discordClient">The Discord Client to wrap.</param>
    /// <param name="logger">a logger associated with this wrapper.</param>
    public DiscordClientWrapper(DiscordClient discordClient, ILogger<DiscordClientWrapper> logger)
    {
        ArgumentNullException.ThrowIfNull(discordClient);
        ArgumentNullException.ThrowIfNull(logger);

        _client = discordClient;
        _logger = logger;

        _readyTaskCompletionSource = new TaskCompletionSource<ClientInformation>(TaskCreationOptions.RunContinuationsAsynchronously);

        void AddEventHandler(Type eventArgsType, Delegate eventHandler)
        {
            IClientErrorHandler errorHandler = discordClient.GetErrorHandler();
            ConcurrentDictionary<Type, AsyncEvent> events = discordClient.GetEvents();

            Type asyncEventType = typeof(AsyncEvent<,>).MakeGenericType(discordClient.GetType(), eventArgsType);
            AsyncEvent asyncEvent = events.GetOrAdd(eventArgsType, _ => (AsyncEvent)Activator.CreateInstance
            (
                type: asyncEventType,
                args: [errorHandler]
            )!);

            asyncEvent.Register(eventHandler);
        }

        AddEventHandler(typeof(VoiceStateUpdatedEventArgs), new AsyncEventHandler<DiscordClient, VoiceStateUpdatedEventArgs>(OnVoiceStateUpdated));
        AddEventHandler(typeof(VoiceServerUpdatedEventArgs), new AsyncEventHandler<DiscordClient, VoiceServerUpdatedEventArgs>(OnVoiceServerUpdated));
        AddEventHandler(typeof(GuildDownloadCompletedEventArgs), new AsyncEventHandler<DiscordClient, GuildDownloadCompletedEventArgs>(OnGuildDownloadCompleted));
    }

    /// <inheritdoc/>
    /// <exception cref="ObjectDisposedException">thrown if the instance is disposed</exception>
    public async ValueTask<ImmutableArray<ulong>> GetChannelUsersAsync(
        ulong guildId,
        ulong voiceChannelId,
        bool includeBots = false,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        DiscordChannel channel;
        try
        {
            channel = await _client
                .GetChannelAsync(voiceChannelId)
                .ConfigureAwait(false);

            if (channel is null)
            {
                return ImmutableArray<ulong>.Empty;
            }
        }
        catch (DiscordException exception)
        {
            _logger.LogWarning(
                exception, "An error occurred while retrieving the users for voice channel '{VoiceChannelId}' of the guild '{GuildId}'.",
                voiceChannelId, guildId);

            return ImmutableArray<ulong>.Empty;
        }

        var filteredUsers = ImmutableArray.CreateBuilder<ulong>(channel.Users.Count);

        foreach (var member in channel.Users)
        {
            // Always skip the current user.
            // If we're not including bots and the member is a bot, skip them.
            if (!member.IsCurrent || includeBots || !member.IsBot)
            {
                filteredUsers.Add(member.Id);
            }
        }

        return filteredUsers.ToImmutable();
    }

    /// <inheritdoc/>
    /// <exception cref="ObjectDisposedException">thrown if the instance is disposed</exception>
    public async ValueTask SendVoiceUpdateAsync(
        ulong guildId,
        ulong? voiceChannelId,
        bool selfDeaf = false,
        bool selfMute = false,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var payload = new VoiceStateUpdatePayload
        {
            GuildId = guildId,
            ChannelId = voiceChannelId,
            IsSelfMuted = selfMute,
            IsSelfDeafened = selfDeaf,
        };

#pragma warning disable DSP0004 // Type is for evaluation purposes only and is subject to change or removal in future updates. Suppress this diagnostic to proceed.
#pragma warning disable CS0618 // This method should not be used unless you know what you're doing. Instead, look towards the other explicitly implemented methods which come with client-side validation.

        // Jan 23, 2024, OoLunar: We're telling Discord that we're joining a voice channel.
        // At the time of writing, both DSharpPlus.VoiceNext and DSharpPlus.VoiceLink™
        // use this method to send voice state updates.
        await _client
            .SendPayloadAsync(GatewayOpCode.VoiceStateUpdate, payload, guildId)
            .ConfigureAwait(false);

#pragma warning restore DSP0004 // Type is for evaluation purposes only and is subject to change or removal in future updates. Suppress this diagnostic to proceed.
#pragma warning restore CS0618 // This method should not be used unless you know what you're doing. Instead, look towards the other explicitly implemented methods which come with client-side validation.
    }

    /// <inheritdoc/>
    public ValueTask<ClientInformation> WaitForReadyAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return new(_readyTaskCompletionSource.Task.WaitAsync(cancellationToken));
    }

    private async Task OnGuildDownloadCompleted(DiscordClient discordClient, GuildDownloadCompletedEventArgs eventArgs)
    {
        ArgumentNullException.ThrowIfNull(discordClient);
        ArgumentNullException.ThrowIfNull(eventArgs);

        var clientInformation = new ClientInformation(
            Label: "DSharpPlus",
            CurrentUserId: discordClient.CurrentUser.Id,
            ShardCount: await discordClient.GetShardCountAsync());

        _readyTaskCompletionSource.TrySetResult(clientInformation);
    }

    private async Task OnVoiceServerUpdated(DiscordClient discordClient, VoiceServerUpdatedEventArgs voiceServerUpdateEventArgs)
    {
        ArgumentNullException.ThrowIfNull(discordClient);
        ArgumentNullException.ThrowIfNull(voiceServerUpdateEventArgs);

        var server = new VoiceServer(
            Token: voiceServerUpdateEventArgs.VoiceToken,
            Endpoint: voiceServerUpdateEventArgs.Endpoint);

        var eventArgs = new L4N.VoiceServerUpdatedEventArgs(
            guildId: voiceServerUpdateEventArgs.Guild.Id,
            voiceServer: server);

        await VoiceServerUpdated
            .InvokeAsync(this, eventArgs)
            .ConfigureAwait(false);
    }

    private async Task OnVoiceStateUpdated(DiscordClient discordClient, VoiceStateUpdatedEventArgs voiceStateUpdateEventArgs)
    {
        ArgumentNullException.ThrowIfNull(discordClient);
        ArgumentNullException.ThrowIfNull(voiceStateUpdateEventArgs);

        // session id is the same as the resume key so DSharpPlus should be able to give us the
        // session key in either before or after voice state
        var sessionId = voiceStateUpdateEventArgs.Before?.SessionId ?? voiceStateUpdateEventArgs.After.SessionId;

        // create voice state
        var voiceState = new VoiceState(
            VoiceChannelId: voiceStateUpdateEventArgs.After?.Channel?.Id,
            SessionId: sessionId);

        var oldVoiceState = new VoiceState(
            VoiceChannelId: voiceStateUpdateEventArgs.Before?.Channel?.Id,
            SessionId: sessionId);

        // invoke event
        var eventArgs = new L4N.VoiceStateUpdatedEventArgs(
            guildId: voiceStateUpdateEventArgs.Guild.Id,
            userId: voiceStateUpdateEventArgs.User.Id,
            isCurrentUser: voiceStateUpdateEventArgs.User.Id == discordClient.CurrentUser.Id,
            oldVoiceState: oldVoiceState,
            voiceState: voiceState);

        await VoiceStateUpdated
            .InvokeAsync(this, eventArgs)
            .ConfigureAwait(false);
    }
}
