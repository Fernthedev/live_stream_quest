using System;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading.Tasks;
using Google.Protobuf;
using JetBrains.Annotations;
using LiveStreamQuest.Configuration;
using LiveStreamQuest.Managers;
using LiveStreamQuest.Protos;
using SiraUtil.Logging;
using Zenject;

namespace LiveStreamQuest.Network
{
    public class NetworkManager : IDisposable, IInitializable
    {
        private readonly Socket _socket;
        private readonly IPEndPoint _endPoint;
        private readonly PluginConfig _pluginConfig;


        [Inject(Optional = true)] [CanBeNull] private readonly IPacketHandler _packetHandler;
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
            _siraLog.Info($"Connecting to {_endPoint}");

            await _socket.ConnectAsync(_endPoint).ConfigureAwait(false);

            if (!_socket.Connected)
            {
                _siraLog.Info($"Failed to connect to {_endPoint}");
                return;
            }
            
            _ = Task.Run(async () =>
            {
                using var networkStream = new NetworkStream(_socket, false);

                _siraLog.Info($"Receiving");
                while (_socket.Connected)
                {
                    await OnReceive(networkStream).ConfigureAwait(false);
                }
                _siraLog.Info($"Stopped Receiving");

            }).ConfigureAwait(false);
        }

        private async Task OnReceive(NetworkStream stream)
        {
            if (!stream.DataAvailable)
            {
                await Task.Yield();
                return;
            }
            _siraLog.Info("Received data");

            using var binaryReader = new BinaryReader(stream, new UTF8Encoding(), true);

            // bad but oh well, C# uses ints
            var len = (int)binaryReader.ReadUInt64();

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
            if (_packetHandler == null) return;
            _mainThreadDispatcher.DispatchOnMainThread(
                (handler, wrapper) => { handler.HandlePacket(wrapper); },
                _packetHandler, packetWrapper);
        }

        public void SendPacket(PacketWrapper packetWrapper)
        {
            Task.Run(() =>
            {
                var byteArray = packetWrapper.ToByteArray();
                return Task.FromResult(_socket.SendAsync(new ArraySegment<byte>(byteArray), SocketFlags.None)
                    .ConfigureAwait(false));
            });
        }
    }
}