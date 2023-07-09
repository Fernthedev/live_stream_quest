#include "packethandlers/socketlib_handler.hpp"

#include "MainThreadRunner.hpp"
#include "main.hpp"

using namespace SocketLib;

void handleLog(LoggerLevel level, std::string_view const tag,
               std::string_view const log) {
  LOG_INFO("[{}] ({}): {}", SocketLib::Logger::loggerLevelToStr(level), tag, log);
}

void SocketLibHandler::listen(const int port) {
    SocketHandler& socketHandler = SocketHandler::getCommonSocketHandler();

    serverSocket = socketHandler.createServerSocket(port);
    serverSocket->bindAndListen();
    LOG_INFO("Started server");

    ServerSocket& serverSocket = *this->serverSocket;

    // Subscribe to logger
    SocketHandler::getCommonSocketHandler().getLogger().loggerCallback +=
        handleLog;
    serverSocket.connectCallback += {&SocketLibHandler::connectEvent, this};
    serverSocket.listenCallback += {&SocketLibHandler::listenOnEvents, this};
}

void SocketLibHandler::scheduleAsync(std::function<void()>&& f) {
    serverSocket->getSocketHandler()->queueWork(std::move(f));
}

bool SocketLibHandler::hasConnection() {
    return !serverSocket->getClients().empty();
}

void SocketLibHandler::connectEvent(Channel& channel, bool connected) {
    LOG_INFO("Connected {} status: {}", channel.clientDescriptor, connected ? "connected" : "disconnected");
    if (!connected)
        channelIncomingQueue.erase(&channel);
    else
        channelIncomingQueue.try_emplace(&channel, 0);
}

void SocketLibHandler::listenOnEvents(Channel& client, const Message& message) {
    // read the bytes
    // if no packet is being parsed, get the first 8 bytes
    // the first 8 bytes are the size frame, which dictate the size of the incoming packet (excluding the frame)
    // then continue reading bytes until the expected size matches the current byte size
    // if excess bytes, loop again

    std::span<const byte> receivedBytes = message;
    auto &pendingPacket = channelIncomingQueue.at(&client);

    // start of a new packet
    if (!pendingPacket.isValid()) {
        // get the first 8 bytes, then cast to size_t
        size_t expectedLength = *reinterpret_cast<size_t const *>(receivedBytes.first(sizeof(size_t)).data());
        expectedLength = ntohq(expectedLength);
        
        // LOG_INFO("Starting packet: is little endian {} {} flipped {} {}", std::endian::native == std::endian::little, expectedLength, ntohq(expectedLength), receivedBytes);

        pendingPacket = {expectedLength};

        auto subspanData = receivedBytes.subspan(sizeof(size_t));
        pendingPacket.insertBytes(subspanData);
        // continue appending to existing packet
    } else {
        pendingPacket.insertBytes(receivedBytes);
    }

    if (pendingPacket.getCurrentLength() < pendingPacket.getExpectedLength()) {
        return;
    }

    auto stream = std::move(pendingPacket.getData()); // avoid copying
    std::span<const byte> const finalMessage = stream;
    auto const packetBytes = (finalMessage).subspan(0, pendingPacket.getExpectedLength());

    if (pendingPacket.getCurrentLength() > pendingPacket.getExpectedLength()) {
        auto excessData = finalMessage.subspan(pendingPacket.getExpectedLength());
        // get the first 8 bytes, then cast to size_t
        size_t expectedLength = *reinterpret_cast<size_t const*>(excessData.data());

        pendingPacket = IncomingPacket(expectedLength); // reset with excess data

        auto excessDataWithoutSize = excessData.subspan(sizeof(size_t));

        // insert excess data, ignoring the size prefix
        pendingPacket.insertBytes(excessDataWithoutSize);
    } else {
        pendingPacket = IncomingPacket(); // reset 
    }

    PacketWrapper packet;
    packet.ParseFromArray(packetBytes.data(), packetBytes.size());
    scheduleFunction([this, packet = std::move(packet)]() {
        onReceivePacket(packet);
    });

    // Parse the next packet as it is ready
    if (pendingPacket.isValid() && pendingPacket.getCurrentLength()  >= pendingPacket.getExpectedLength()) {
        listenOnEvents(client, Message(""));
    }
}

void SocketLibHandler::sendPacket(const PacketWrapper& packet) {
    packet.CheckInitialized();
    size_t size = packet.ByteSizeLong();
    // send size header
    // send message with that size
    // construct message size
    Message message(sizeof(size_t) + size);
    auto networkSize = htonq(size); // convert to big endian

    //set size header
    *reinterpret_cast<size_t*>(message.data()) = networkSize;

    packet.SerializeToArray(message.data() + sizeof(size_t), size); // payload

    for (auto const& [id, client] : serverSocket->getClients()) {
        client->queueWrite(message);
        // LOG_INFO("Sending to {} bytes {} {}", id, size, finishedBytes);
    }
}