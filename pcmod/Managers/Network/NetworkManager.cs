using System;
using System.Net;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using Google.Protobuf;
using JetBrains.Annotations;
using LiveStreamQuest.Configuration;
using LiveStreamQuest.Protos;
using SiraUtil.Logging;
using Zenject;

namespace LiveStreamQuest.Managers.Network;

/// <summary>
/// Transport-agnostic packet manager. It frames packets and delegates TCP/UDP specifics
/// to the selected <see cref="ITransportHandler"/> implementation.
/// </summary>
[UsedImplicitly]
public class NetworkManager : IDisposable, IInitializable
{
    private readonly PluginConfig _pluginConfig;
    private readonly SiraLog _siraLog;

    public event Action<PacketWrapper>? PacketReceivedEvent;
    public event Action? ConnectStateChanged;

    private ITransportHandler? _transportHandler;
    private Channel<byte[]>? _sendChannel;

    public bool Connecting { get; private set; }
    public bool Connected => _transportHandler is { Connected: true };

    private CancellationTokenSource _cancellationTokenSource = new();

    private NetworkTransport Transport => (NetworkTransport)_pluginConfig.Transport;

    [Inject]
    public NetworkManager(SiraLog siraLog, PluginConfig pluginConfig)
    {
        _siraLog = siraLog;
        _pluginConfig = pluginConfig;
    }

    /// <summary>
    /// Starts the connection flow when startup auto-connect is enabled.
    /// </summary>
    public void Initialize()
    {
        _siraLog.Info("Initializing network manager");
        if (!_pluginConfig.ConnectOnStartup) return;

        _ = Task.Run(() => Connect()).ConfigureAwait(false);
    }

    /// <summary>
    /// Stops the active transport, cancels pending work, and releases manager resources.
    /// </summary>
    public void Dispose()
    {
        _siraLog.Info("Closing network stream");
        Disconnect();
        _cancellationTokenSource.Cancel();
        _cancellationTokenSource.Dispose();
    }

    /// <summary>
    /// Disconnects the current transport and completes the send queue.
    /// </summary>
    public void Disconnect()
    {
        var handler = _transportHandler;
        if (handler == null) return;

        // Set to null to mark an intentional disconnect
        _transportHandler = null;

        // Complete/cleanup sender
        _sendChannel?.Writer.TryComplete();
        _sendChannel = null;

        if (handler.Connected)
        {
            _siraLog.Info("Disconnecting");
            handler.Disconnect();
            CancelAll();
        }

        handler.Dispose();
        ConnectStateChanged?.Invoke();
    }

    private void CancelAll()
    {
        _cancellationTokenSource.Cancel();
        _cancellationTokenSource = new CancellationTokenSource();
    }

    /// <summary>
    /// Connects using the transport selected in configuration.
    /// </summary>
    public async ValueTask Connect(bool cancelExisting = false)
    {
        if (Connecting && !cancelExisting)
        {
            _siraLog.Info("Attempting to connect while an existing attempt is still running");
            return;
        }

        Connecting = true;
        ConnectStateChanged?.Invoke();
        try
        {
            var endPoint = new IPEndPoint(IPAddress.Parse(_pluginConfig.Address), _pluginConfig.Port);
            
            if (_transportHandler != null)
            {
                Disconnect();
            }

            _transportHandler = Transport == NetworkTransport.Udp 
                ? new UDPTransportHandler(_siraLog)
                : new TCPTransportHandler(_siraLog);
            var transportHandler = _transportHandler;
            
            var token = _cancellationTokenSource.Token;
            await transportHandler.ConnectAsync(endPoint, _pluginConfig.ConnectionTimeoutSeconds, token).ConfigureAwait(false);

            if (!transportHandler.Connected)
            {
                _siraLog.Info($"Failed to connect to {endPoint}");
                return;
            }

            token.ThrowIfCancellationRequested();

            _siraLog.Info("Connected successfully");

            // Create per-connection channel and start single-writer sender
            _sendChannel = Channel.CreateUnbounded<byte[]>(new UnboundedChannelOptions { SingleReader = true });
            _ = Task.Run(() => SendLoop(transportHandler, _cancellationTokenSource.Token), token);
            _ = Task.Run(() => ReceiveLoop(transportHandler, _cancellationTokenSource.Token), token);
        }
        catch (Exception e)
        {
            _siraLog.Error(e);
            Disconnect();
        }
        finally
        {
            Connecting = false;
            ConnectStateChanged?.Invoke();
        }
    }
    

    private async Task ReceiveLoop(ITransportHandler handler, CancellationToken token)
    {
        await handler.ReceiveLoopAsync(token, HandleParsedPacket).ConfigureAwait(false);

        if (_transportHandler != null && handler == _transportHandler)
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
            if (_transportHandler is { Connected: true }) break;

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

    private void HandleParsedPacket(byte[] buffer, int offset, int len, CancellationToken token)
    {
        var packetWrapper = PacketWrapper.Parser.ParseFrom(buffer, offset, len);

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

    /// <summary>
    /// Frames a packet once and queues it for the active transport.
    /// </summary>
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

        // Build framed message (4-byte length prefix in network order + payload)
        // ReSharper disable once RedundantCast
        var framed = FramedPacket.Encode(packetWrapper);

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
        _ = channel.Writer.WriteAsync(framed, _cancellationTokenSource.Token).AsTask();
    }

    private async Task SendLoop(ITransportHandler handler, CancellationToken token)
    {
        var ch = _sendChannel;
        if (ch == null) return;

        var reader = ch.Reader;
        try
        {
            // channel loop
            while (await reader.WaitToReadAsync(token).ConfigureAwait(false))
            {
                // keep taking out items
                while (reader.TryRead(out var seg))
                {
                    try
                    {
                        await handler.SendAsync(seg, token).ConfigureAwait(false);
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
