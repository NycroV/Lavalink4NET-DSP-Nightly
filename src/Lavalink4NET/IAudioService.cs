namespace Lavalink4NET;

using System;
using System.Threading;
using System.Threading.Tasks;
using Lavalink4NET.Events;
using Lavalink4NET.Events.Players;
using Lavalink4NET.Integrations;
using Lavalink4NET.Players;
using Lavalink4NET.Rest;
using Lavalink4NET.Tracks;

/// <summary>
///     The interface for a lavalink audio provider service.
/// </summary>
public interface IAudioService : IDisposable, IAsyncDisposable
{
    event AsyncEventHandler<TrackEndedEventArgs>? TrackEnded;

    event AsyncEventHandler<TrackExceptionEventArgs>? TrackException;

    event AsyncEventHandler<TrackStartedEventArgs>? TrackStarted;

    event AsyncEventHandler<TrackStuckEventArgs>? TrackStuck;

    event AsyncEventHandler<StatisticsUpdatedEventArgs>? StatisticsUpdated;

    IIntegrationManager Integrations { get; }

    IPlayerManager Players { get; }

    ITrackManager Tracks { get; }

    ILavalinkApiClientProvider ApiClientProvider { get; }

    ValueTask StartAsync(CancellationToken cancellationToken = default);

    ValueTask StopAsync(CancellationToken cancellationToken = default);

    ValueTask WaitForReadyAsync(CancellationToken cancellationToken = default);
}
