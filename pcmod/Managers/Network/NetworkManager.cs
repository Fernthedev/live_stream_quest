using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Threading.Tasks;
using Google.Protobuf;
using LiveStreamQuest.Configuration;
using LiveStreamQuest.Protos;
using LiveStreamQuest.Extensions;
using SiraUtil.Logging;
using Zenject;

namespace LiveStreamQuest.Managers.Network;

public class NetworkManager : IDisposable, IInitializable
{
    private Socket? _socket;

    [Inject] private readonly PluginConfig _pluginConfig;
    public event Action<PacketWrapper>? PacketReceivedEvent;
    public event Action? ConnectStateChanged;

    [Inject] private readonly SiraLog _siraLog;
    public bool Connecting { get; private set; }
    public bool Connected => _socket is { Connected: true };

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
    }


    public void Disconnect()
    {
        var socket = _socket;
        if (socket == null) return;
        // Set to null to mark an intentional disconnect
        _socket = null;

        if (socket.Connected)
        {
            _siraLog.Info("Disconnecting");
            socket.Disconnect(false);
        }

        socket.Dispose();
        ConnectStateChanged?.Invoke();
    }

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

            _siraLog.Info("Connected successfully");

            _ = Task.Run(OnReceiveLoop);
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

    private async ValueTask OnReceiveLoop()
    {
        if (_socket == null) throw new InvalidOperationException("Socket is null");

        var socket = _socket;

        _siraLog.Info("Receiving");
        try
        {
            // What if I was crazy and decided to use some unsafe code here? :smirk:
            using var networkStream = new NetworkStream(socket, false);

            // Reuse byte array and overwrite
            var bytePool = new byte[int.MaxValue];

            while (_socket == socket && socket.Connected)
            {
                await OnReceive(networkStream, bytePool).ConfigureAwait(false);
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

    private async ValueTask OnReceive(NetworkStream stream, byte[] bytePool)
    {
        if (!stream.DataAvailable)
        {
            await Task.Yield();
            await Task.Delay(new TimeSpan(seconds: 0, days: 0, hours: 0, minutes: 0, milliseconds: 2));
            return;
        }

        // must be uint64 to consume 8 bytes
        // bad but oh well, C# uses ints
        var len = (int)IPAddress.NetworkToHostOrder((long)stream.ReadUint64(bytePool));

        var readCount = 0;
        while (readCount < len)
        {
            var tempReadCount = await stream.ReadAsync(bytePool, readCount, len - readCount).ConfigureAwait(false);

            if (tempReadCount == 0)
            {
                throw new IOException("Connection was closed unexpectedly!");
            }

            readCount += tempReadCount;
        }

        var packetWrapper = PacketWrapper.Parser.ParseFrom(bytePool, 0, len);

        // Fire and forget
        _ = Task.Run(() => HandlePacket(packetWrapper)).ConfigureAwait(false);
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
        Task.Run(() =>
        {
            try
            {
                var byteArray = packetWrapper.ToByteArray();
                long len = byteArray.Length;

                var lenBytes = BitConverter.GetBytes(IPAddress.HostToNetworkOrder(len));

                return Task.FromResult(_socket.SendAsync(new List<ArraySegment<byte>>
                    {
                        new(lenBytes),
                        new(byteArray)
                    }, SocketFlags.None)
                    .ConfigureAwait(false));
            }
            catch (Exception e)
            {
                _siraLog.Error(e);
                throw;
            }
        });
    }
}