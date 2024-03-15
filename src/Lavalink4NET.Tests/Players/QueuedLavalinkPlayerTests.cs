﻿namespace Lavalink4NET.Tests.Players;

using System;
using System.Collections.Immutable;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Lavalink4NET.Clients;
using Lavalink4NET.Players;
using Lavalink4NET.Players.Queued;
using Lavalink4NET.Protocol.Models;
using Lavalink4NET.Protocol.Models.Filters;
using Lavalink4NET.Protocol.Payloads.Events;
using Lavalink4NET.Protocol.Requests;
using Lavalink4NET.Rest;
using Microsoft.Extensions.Internal;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using Xunit;

public sealed class QueuedLavalinkPlayerTests
{
    [Fact]
    public async Task TestPlayerIsStartedOnSkipAsync()
    {
        // Arrange
        var sessionId = "abc";
        var guildId = 0UL;

        var apiClientMock = new Mock<ILavalinkApiClient>(MockBehavior.Strict);

        var playerModel = new PlayerInformationModel(
            GuildId: guildId,
            CurrentTrack: null,
            Volume: 1F,
            IsPaused: false,
            VoiceState: CreateVoiceState(),
            Filters: new PlayerFilterMapModel());

        var resultModel = playerModel with
        {
            CurrentTrack = new TrackModel(
                Data: "track1",
                Information: CreateDummyTrack(),
                AdditionalInformation: ImmutableDictionary<string, JsonElement>.Empty),
        };

        apiClientMock
            .Setup(x => x.UpdatePlayerAsync(
                sessionId,
                guildId,
                It.IsAny<PlayerUpdateProperties>(),
                It.IsAny<CancellationToken>()))
            .Callback(static (string sessionId, ulong guildId, PlayerUpdateProperties properties, CancellationToken token) =>
            {
                Assert.True(properties.Identifier.IsPresent);
                Assert.Equal("track1", properties.Identifier.Value);
            })
            .ReturnsAsync(resultModel)
            .Verifiable();

        var discordClientMock = new Mock<IDiscordClientWrapper>();

        var sessionProvider = Mock.Of<ILavalinkSessionProvider>(x
            => x.GetSessionAsync(guildId, It.IsAny<CancellationToken>())
            == ValueTask.FromResult(new LavalinkPlayerSession(apiClientMock.Object, "abc", "abc")));

        var playerProperties = new PlayerProperties<QueuedLavalinkPlayer, QueuedLavalinkPlayerOptions>(
            Context: new PlayerContext(
                ServiceProvider: null,
                SessionProvider: sessionProvider,
                DiscordClient: discordClientMock.Object,
                SystemClock: new SystemClock(),
                LifecycleNotifier: null),
            Lifecycle: Mock.Of<IPlayerLifecycle>(),
            ApiClient: apiClientMock.Object,
            InitialTrack: null,
            InitialState: playerModel,
            Label: "Player",
            VoiceChannelId: 0,
            SessionId: sessionId,
            Options: Options.Create(new QueuedLavalinkPlayerOptions()),
            Logger: NullLogger<QueuedLavalinkPlayer>.Instance);

        var player = new QueuedLavalinkPlayer(playerProperties);
        await player.Queue.AddAsync(new TrackQueueItem(new TrackReference("track1")));

        // Act
        await player.SkipAsync().ConfigureAwait(false);

        // Assert
        apiClientMock.Verify();
    }

    [Fact]
    public async Task TestPlayerStartsSecondTrackIfNoneIsPlayingButCountIsTwoOnSkipAsync()
    {
        // Arrange
        var sessionId = "abc";
        var guildId = 0UL;

        var apiClientMock = new Mock<ILavalinkApiClient>(MockBehavior.Strict);

        var playerModel = new PlayerInformationModel(
            GuildId: guildId,
            CurrentTrack: null,
            Volume: 1F,
            IsPaused: false,
            VoiceState: CreateVoiceState(),
            Filters: new PlayerFilterMapModel());

        var resultModel = playerModel with
        {
            CurrentTrack = new TrackModel(
                Data: "track2",
                Information: CreateDummyTrack(),
                AdditionalInformation: ImmutableDictionary<string, JsonElement>.Empty),
        };

        apiClientMock
            .Setup(x => x.UpdatePlayerAsync(
                sessionId,
                guildId,
                It.IsAny<PlayerUpdateProperties>(),
                It.IsAny<CancellationToken>()))
            .Callback(static (string sessionId, ulong guildId, PlayerUpdateProperties properties, CancellationToken token) =>
            {
                Assert.True(properties.Identifier.IsPresent);
                Assert.Equal("track2", properties.Identifier.Value);
            })
            .ReturnsAsync(resultModel)
            .Verifiable();

        var discordClientMock = new Mock<IDiscordClientWrapper>();

        var sessionProvider = Mock.Of<ILavalinkSessionProvider>(x
            => x.GetSessionAsync(guildId, It.IsAny<CancellationToken>())
            == ValueTask.FromResult(new LavalinkPlayerSession(apiClientMock.Object, "abc", "abc")));

        var playerProperties = new PlayerProperties<QueuedLavalinkPlayer, QueuedLavalinkPlayerOptions>(
            Context: new PlayerContext(
                ServiceProvider: null,
                DiscordClient: discordClientMock.Object,
                SystemClock: new SystemClock(),
                SessionProvider: sessionProvider,
                LifecycleNotifier: null),
            Lifecycle: Mock.Of<IPlayerLifecycle>(),
            ApiClient: apiClientMock.Object,
            InitialState: playerModel,
            InitialTrack: null,
            Label: "Player",
            VoiceChannelId: 0,
            SessionId: sessionId,
            Options: Options.Create(new QueuedLavalinkPlayerOptions()),
            Logger: NullLogger<QueuedLavalinkPlayer>.Instance);

        var player = new QueuedLavalinkPlayer(playerProperties);
        await player.Queue.AddAsync(new TrackQueueItem(new TrackReference("track1")));
        await player.Queue.AddAsync(new TrackQueueItem(new TrackReference("track2")));

        // Act
        await player.SkipAsync(count: 2).ConfigureAwait(false);

        // Assert
        apiClientMock.Verify();
    }

    [Fact]
    public async Task TestPlayerStopsOnSkipIfQueueIsEmptyAsync()
    {
        // Arrange
        var sessionId = "abc";
        var guildId = 0UL;

        var apiClientMock = new Mock<ILavalinkApiClient>(MockBehavior.Strict);

        var playerModel = new PlayerInformationModel(
            GuildId: guildId,
            CurrentTrack: null,
            Volume: 1F,
            IsPaused: false,
            VoiceState: CreateVoiceState(),
            Filters: new PlayerFilterMapModel());

        var resultModel = playerModel with
        {
            CurrentTrack = null,
        };

        apiClientMock
            .Setup(x => x.UpdatePlayerAsync(
                sessionId,
                guildId,
                It.IsAny<PlayerUpdateProperties>(),
                It.IsAny<CancellationToken>()))
            .Callback(static (string sessionId, ulong guildId, PlayerUpdateProperties properties, CancellationToken token) =>
            {
                Assert.False(properties.Identifier.IsPresent);
            })
            .ReturnsAsync(resultModel)
            .Verifiable();

        var discordClientMock = new Mock<IDiscordClientWrapper>();

        var sessionProvider = Mock.Of<ILavalinkSessionProvider>(x
            => x.GetSessionAsync(guildId, It.IsAny<CancellationToken>())
            == ValueTask.FromResult(new LavalinkPlayerSession(apiClientMock.Object, "abc", "abc")));

        var playerProperties = new PlayerProperties<QueuedLavalinkPlayer, QueuedLavalinkPlayerOptions>(
            Context: new PlayerContext(
                ServiceProvider: null,
                SessionProvider: sessionProvider,
                DiscordClient: discordClientMock.Object,
                SystemClock: new SystemClock(),
                LifecycleNotifier: null),
            Lifecycle: Mock.Of<IPlayerLifecycle>(),
            ApiClient: apiClientMock.Object,
            InitialState: playerModel,
            InitialTrack: null,
            Label: "Player",
            VoiceChannelId: 0,
            SessionId: sessionId,
            Options: Options.Create(new QueuedLavalinkPlayerOptions()),
            Logger: NullLogger<QueuedLavalinkPlayer>.Instance);

        var player = new QueuedLavalinkPlayer(playerProperties);
        await player.Queue.AddAsync(new TrackQueueItem(new TrackReference("track1")));

        // Act
        await player.SkipAsync(count: 2).ConfigureAwait(false);

        // Assert
        apiClientMock.Verify();
    }

    [Fact]
    public async Task TestPlayerPlaysNextAfterTrackEndAsync()
    {
        // Arrange
        var sessionId = "abc";
        var guildId = 0UL;

        var apiClientMock = new Mock<ILavalinkApiClient>(MockBehavior.Strict);

        var playerModel = new PlayerInformationModel(
            GuildId: guildId,
            CurrentTrack: new TrackModel(
                Data: "track1",
                Information: CreateDummyTrack(),
                AdditionalInformation: ImmutableDictionary<string, JsonElement>.Empty),
            Volume: 1F,
            IsPaused: false,
            VoiceState: CreateVoiceState(),
            Filters: new PlayerFilterMapModel());

        var resultModel = playerModel with
        {
            CurrentTrack = new TrackModel(
                Data: "track2",
                Information: CreateDummyTrack(),
                AdditionalInformation: ImmutableDictionary<string, JsonElement>.Empty),
        };

        apiClientMock
            .Setup(x => x.UpdatePlayerAsync(
                sessionId,
                guildId,
                It.IsAny<PlayerUpdateProperties>(),
                It.IsAny<CancellationToken>()))
            .Callback(static (string sessionId, ulong guildId, PlayerUpdateProperties properties, CancellationToken token) =>
            {
                Assert.True(properties.Identifier.IsPresent);
                Assert.Equal("track2", properties.Identifier.Value);
            })
            .ReturnsAsync(resultModel)
            .Verifiable();

        var discordClientMock = new Mock<IDiscordClientWrapper>();

        var sessionProvider = Mock.Of<ILavalinkSessionProvider>(x
            => x.GetSessionAsync(guildId, It.IsAny<CancellationToken>())
            == ValueTask.FromResult(new LavalinkPlayerSession(apiClientMock.Object, "abc", "abc")));

        var playerProperties = new PlayerProperties<QueuedLavalinkPlayer, QueuedLavalinkPlayerOptions>(
            Context: new PlayerContext(
                ServiceProvider: null,
                SessionProvider: sessionProvider,
                DiscordClient: discordClientMock.Object,
                SystemClock: new SystemClock(),
                LifecycleNotifier: null),
            Lifecycle: Mock.Of<IPlayerLifecycle>(),
            ApiClient: apiClientMock.Object,
            InitialState: playerModel,
            InitialTrack: new TrackQueueItem(new TrackReference(LavalinkApiClient.CreateTrack(playerModel.CurrentTrack!))),
            Label: "Player",
            VoiceChannelId: 0,
            SessionId: sessionId,
            Options: Options.Create(new QueuedLavalinkPlayerOptions()),
            Logger: NullLogger<QueuedLavalinkPlayer>.Instance);

        var player = new QueuedLavalinkPlayer(playerProperties);
        await player.Queue.AddAsync(new TrackQueueItem(new TrackReference("track2")));

        var listener = (ILavalinkPlayerListener)player;

        // Act
        await listener
            .NotifyTrackEndedAsync(player.CurrentTrack!, TrackEndReason.Finished)
            .ConfigureAwait(false);

        // Assert
        apiClientMock.Verify();
    }

    [Fact]
    public async Task TestPlayerRepeatsTrackIfRepeatModeIsTrackAsync()
    {
        // Arrange
        var sessionId = "abc";
        var guildId = 0UL;

        var apiClientMock = new Mock<ILavalinkApiClient>(MockBehavior.Strict);

        var playerModel = new PlayerInformationModel(
            GuildId: guildId,
            CurrentTrack: new TrackModel(
                Data: "QAAAMwMABXZpZGVvAAZhdXRob3IAAAAAAAAnEAAFdmlkZW8AAAAAAAZtYW51YWwAAAAAAAAnEA==",
                Information: CreateDummyTrack(),
                AdditionalInformation: ImmutableDictionary<string, JsonElement>.Empty),
            Volume: 1F,
            IsPaused: false,
            VoiceState: CreateVoiceState(),
            Filters: new PlayerFilterMapModel());

        var resultModel = playerModel with
        {
            CurrentTrack = new TrackModel(
                Data: "track1",
                Information: CreateDummyTrack(),
                AdditionalInformation: ImmutableDictionary<string, JsonElement>.Empty),
        };

        apiClientMock
            .Setup(x => x.UpdatePlayerAsync(
                sessionId,
                guildId,
                It.IsAny<PlayerUpdateProperties>(),
                It.IsAny<CancellationToken>()))
            .Callback(static (string sessionId, ulong guildId, PlayerUpdateProperties properties, CancellationToken token) =>
            {
                // In this case the track data is used because the track has been already resolved
                Assert.True(properties.TrackData.IsPresent);
                Assert.Equal("QAAAMwMABXZpZGVvAAZhdXRob3IAAAAAAAAnEAAFdmlkZW8AAAAAAAZtYW51YWwAAAAAAAAnEA==", properties.TrackData.Value);
            })
            .ReturnsAsync(resultModel)
            .Verifiable();

        var discordClientMock = new Mock<IDiscordClientWrapper>();

        var sessionProvider = Mock.Of<ILavalinkSessionProvider>(x
            => x.GetSessionAsync(guildId, It.IsAny<CancellationToken>())
            == ValueTask.FromResult(new LavalinkPlayerSession(apiClientMock.Object, "abc", "abc")));

        var playerProperties = new PlayerProperties<QueuedLavalinkPlayer, QueuedLavalinkPlayerOptions>(
            Context: new PlayerContext(
                ServiceProvider: null,
                SessionProvider: sessionProvider,
                DiscordClient: discordClientMock.Object,
                SystemClock: new SystemClock(),
                LifecycleNotifier: null),
            Lifecycle: Mock.Of<IPlayerLifecycle>(),
            ApiClient: apiClientMock.Object,
            InitialState: playerModel,
            InitialTrack: new TrackQueueItem(new TrackReference(LavalinkApiClient.CreateTrack(playerModel.CurrentTrack!))),
            Label: "Player",
            VoiceChannelId: 0,
            SessionId: sessionId,
            Options: Options.Create(new QueuedLavalinkPlayerOptions()),
            Logger: NullLogger<QueuedLavalinkPlayer>.Instance);

        var player = new QueuedLavalinkPlayer(playerProperties)
        {
            RepeatMode = TrackRepeatMode.Track
        };

        await player.Queue.AddAsync(new TrackQueueItem(new TrackReference("track2")));

        var listener = (ILavalinkPlayerListener)player;

        // Act
        await listener
            .NotifyTrackEndedAsync(player.CurrentTrack!, TrackEndReason.Finished)
            .ConfigureAwait(false);

        // Assert
        apiClientMock.Verify();
    }

    [Fact]
    public async Task TestPlayerRepeatsTrackIfRepeatModeIsTrackOnSkipAsync()
    {
        // Arrange
        var sessionId = "abc";
        var guildId = 0UL;

        var apiClientMock = new Mock<ILavalinkApiClient>(MockBehavior.Strict);

        var playerModel = new PlayerInformationModel(
            GuildId: guildId,
            CurrentTrack: new TrackModel(
                Data: "QAAAMwMABXZpZGVvAAZhdXRob3IAAAAAAAAnEAAFdmlkZW8AAAAAAAZtYW51YWwAAAAAAAAnEA==",
                Information: CreateDummyTrack(),
                AdditionalInformation: ImmutableDictionary<string, JsonElement>.Empty),
            Volume: 1F,
            IsPaused: false,
            VoiceState: CreateVoiceState(),
            Filters: new PlayerFilterMapModel());

        var resultModel = playerModel with
        {
            CurrentTrack = new TrackModel(
                Data: "QAAAMwMABXZpZGVvAAZhdXRob3IAAAAAAAAnEAAFdmlkZW8AAAAAAAZtYW51YWwAAAAAAAAnEA==",
                Information: CreateDummyTrack(),
                AdditionalInformation: ImmutableDictionary<string, JsonElement>.Empty),
        };

        apiClientMock
            .Setup(x => x.UpdatePlayerAsync(
                sessionId,
                guildId,
                It.IsAny<PlayerUpdateProperties>(),
                It.IsAny<CancellationToken>()))
            .Callback(static (string sessionId, ulong guildId, PlayerUpdateProperties properties, CancellationToken token) =>
            {
                // In this case the track data is used because the track has been already resolved
                Assert.True(properties.TrackData.IsPresent);
                Assert.Equal("QAAAMwMABXZpZGVvAAZhdXRob3IAAAAAAAAnEAAFdmlkZW8AAAAAAAZtYW51YWwAAAAAAAAnEA==", properties.TrackData.Value);
            })
            .ReturnsAsync(resultModel)
            .Verifiable();

        var discordClientMock = new Mock<IDiscordClientWrapper>();

        var sessionProvider = Mock.Of<ILavalinkSessionProvider>(x
            => x.GetSessionAsync(guildId, It.IsAny<CancellationToken>())
            == ValueTask.FromResult(new LavalinkPlayerSession(apiClientMock.Object, "abc", "abc")));

        var playerProperties = new PlayerProperties<QueuedLavalinkPlayer, QueuedLavalinkPlayerOptions>(
            Context: new PlayerContext(
                ServiceProvider: null,
                SessionProvider: sessionProvider,
                DiscordClient: discordClientMock.Object,
                SystemClock: new SystemClock(),
                LifecycleNotifier: null),
            Lifecycle: Mock.Of<IPlayerLifecycle>(),
            ApiClient: apiClientMock.Object,
            InitialState: playerModel,
            InitialTrack: new TrackQueueItem(new TrackReference(LavalinkApiClient.CreateTrack(playerModel.CurrentTrack!))),
            Label: "Player",
            VoiceChannelId: 0,
            SessionId: sessionId,
            Options: Options.Create(new QueuedLavalinkPlayerOptions { RespectTrackRepeatOnSkip = true, }),
            Logger: NullLogger<QueuedLavalinkPlayer>.Instance);

        var player = new QueuedLavalinkPlayer(playerProperties)
        {
            RepeatMode = TrackRepeatMode.Track,
        };

        await player.Queue.AddAsync(new TrackQueueItem(new TrackReference("track2")));

        // Act
        await player
            .SkipAsync()
            .ConfigureAwait(false);

        // Assert
        apiClientMock.Verify();
    }

    [Fact]
    public async Task TestPlayerStopsTrackIfRepeatModeIsTrackButNoTrackIsPlayingOnSkipAsync()
    {
        // Arrange
        var sessionId = "abc";
        var guildId = 0UL;

        var apiClientMock = new Mock<ILavalinkApiClient>(MockBehavior.Strict);

        var playerModel = new PlayerInformationModel(
            GuildId: guildId,
            CurrentTrack: null,
            Volume: 1F,
            IsPaused: false,
            VoiceState: CreateVoiceState(),
            Filters: new PlayerFilterMapModel());

        var resultModel = playerModel with
        {
            CurrentTrack = null,
        };

        apiClientMock
            .Setup(x => x.UpdatePlayerAsync(
                sessionId,
                guildId,
                It.IsAny<PlayerUpdateProperties>(),
                It.IsAny<CancellationToken>()))
            .Callback(static (string sessionId, ulong guildId, PlayerUpdateProperties properties, CancellationToken token) =>
            {
                Assert.True(properties.TrackData.IsPresent);
                Assert.Null(properties.TrackData.Value);
            })
            .ReturnsAsync(resultModel)
            .Verifiable();

        var discordClientMock = new Mock<IDiscordClientWrapper>();

        var sessionProvider = Mock.Of<ILavalinkSessionProvider>(x
            => x.GetSessionAsync(guildId, It.IsAny<CancellationToken>())
            == ValueTask.FromResult(new LavalinkPlayerSession(apiClientMock.Object, "abc", "abc")));

        var playerProperties = new PlayerProperties<QueuedLavalinkPlayer, QueuedLavalinkPlayerOptions>(
            Context: new PlayerContext(
                ServiceProvider: null,
                SessionProvider: sessionProvider,
                DiscordClient: discordClientMock.Object,
                SystemClock: new SystemClock(),
                LifecycleNotifier: null),
            Lifecycle: Mock.Of<IPlayerLifecycle>(),
            ApiClient: apiClientMock.Object,
            InitialState: playerModel,
            InitialTrack: null,
            Label: "Player",
            VoiceChannelId: 0,
            SessionId: sessionId,
            Options: Options.Create(new QueuedLavalinkPlayerOptions()),
            Logger: NullLogger<QueuedLavalinkPlayer>.Instance);

        var player = new QueuedLavalinkPlayer(playerProperties);
        player.RepeatMode = TrackRepeatMode.Track;

        // Act
        await player
            .SkipAsync()
            .ConfigureAwait(false);

        // Assert
        apiClientMock.Verify();
    }

    private static TrackInformationModel CreateDummyTrack()
    {
        return new TrackInformationModel(
            Identifier: "video",
            IsSeekable: true,
            Author: "author",
            Duration: TimeSpan.FromSeconds(10),
            IsLiveStream: false,
            Position: TimeSpan.FromSeconds(10),
            Title: "video",
            Uri: null,
            ArtworkUri: null,
            Isrc: null,
            SourceName: "manual");
    }

    private static VoiceStateModel CreateVoiceState()
    {
        return new VoiceStateModel(
            Token: "abc",
            Endpoint: "server.discord.gg",
            SessionId: "abc");
    }
}
