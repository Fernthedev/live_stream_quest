using System;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using SiraUtil.Logging;

namespace LiveStreamQuest.Managers.Network;

/// <summary>
/// TCP implementation of <see cref="ITransportHandler"/>.
/// Uses a stream, so reads may span multiple operations and payloads are reassembled in memory.
/// </summary>
internal sealed class TCPTransportHandler : ITransportHandler
{
    private readonly SiraLog _siraLog;
    private Socket? _socket;
    private NetworkStream? _networkStream;

    /// <summary>
    /// Initializes a TCP transport handler.
    /// </summary>
    public TCPTransportHandler(SiraLog siraLog)
    {
        _siraLog = siraLog;
    }

    public bool Connected => _socket is { Connected: true };

    /// <summary>
    /// Opens a TCP socket and creates the stream wrapper used for framed reads/writes.
    /// </summary>
    public async Task ConnectAsync(IPEndPoint endPoint, int timeoutSeconds, CancellationToken token)
    {
        Disconnect();

        _socket = new Socket(
            endPoint.AddressFamily,
            SocketType.Stream,
            ProtocolType.Tcp
        )
        {
            ReceiveTimeout = timeoutSeconds * 1000,
            SendTimeout = timeoutSeconds * 1000,
            NoDelay = true
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

        // Create a shared NetworkStream for both read and write. Do not take ownership
        // of the socket so disconnect logic can dispose the socket explicitly.
        _networkStream = new NetworkStream(_socket, false);
    }

    /// <summary>
    /// Reads the 4-byte prefix followed by the payload, reassembling the stream into packet frames.
    /// </summary>
    public async Task ReceiveLoopAsync(CancellationToken token, PacketFrameHandler onFrame)
    {
        if (_socket == null) throw new InvalidOperationException("Socket is null");

        var socket = _socket;
        var networkStream = _networkStream;

        if (networkStream == null) throw new InvalidOperationException("NetworkStream is null");

        _siraLog.Info("Receiving");
        try
        {
            while (_socket == socket && socket.Connected)
            {
                token.ThrowIfCancellationRequested();

                // TCP is a byte stream, so we read the fixed-size prefix first and then the payload.
                var prefixBuffer = new byte[FramedPacket.HeaderSize];
                var prefixCursor = new Cursor(prefixBuffer);

                // Read the 4-byte length prefix (network order)
                await prefixCursor.ReadAllFromStream(networkStream, FramedPacket.HeaderSize, token).ConfigureAwait(false);

                var len = FramedPacket.DecodeLength(prefixBuffer);

                if (len < 0)
                {
                    _siraLog.Warn($"Invalid packet length: {len}");
                    return;
                }

                var bytePool = new byte[FramedPacket.HeaderSize + len];
                Buffer.BlockCopy(prefixBuffer, 0, bytePool, 0, FramedPacket.HeaderSize);

                // The payload starts immediately after the prefix and may require multiple stream reads.
                var payloadCursor = new Cursor(bytePool) { Position = FramedPacket.HeaderSize };
                await payloadCursor.ReadAllFromStream(networkStream, len, token).ConfigureAwait(false);

                token.ThrowIfCancellationRequested();

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
    /// Writes a framed packet over the TCP stream.
    /// </summary>
    public async Task SendAsync(byte[] framedPacket, CancellationToken token)
    {
        var networkStream = _networkStream;
        if (networkStream == null) throw new InvalidOperationException("NetworkStream is null");

        // TCP preserves ordering and framing is handled by the caller, so write once and flush.
        await networkStream.WriteAsync(framedPacket, 0, framedPacket.Length, token).ConfigureAwait(false);
        await networkStream.FlushAsync(token).ConfigureAwait(false);
    }

    public void Disconnect()
    {
        var socket = _socket;
        if (socket == null) return;

        _socket = null;

        _networkStream?.Dispose();
        _networkStream = null;

        if (socket.Connected)
        {
            _siraLog.Info("Disconnecting");
            socket.Disconnect(false);
        }

        socket.Dispose();
    }

    public void Dispose()
    {
        Disconnect();
    }
}
