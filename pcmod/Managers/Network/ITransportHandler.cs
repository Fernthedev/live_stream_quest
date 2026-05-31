using System;
using System.Net;
using System.Threading;
using System.Threading.Tasks;

namespace LiveStreamQuest.Managers.Network;

/// <summary>
/// Transport-specific network operations used by <see cref="NetworkManager"/>.
/// TCP handlers own stream framing; UDP handlers own datagram validation.
/// </summary>
public delegate void PacketFrameHandler(byte[] buffer, int offset, int length, CancellationToken token);

/// <summary>
/// Common transport contract for connection lifecycle, receive loops, and framed sends.
/// </summary>
public interface ITransportHandler : IDisposable
{
    /// <summary>
    /// Gets whether the underlying transport is currently connected.
    /// </summary>
    bool Connected { get; }

    /// <summary>
    /// Connects to the remote endpoint using transport-specific socket semantics.
    /// </summary>
    Task ConnectAsync(IPEndPoint endPoint, int timeoutSeconds, CancellationToken token);

    /// <summary>
    /// Disconnects the transport and releases its owned resources.
    /// </summary>
    void Disconnect();

    /// <summary>
    /// Runs the transport-specific receive loop and forwards complete framed packets.
    /// </summary>
    Task ReceiveLoopAsync(CancellationToken token, PacketFrameHandler onFrame);

    /// <summary>
    /// Sends a fully framed packet using the transport-specific write path.
    /// </summary>
    Task SendAsync(byte[] framedPacket, CancellationToken token);
}
