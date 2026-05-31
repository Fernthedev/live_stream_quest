using System;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using Google.Protobuf;
using System.Threading.Channels;
using JetBrains.Annotations;
using LiveStreamQuest.Configuration;
using LiveStreamQuest.Protos;
using SiraUtil.Logging;
using Zenject;

namespace LiveStreamQuest.Managers.Network;

// typealias PacketSize -> int
using PacketSize = uint;

[UsedImplicitly]
public class NetworkManager : IDisposable, IInitializable
{
    private readonly PluginConfig _pluginConfig;
    private readonly SiraLog _siraLog;

    public event Action<PacketWrapper>? PacketReceivedEvent;
    public event Action? ConnectStateChanged;

    private Socket? _socket;
    private NetworkStream? _networkStream;
    private Channel<byte[]>? _sendChannel;

    public bool Connecting { get; private set; }
    public bool Connected => _socket is { Connected: true };

    private CancellationTokenSource _cancellationTokenSource = new();

    [Inject]
    public NetworkManager(SiraLog siraLog, PluginConfig pluginConfig)
    {
        _siraLog = siraLog;
        _pluginConfig = pluginConfig;
    }

    public void Initialize()
    {
        _siraLog.Info("Initializing network manager");
        if (!_pluginConfig.ConnectOnStartup) return;


        _ = Task.Run(() => Connect()).ConfigureAwait(false);
    }

    public void Dispose()
    {
        _siraLog.Info("Closing network stream");
        Disconnect();
        _socket?.Dispose();
        _cancellationTokenSource.Cancel();
        _cancellationTokenSource.Dispose();
    }


    public void Disconnect()
    {
        var socket = _socket;
        if (socket == null) return;
        // Set to null to mark an intentional disconnect
        _socket = null;

        // Dispose the shared network stream if present
        _networkStream?.Dispose();
        _networkStream = null;

        // Complete/cleanup sender
        _sendChannel?.Writer.TryComplete();
        _sendChannel = null;

        if (socket.Connected)
        {
            _siraLog.Info("Disconnecting");
            socket.Disconnect(false);
            CancelAll();
        }

        socket.Dispose();
        ConnectStateChanged?.Invoke();
    }

    private void CancelAll()
    {
        _cancellationTokenSource.Cancel();
        _cancellationTokenSource = new CancellationTokenSource();
    }

    public async ValueTask Connect(bool cancelExisting = false)
    {
        if (Connecting && !cancelExisting)
        {
            _siraLog.Info("Attempting to connect while an existing attempt is still running");
            return;
        }

        var token = _cancellationTokenSource.Token;

        Connecting = true;
        ConnectStateChanged?.Invoke();
        try
        {
            var endPoint = new IPEndPoint(IPAddress.Parse(_pluginConfig.Address), _pluginConfig.Port);

            if (_socket != null)
            {
                Disconnect();
            }

            _socket = new Socket(
                endPoint.AddressFamily,
                SocketType.Stream,
                ProtocolType.Tcp
            )
            {
                ReceiveTimeout = _pluginConfig.ConnectionTimeoutSeconds * 1000,
                SendTimeout = _pluginConfig.ConnectionTimeoutSeconds * 1000,
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

            // Create per-connection channel and start single-writer sender
            _sendChannel = Channel.CreateUnbounded<byte[]>(new UnboundedChannelOptions { SingleReader = true });
            _ = Task.Run(() => SendLoop(_cancellationTokenSource.Token), token);
            _ = Task.Run(() => OnReceiveLoop(_cancellationTokenSource.Token), token);
        }
        catch (Exception e)
        {
            _siraLog.Error(e);
        }
        finally
        {
            Connecting = false;
            ConnectStateChanged?.Invoke();
        }
    }

    private async ValueTask OnReceiveLoop(CancellationToken token)
    {
        if (_socket == null) throw new InvalidOperationException("Socket is null");

        var socket = _socket;

        _siraLog.Info("Receiving");
        try
        {
            // Use the shared network stream created at connect time.
            var networkStream = _networkStream;

            if (networkStream == null) throw new InvalidOperationException("NetworkStream is null");

            // Reuse byte array and overwrite
            var bytePool = new byte[int.MaxValue];

            while (_socket == socket && socket.Connected)
            {
                token.ThrowIfCancellationRequested();
                await OnReceive(networkStream, bytePool, token).ConfigureAwait(false);
            }
        }
        catch (Exception e)
        {
            _siraLog.Error(e);
        }

        _siraLog.Info("Stopped Receiving");

        if (_socket != null && socket == _socket)
        {
            await ReviveConnection();
        }
    }

    private async ValueTask ReviveConnection()
    {
        _siraLog.Info("Attempting to reconnect");

        for (var i = 0; i < _pluginConfig.ReconnectionAttempts; i++)
        {
            // Break loop
            if (_socket is { Connected: true }) break;

            if (i > 2)
            {
                // Wait before reconnecting
                await Task.Delay(TimeSpan.FromSeconds(i + 5));
            }

            try
            {
                await Connect().ConfigureAwait(false);
            }
            catch (Exception e)
            {
                _siraLog.Error($"Caught exception, retrying {i}th time");
                _siraLog.Error(e);
            }
        }
    }

    private async ValueTask OnReceive(Stream stream, byte[] bytePool, CancellationToken token)
    {
        // Use Cursor to manage read offsets and make intent clearer.
        var cursor = new Cursor(bytePool);

        // Read the 4-byte length prefix (network order)
        await cursor.ReadAllFromStream(stream, sizeof(PacketSize), token).ConfigureAwait(false);
        
        // TODO: This does not work with UInt32
        var lenNetworkOrder = BitConverter.ToInt32(bytePool, 0);
        var len = IPAddress.NetworkToHostOrder(lenNetworkOrder);

        if (len < 0)
        {
            _siraLog.Warn($"Invalid packet length: {len}");
            return;
        }

        cursor.ResetPosition();
        // Read the payload immediately after the prefix.
        await cursor.ReadAllFromStream(stream, len, token).ConfigureAwait(false);

        token.ThrowIfCancellationRequested();

        var packetWrapper = PacketWrapper.Parser.ParseFrom(bytePool, 0, len);

        if (!packetWrapper.IsInitialized())
        {
            _siraLog.Warn("Received uninitialized packet, ignoring: " + packetWrapper);
            return;
        }
        
        if (packetWrapper.PacketCase == PacketWrapper.PacketOneofCase.None)
        {
            _siraLog.Warn("Received empty packet, ignoring");
            return;
        }

        // Fire and forget
        _ = Task.Run(() => HandlePacket(packetWrapper), token).ConfigureAwait(false);
    }


    private void HandlePacket(PacketWrapper packetWrapper)
    {
        // Don't bother fire
        try
        {
            PacketReceivedEvent?.Invoke(packetWrapper);
        }
        catch (Exception e)
        {
            _siraLog.Error(e);
        }

        // _mainThreadDispatcher.DispatchOnMainThread(
        //     (siraLog, handler, wrapper) =>
        //     {
        //         try
        //         {
        //             handler.TryFire(wrapper);
        //         }
        //         catch (Exception e)
        //         {
        //             siraLog.Error(e);
        //         }
        //     },
        //     _siraLog, PacketReceivedEvent, packetWrapper);
    }

    public void SendPacket(PacketWrapper packetWrapper)
    {
        if (packetWrapper.PacketCase == PacketWrapper.PacketOneofCase.None)
        {
            throw new InvalidOperationException("Cannot send empty packet");
        }

        if (!packetWrapper.IsInitialized()) 
        {
            throw new InvalidOperationException("Cannot send uninitialized packet: " + packetWrapper);
        }

        var token = _cancellationTokenSource.Token;

        // Build framed message (4-byte length prefix in network order + payload)
        // ReSharper disable once RedundantCast
        var packetLen = packetWrapper.CalculateSize();
        var lenBytes = BitConverter.GetBytes(IPAddress.HostToNetworkOrder(packetLen));
        var framed = new byte[sizeof(PacketSize) + packetLen];
        Buffer.BlockCopy(lenBytes, 0, framed, 0, sizeof(PacketSize));
        packetWrapper.WriteTo(new Span<byte>(framed, sizeof(PacketSize), framed.Length - sizeof(PacketSize)));

        // log framed base64
        // var base64Frame = Convert.ToBase64String(framed);
        // _siraLog.Info(
        //     $"Sending packet: {packetWrapper.PacketCase}, framed length: {framed.Length}, base64: {base64Frame}");
        
        var channel = _sendChannel;
        if (channel == null)
        {
            // disconnected
            throw new InvalidOperationException("Not connected");
        }

        // Enqueue the complete frame so the length prefix and payload stay atomic.
        _ = channel.Writer.WriteAsync(framed, token).AsTask();
    }

    private async Task SendLoop(CancellationToken token)
    {
        var ch = _sendChannel;
        if (ch == null) return;
        
        var ns = _networkStream;
        if (ns == null) return;

        var reader = ch.Reader;
        try
        {
            // channel loop
            while (await reader.WaitToReadAsync(token).ConfigureAwait(false))
            {
                // keep taking out items
                while (reader.TryRead(out var seg))
                {
                    if (!ns.CanWrite) break;
                    
                    try
                    {
                        await ns.WriteAsync(seg, 0, seg.Length, token).ConfigureAwait(false);
                        await ns.FlushAsync(token).ConfigureAwait(false);
                    }
                    catch (OperationCanceledException)
                    {
                        // shutdown
                    }
                    catch (Exception e)
                    {
                        _siraLog.Error(e);
                        // on write failure, attempt to cancel connection
                        CancelAll();
                    }
                }
            }
        }
        catch (OperationCanceledException) { }
        catch (Exception e)
        {
            _siraLog.Error(e);
        }
    }
}
