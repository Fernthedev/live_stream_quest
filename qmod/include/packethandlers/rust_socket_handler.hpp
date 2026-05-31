#pragma once

#include "../packethandler.hpp"

#include "bindings.h"

class RustSocketHandler : public PacketHandler {
public:
  explicit RustSocketHandler(
      ReceivePacketFunc onReceivePacket,
      LiveStreamQuestRust::ffi::SocketTransport transport =
          LiveStreamQuestRust::ffi::SocketTransport::TCP);

  // delete move & copy constructors and assignment operators
  RustSocketHandler(RustSocketHandler &&) = delete;
  RustSocketHandler &operator=(RustSocketHandler &&) = delete;
  RustSocketHandler(const RustSocketHandler &) = delete;
  RustSocketHandler &operator=(const RustSocketHandler &) = delete;

  
  ~RustSocketHandler() override;

  void listen(const int port) override;
  void sendPacket(const PacketWrapper &packet) override;
  bool hasConnection() override;
  void scheduleAsync(std::function<void()> &&f) override;

private:
  static void onPacketBytes(const uint8_t *data, uintptr_t len,
                            void *user_data);

  LiveStreamQuestRust::ffi::RustSocketServerBinding *serverSocket;
  LiveStreamQuestRust::ffi::SocketTransport transport;
};