#pragma once

#include "packethandler.hpp"

#include "packethandlers/socketlib_handler.hpp"
#include "packethandlers/websocket_handler.hpp"

#include <atomic>
#include <sstream>


/**
 * Manager handles the socket packet flow and the handshake state used to
 * coordinate starting/pausing/resuming between Quest (this mod) and a PC
 * client. The handshake uses three boolean flags:
 *  - `waiting`: Quest is currently paused and waiting for PC readiness.
 *  - `pcReady`: PC has signalled it is ready to start/resume.
 *  - `questReady`: Quest (audio/scene) is ready to start/resume.
 *
 * When `waiting && pcReady && questReady` the Manager will send a
 * `StartMap` packet to the PC and clear `waiting` so the Quest will resume.
 */
class Manager {
private:
  void processMessage(const PacketWrapper &packet);

  std::atomic_bool initialized;
  std::atomic_bool pcReady;
  std::atomic_bool questReady;
  std::atomic_bool waiting;
  float initSongTime = 0;

  std::unique_ptr<SocketLibHandler> handler;

  // Called internally
  void readyPCUp();

  void tryStartGame();

public:
  /** Returns true if the PC has signalled ready. If no connection exists
   *  this method returns true (treat no-PC as automatically ready). */
  [[nodiscard]] bool isPcReady() const;

  /** Returns true when Quest audio/scene is ready to start. */
  [[nodiscard]] bool isQuestReady() const;

  /** Returns true when Quest is in waiting (paused) state pending PC. */
  [[nodiscard]] bool isWaiting() const;

  void Init();

  PacketHandler &GetHandler() { return *handler; }

  /**
   * Put the Manager into waiting mode and record the initial song time.
   * If `questReady` is true the Quest side is considered ready (scene/audio
   * loaded) but Manager will remain in `waiting` until the PC signals
   * readiness via `ReadyUp`.
   */
  void StartWait(float songTime, bool questReady);
  void StopWait();

  void ReadyQuestUp();

  static Manager *GetInstance();
};
