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
    private readonly Socket _socket;
    private readonly IPEndPoint _endPoint;
    private readonly PluginConfig _pluginConfig;

    
    [Inject]
    public readonly SignalBus PacketReceivedEvent;
    
    [Inject] private readonly MainThreadDispatcher _mainThreadDispatcher;
    [Inject] private readonly SiraLog _siraLog;

    [Inject]
    public NetworkManager(PluginConfig config)
    {
        _pluginConfig = config;
        _endPoint = new IPEndPoint(IPAddress.Parse(config.Address), config.Port);

        _socket = new Socket(
            _endPoint.AddressFamily,
            SocketType.Stream,
            ProtocolType.Tcp
        );
        _socket.ReceiveTimeout = 30 * 1000;
        _socket.SendTimeout = 30 * 1000;
    }

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
        if (!_socket.Connected) return;

        _siraLog.Info("Disconnecting");
        _socket.Disconnect(false);
    }

    public async Task Connect()
    {
        try
        {
            _siraLog.Info($"Connecting to {_endPoint}");

            await _socket.ConnectAsync(_endPoint).ConfigureAwait(false);

            if (!_socket.Connected)
            {
                _siraLog.Info($"Failed to connect to {_endPoint}");
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
        _siraLog.Info("Receiving");
        try
        {
            using var networkStream = new NetworkStream(_socket, false);

            while (_socket.Connected)
            {
                await OnReceive(networkStream).ConfigureAwait(false);
            }
        }
        catch (Exception e)
        {
            _siraLog.Error(e);
        }

        _siraLog.Info("Stopped Receiving");
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
        
        _mainThreadDispatcher.DispatchOnMainThread(
            (siraLog, handler, wrapper) =>
            {
                try
                {
                    handler.TryFire(wrapper);
                }
                catch (Exception e)
                {
                    siraLog.Error(e);
                }
            },
            _siraLog, PacketReceivedEvent, packetWrapper);
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