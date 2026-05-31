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

std::string base64_encode(const uint8_t *data, size_t length) {
  static const char lookup_table[] = "ABCDEFGHIJKLMNOPQRSTUVWXYZ"
                                     "abcdefghijklmnopqrstuvwxyz"
                                     "0123456789+/";
  std::string out;
  out.reserve(((length + 2) / 3) * 4);

  int val = 0;
  int valb = -6;
  for (size_t i = 0; i < length; ++i) {
    val = (val << 8) + data[i];
    valb += 8;
    while (valb >= 0) {
      out.push_back(lookup_table[(val >> valb) & 0x3F]);
      valb -= 6;
    }
  }

  // Handle padding
  if (valb > -6) {
    out.push_back(lookup_table[((val << 8) >> (valb + 8)) & 0x3F]);
  }
  while (out.size() % 4) {
    out.push_back('=');
  }

  return out;
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
    if (incomingQueue.queueSize() < sizeof(PacketSize))
      return;

    auto lenBytes = incomingQueue.dequeueAsVec(sizeof(PacketSize));
    PacketSize lenNetwork = 0;
    std::memcpy(&lenNetwork, lenBytes.data(), sizeof(PacketSize));
    auto len = ntohl(lenNetwork);

    pendingPacket.emplace(len);
  }

  if (incomingQueue.queueSize() < pendingPacket.value())
    return;

  auto packetBytes =
      std::move(incomingQueue.dequeueAsVec(pendingPacket.value()));
  // reset for next packet
  pendingPacket.reset();
  lock.unlock();

  // log len and bytes as base64 for debugging
  // {
  //   std::vector<uint8_t> frame(packetBytes.size() + sizeof(PacketSize));
  //   // lets frame the packet bytes with the size header for easier debugging
  //   PacketSize networkSize = htonq(packetBytes.size());
  //   *reinterpret_cast<PacketSize *>(frame.data()) = networkSize;
  //   std::copy(packetBytes.begin(), packetBytes.end(),
  //             frame.begin() + sizeof(PacketSize));

  //   std::string packetBase64 =
  //       base64_encode(frame.data(), frame.size());
  //   LOG_DEBUG("Received packet ({} bytes + len): {}", packetBytes.size(),
  //             packetBase64);
  // }

  PacketWrapper packet;
  packet.ParseFromArray(packetBytes.data(), packetBytes.size());

  if (!packet.IsInitialized()) {
    LOG_INFO("Received uninitialized packet: {}", packet.DebugString());
    return;
  }

  scheduleFunction(
      [this, packet = std::move(packet)]() { onReceivePacket(packet); });
}

void SocketLibHandler::sendPacket(const PacketWrapper &packet) {
  packet.CheckInitialized();
  auto size = packet.ByteSizeLong();
  // send size header
  // send message with that size
  // construct message size
  Message message(sizeof(PacketSize) + size);
  PacketSize networkSize =
      htonl(static_cast<PacketSize>(size)); // convert to big endian

  // set size header
  std::memcpy(message.data(), &networkSize, sizeof(PacketSize));

  packet.SerializeWithCachedSizesToArray(message.data() +
                                         sizeof(PacketSize)); // payload

  for (auto const &[id, client] : serverSocket->getClients()) {
    client->queueWrite(message);
    // LOG_INFO("Sending to {} bytes {}", id, size);
  }
}