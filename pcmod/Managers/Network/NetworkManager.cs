using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading.Tasks;
using Google.Protobuf;
using LiveStreamQuest.Configuration;
using LiveStreamQuest.Protos;
using SiraUtil.Logging;
using Zenject;

namespace LiveStreamQuest.Managers.Network;

public class NetworkManager : IDisposable, IInitializable
{
    private Socket? _socket;

    [Inject] private readonly PluginConfig _pluginConfig;


    [Inject] public readonly SignalBus PacketReceivedEvent;

    [Inject] private readonly SiraLog _siraLog;

    public void Initialize()
    {
        _siraLog.Info("Initializing network manager");
        if (!_pluginConfig.ConnectOnStartup) return;


        _ = Connect().ConfigureAwait(false);
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

        if (!socket.Connected) return;

        _siraLog.Info("Disconnecting");
        socket.Disconnect(false);
        socket.Dispose();
    }

    public async Task Connect()
    {
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
    }

    private async Task OnReceiveLoop()
    {
        if (_socket == null) throw new InvalidOperationException("Socket is null");

        var socket = _socket;

        _siraLog.Info("Receiving");
        try
        {
            using var networkStream = new NetworkStream(socket, false);

            while (socket.Connected)
            {
                await OnReceive(networkStream).ConfigureAwait(false);
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

    private async Task ReviveConnection()
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

    private async Task OnReceive(NetworkStream stream)
    {
        if (!stream.DataAvailable)
        {
            await Task.Yield();
            return;
        }

        using var binaryReader = new BinaryReader(stream, new UTF8Encoding(), true);

        // bad but oh well, C# uses ints
        var len = (int)IPAddress.NetworkToHostOrder((long)binaryReader.ReadUInt64());

        var bytes = new byte[len];

        var readCount = 0;
        while (readCount < len)
        {
            readCount = await stream.ReadAsync(bytes, readCount, len - readCount).ConfigureAwait(false);
        }

        var packetWrapper = PacketWrapper.Parser.ParseFrom(bytes);

        HandlePacket(packetWrapper);
    }

    private void HandlePacket(PacketWrapper packetWrapper)
    {
        // Don't bother fire
        if (PacketReceivedEvent.NumSubscribers == 0) return;

        try
        {
            PacketReceivedEvent.TryFire(packetWrapper);
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