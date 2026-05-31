#include "packethandlers/rust_socket_handler.hpp"

#include "MainThreadRunner.hpp"
#include "main.hpp"

#include <vector>

using namespace LiveStreamQuestRust::ffi;

RustSocketHandler::RustSocketHandler(ReceivePacketFunc onReceivePacket,
                                     SocketTransport transport)
    : PacketHandler(std::move(onReceivePacket)), serverSocket(nullptr),
      transport(transport) {}

RustSocketHandler::~RustSocketHandler() {
  if (serverSocket != nullptr) {
    rust_socket_server_free(serverSocket);
    serverSocket = nullptr;
  }
}

void RustSocketHandler::listen(const int port) {
  if (serverSocket != nullptr) {
    rust_socket_server_free(serverSocket);
    serverSocket = nullptr;
  }

  serverSocket = rust_socket_server_new(
      port, transport, &RustSocketHandler::onPacketBytes, this);
  if (serverSocket == nullptr) {
    LOG_INFO("Failed to create Rust socket server on port {}", port);
    return;
  }

  if (!rust_socket_server_listen(serverSocket)) {
    LOG_INFO("Failed to start Rust socket server listener on port {}", port);
  }
}

void RustSocketHandler::scheduleAsync(std::function<void()> &&f) {
  std::thread([func = std::move(f)]() {
    IL2CPP_CATCH_HANDLER(func();)
  }).detach();
}

bool RustSocketHandler::hasConnection() {
  if (serverSocket == nullptr) {
    return false;
  }

  return rust_socket_server_has_connection(serverSocket);
}

void RustSocketHandler::sendPacket(const PacketWrapper &packet) {
  if (serverSocket == nullptr) {
    return;
  }

  packet.CheckInitialized();
  const auto size = packet.ByteSizeLong();
  std::vector<uint8_t> bytes(size);
  packet.SerializeWithCachedSizesToArray(bytes.data());

  if (!rust_socket_server_send_packet(serverSocket, bytes.data(), bytes.size(),
                                      false)) {
    LOG_INFO("Failed to send packet through Rust socket server");
  }
}

void RustSocketHandler::onPacketBytes(const uint8_t *data, uintptr_t len,
                                      void *user_data) {
  auto *self = static_cast<RustSocketHandler *>(user_data);
  if (self == nullptr || data == nullptr) {
    return;
  }

  PacketWrapper packet;
  packet.ParseFromArray(data, len);

  if (!packet.IsInitialized()) {
    LOG_INFO("Received uninitialized packet from Rust socket server: {}",
             packet.DebugString());
    return;
  }

  scheduleFunction(
      [self, packet = std::move(packet)]() { self->onReceivePacket(packet); });
}
