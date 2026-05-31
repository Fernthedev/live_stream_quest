using System;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using SiraUtil.Logging;

namespace LiveStreamQuest.Managers.Network;

/// <summary>
/// UDP implementation of <see cref="ITransportHandler"/>.
/// Treats each datagram as an atomic frame, so reads are validated per packet rather than reassembled.
/// </summary>
internal sealed class UDPTransportHandler : ITransportHandler
{
    private const int UdpMaxDatagramSize = 65507;

    private readonly SiraLog _siraLog;
    private Socket? _socket;

    /// <summary>
    /// Initializes a UDP transport handler.
    /// </summary>
    public UDPTransportHandler(SiraLog siraLog)
    {
        _siraLog = siraLog;
    }

    public bool Connected => _socket is { Connected: true };

    /// <summary>
    /// Opens a UDP socket; datagrams remain packet-sized and are not stream-reassembled.
    /// </summary>
    public async Task ConnectAsync(IPEndPoint endPoint, int timeoutSeconds, CancellationToken token)
    {
        Disconnect();

        _socket = new Socket(
            endPoint.AddressFamily,
            SocketType.Dgram,
            ProtocolType.Udp
        )
        {
            ReceiveTimeout = timeoutSeconds * 1000,
            SendTimeout = timeoutSeconds * 1000
        };

        _siraLog.Info($"Connecting to {endPoint}");

        await _socket.ConnectAsync(endPoint).ConfigureAwait(false);

        if (!_socket.Connected)
        {
            _siraLog.Info($"Failed to connect to {endPoint}");
            return;
        }

        token.ThrowIfCancellationRequested();

        _siraLog.Info("Connected successfully");
    }

    /// <summary>
    /// Receives complete UDP datagrams and validates the framed payload length per packet.
    /// </summary>
    public async Task ReceiveLoopAsync(CancellationToken token, PacketFrameHandler onFrame)
    {
        if (_socket == null) throw new InvalidOperationException("Socket is null");

        var socket = _socket;
        var bytePool = new byte[UdpMaxDatagramSize];

        _siraLog.Info("Receiving");
        try
        {
            while (_socket == socket && socket.Connected)
            {
                token.ThrowIfCancellationRequested();

                // UDP preserves datagram boundaries, so one receive call corresponds to one packet frame.
                var read = await socket.ReceiveAsync(new ArraySegment<byte>(bytePool), SocketFlags.None)
                    .ConfigureAwait(false);
                if (read == 0)
                {
                    throw new IOException("Connection was closed!");
                }

                if (read < FramedPacket.HeaderSize)
                {
                    _siraLog.Warn("Received truncated UDP frame, ignoring");
                    continue;
                }

                var len = FramedPacket.DecodeLength(bytePool);

                if (len < 0)
                {
                    _siraLog.Warn($"Invalid UDP packet length: {len}");
                    continue;
                }

                if (read != FramedPacket.HeaderSize + len)
                {
                    _siraLog.Warn($"UDP frame length mismatch: header={len}, datagram={read}");
                    continue;
                }

                onFrame(bytePool, FramedPacket.HeaderSize, len, token);
            }
        }
        catch (Exception e)
        {
            _siraLog.Error(e);
        }

        _siraLog.Info("Stopped Receiving");
    }

    /// <summary>
    /// Sends a framed packet as one UDP datagram.
    /// </summary>
    public async Task SendAsync(byte[] framedPacket, CancellationToken token)
    {
        if (framedPacket.Length > UdpMaxDatagramSize)
        {
            throw new InvalidOperationException($"Packet too large for UDP: {framedPacket.Length} bytes");
        }

        var socket = _socket;
        if (socket == null) throw new InvalidOperationException("Socket is null");

        // UDP send is atomic; the entire framed packet must fit in a single datagram.
        token.ThrowIfCancellationRequested();
        await socket.SendAsync(new ArraySegment<byte>(framedPacket), SocketFlags.None).ConfigureAwait(false);
    }

    public void Disconnect()
    {
        var socket = _socket;
        if (socket == null) return;

        _socket = null;
        socket.Dispose();
    }

    public void Dispose()
    {
        Disconnect();
    }
}
