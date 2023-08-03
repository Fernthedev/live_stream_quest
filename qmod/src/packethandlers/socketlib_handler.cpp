#include "packethandlers/socketlib_handler.hpp"

#include "MainThreadRunner.hpp"
#include "main.hpp"
#include <mutex>
#include <vector>

using namespace SocketLib;

void handleLog(LoggerLevel level, std::string_view const tag,
               std::string_view const log) {
  LOG_INFO("[{}] ({}): {}", SocketLib::Logger::loggerLevelToStr(level), tag,
           log);
}

void SocketLibHandler::listen(const int port) {
  SocketHandler &socketHandler = SocketHandler::getCommonSocketHandler();
  socketHandler.getLogger().DebugEnabled = true;

  serverSocket = socketHandler.createServerSocket(port);
  serverSocket->noDelay = true;
  serverSocket->bindAndListen();
  LOG_INFO("Started server");

  ServerSocket &serverSocket = *this->serverSocket;

  // Subscribe to logger
  SocketHandler::getCommonSocketHandler().getLogger().loggerCallback +=
      handleLog;
  serverSocket.connectCallback += {&SocketLibHandler::connectEvent, this};
  serverSocket.listenCallback += {&SocketLibHandler::listenOnEvents, this};
}

void SocketLibHandler::scheduleAsync(std::function<void()> &&f) {
  std::thread([func = std::move(f)]() {
    IL2CPP_CATCH_HANDLER(func();)
  }).detach();
}

bool SocketLibHandler::hasConnection() {
  return !serverSocket->getClients().empty();
}

void SocketLibHandler::connectEvent(Channel &channel, bool connected) {
  LOG_INFO("Connected {} status: {}", channel.clientDescriptor,
           connected ? "connected" : "disconnected");
}

void SocketLibHandler::listenOnEvents(
    Channel &client, SocketLib::ReadOnlyStreamQueue &incomingQueue) {
  // read the bytes
  // if no packet is being parsed, get the first 8 bytes
  // the first 8 bytes are the size frame, which dictate the size of the
  // incoming packet (excluding the frame) then continue reading bytes until the
  // expected size matches the current byte size if excess bytes, loop again

  std::unique_lock lock(this->mutex);

  auto &pendingPacket = channelIncomingQueue[&client];
  if (!pendingPacket.has_value()) {
    auto lenBytes = incomingQueue.dequeueAsVec(8);
    auto len = ntohq(*reinterpret_cast<size_t *>(lenBytes.data()));

    pendingPacket.emplace(len);
  }

  if (incomingQueue.queueSize() < pendingPacket.value())
    return;

  auto packetBytes =
      std::move(incomingQueue.dequeueAsVec(pendingPacket.value()));
  lock.unlock();

  PacketWrapper packet;
  packet.ParseFromArray(packetBytes.data(), packetBytes.size());
  scheduleFunction(
      [this, packet = std::move(packet)]() { onReceivePacket(packet); });
}

void SocketLibHandler::sendPacket(const PacketWrapper &packet) {
  packet.CheckInitialized();
  size_t size = packet.ByteSizeLong();
  // send size header
  // send message with that size
  // construct message size
  Message message(sizeof(size_t) + size);
  auto networkSize = htonq(size); // convert to big endian

  // set size header
  *reinterpret_cast<size_t *>(message.data()) = networkSize;

  packet.SerializeToArray(message.data() + sizeof(size_t), size); // payload

  for (auto const &[id, client] : serverSocket->getClients()) {
    client->queueWrite(message);
    // LOG_INFO("Sending to {} bytes {} {}", id, size, finishedBytes);
  }
}