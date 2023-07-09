using System;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Threading.Tasks;
using Google.Protobuf;
using LiveStreamQuest.Configuration;
using LiveStreamQuest.Managers;
using LiveStreamQuest.Protos;
using Zenject;

namespace LiveStreamQuest.Network
{
    public class NetworkManager : IDisposable, IInitializable
    {
        private readonly Socket _socket;
        private readonly IPEndPoint _endPoint;

        [Inject] private readonly IPacketHandler _packetHandler;

        [Inject]
        public NetworkManager(PluginConfig config)
        {
            const string address = "192.168.13";
            const int port = 3306;
            _endPoint = new IPEndPoint(IPAddress.Parse(address), port);

            _socket = new Socket(
                _endPoint.AddressFamily,
                SocketType.Stream,
                ProtocolType.Tcp
            );
        }

        public void Dispose()
        {
            _socket?.Dispose();
        }

        public async void Initialize()
        {
            await _socket.ConnectAsync(_endPoint);

            _ = Task.Run(async () =>
            {
                using var networkStream = new NetworkStream(_socket, false);

                while (_socket.Connected)
                {
                    await OnReceive(networkStream);
                }
            }).ConfigureAwait(false);
        }

        private async Task OnReceive(NetworkStream stream)
        {
            if (!stream.DataAvailable)
            {
                await Task.Yield();
                return;
            }

            using var binaryReader = new BinaryReader(stream);

            // bad but oh well, C# uses ints
            var len = (int)binaryReader.ReadUInt64();

            var bytes = new byte[len];

            var readCount = 0;
            while (readCount < len)
            {
                readCount = await stream.ReadAsync(bytes, readCount, len - readCount);
            }

            var packetWrapper = PacketWrapper.Parser.ParseFrom(bytes);

            HandlePacket(packetWrapper);
        }

        private void HandlePacket(PacketWrapper packetWrapper)
        {
            _packetHandler.HandlePacket(packetWrapper);
        }

        public void SendPacket(PacketWrapper packetWrapper)
        {
            Task.Run(async () =>
            {
                var byteArray = packetWrapper.ToByteArray();
                await _socket.SendAsync(new ArraySegment<byte>(byteArray), SocketFlags.None);
            });
        }
    }
}