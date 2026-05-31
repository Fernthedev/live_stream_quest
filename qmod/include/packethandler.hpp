#pragma once

#include "main.hpp"
#include <functional>

#include "protos/live_stream.pb.h"

using ReceivePacketFunc = std::function<void(const PacketWrapper& packet)>;

class PacketHandler {
    public:
        PacketHandler(ReceivePacketFunc onReceivePacket) {
            this->onReceivePacket = onReceivePacket;
        }
        virtual ~PacketHandler() { };
        virtual void listen(const int port) = 0;
        virtual void sendPacket(const PacketWrapper& packet) = 0;
        virtual bool hasConnection() = 0;

        virtual void scheduleAsync(std::function<void()> &&f) = 0;

      protected:
        // Packet handler callback to call when a packet is received, should be
        // set by the constructor of the derived class
        // called on the main thread, so it can safely interact with the game and other main thread only data structures
        ReceivePacketFunc onReceivePacket = nullptr;
};
