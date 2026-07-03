#pragma once

#include "../packethandler.hpp"

#include "socket_lib/shared/SocketHandler.hpp"
#include <mutex>

class SocketLibHandler : public PacketHandler {
    using PacketSize = uint16_t;
  
    public:
        SocketLibHandler(ReceivePacketFunc onReceivePacket) : PacketHandler(onReceivePacket) { }
        void listen(const int port) override;
        void sendPacket(const PacketWrapper &packet) override;
        bool hasConnection() override;
        void scheduleAsync(std::function<void()> &&f) override;
    private:
        SocketLib::ServerSocket* serverSocket;

        std::mutex mutex;
        /// Map of client channels to their pending packet size (if any)
        std::unordered_map<SocketLib::Channel *, std::optional<PacketSize>> channelIncomingQueue;
        void connectEvent(SocketLib::Channel& channel, bool connected);
        void listenOnEvents(SocketLib::Channel &client,
                            SocketLib::ReadOnlyStreamQueue &incomingQueue);
};