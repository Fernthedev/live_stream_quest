#include "manager.hpp"

#include <fmt/ranges.h>

#include "main.hpp"
#include "packethandlers/socketlib_handler.hpp"
#include "packethandlers/websocket_handler.hpp"

#define MESSAGE_LOGGING

using namespace UnityEngine;

Manager *Manager::GetInstance() {
  static Manager Instance = Manager();
  return &Instance;
}

void Manager::Init() {
  initialized = true;
  LOG_INFO("Starting server at port 3306");
  handler = std::make_unique<SocketLibHandler>((ReceivePacketFunc)[this](
      auto &&PH1) { processMessage(std::forward<decltype(PH1)>(PH1)); });
  handler->listen(9542);
  LOG_INFO("Server fully initialized");
}

#pragma region parsing

void Manager::processMessage(const PacketWrapper &packet) {

  auto id = packet.queryresultid();
  LOG_INFO("Processing packet type {}", (int)packet.Packet_case());
  LOG_DEBUG("Packet is {}", packet.DebugString());

  // Dispatch incoming packets to local handlers. Important packets that
  // affect handshake state are `ReadyUp` (PC -> Quest) and
  // `StartBeatmapFailure` (PC -> Quest).
  switch (packet.Packet_case()) {
  case PacketWrapper::kReadyUp: {
    readyPCUp();
    break;
  }
  case PacketWrapper::kStartBeatmapFailure: {
    LOG_INFO("Failed to start beatmap! {}",
             packet.startbeatmapfailure().error());
    // TODO: User notification
    break;
  }
    //            case PacketWrapper::
    //            case PacketWrapper::kInvokeMethod:
    //                invokeMethod(packet.invokemethod(), id);
    //                break;
    //            case PacketWrapper::kSetField:
    //                setField(packet.setfield(), id);
    //                break;
    //            case PacketWrapper::kGetField:
    //                getField(packet.getfield(), id);
    //                break;
    //            case PacketWrapper::kSearchObjects:
    //                searchObjects(packet.searchobjects(), id);
    //                break;
    //            case PacketWrapper::kGetAllGameObjects:
    //                getAllGameObjects(packet.getallgameobjects(), id);
    //                break;
    //            case PacketWrapper::kGetGameObjectComponents:
    //                getGameObjectComponents(packet.getgameobjectcomponents(),
    //                id); break;
    //            case PacketWrapper::kReadMemory:
    //                readMemory(packet.readmemory(), id);
    //                break;
    //            case PacketWrapper::kWriteMemory:
    //                writeMemory(packet.writememory(), id);
    //                break;
    //            case PacketWrapper::kGetClassDetails:
    //                getClassDetails(packet.getclassdetails(), id);
    //                break;
    //            case PacketWrapper::kGetInstanceClass:
    //                getInstanceClass(packet.getinstanceclass(), id);
    //                break;
    //            case PacketWrapper::kGetInstanceValues:
    //                getInstanceValues(packet.getinstancevalues(), id);
    //                break;
    //            case PacketWrapper::kGetInstanceDetails:
    //                getInstanceDetails(packet.getinstancedetails(), id);
    //                break;
    //            case PacketWrapper::kCreateGameObject:
    //                createGameObject(packet.creategameobject(), id);
    //                break;
    //            case PacketWrapper::kAddSafePtrAddress:
    //                addSafePtrAddress(packet.addsafeptraddress(), id);
    //                break;
    //            case PacketWrapper::kGetSafePtrAddresses:
    //                sendSafePtrList(id);
    //                break;
    //            case PacketWrapper::kRequestLogger:
    //                setLoggerListener(packet.requestlogger(), id);
    //                break;
  default:
    LOG_INFO("Invalid packet type!");
  }
}

void Manager::tryStartGame() {
  // Only attempt to start when in waiting mode and both sides report ready.
  if (!waiting)
    return;
  LSQLogger.info("6. Attempting to start game: pcReady={}, questReady={}",
                 pcReady.load(), questReady.load());
  if (!pcReady.load() || !questReady.load())
    return;

  // Clear waiting and send the authoritative StartMap (resume) to PC.
  waiting = false;

  PacketWrapper packetWrapper;
  packetWrapper.mutable_startmap()->set_songtime(initSongTime);
  handler->sendPacket(packetWrapper);
}

void Manager::StartWait(float songTime, bool questReady) {
  // Record the song time and whether Quest is already ready. `questReady`
  // should be true if the audio/scene has loaded and Quest can resume when
  // PC reports ready. Setting `waiting=true` ensures local pauses remain
  // until the handshake completes.
  this->initSongTime = songTime;
  this->questReady = questReady;

  LSQLogger.info("3. Entering waiting state: songTime={}, questReady={}",
                 songTime, questReady);

  if (waiting)
    return;

  // Enter waiting state and reset PC readiness.
  waiting = true;
  pcReady = false;
}
void Manager::StopWait() {
  waiting = false;
  pcReady = false;
  questReady = false;
}

void Manager::readyPCUp() {
  // Called when PC sends ReadyUp; set the flag and attempt to complete the
  // handshake (tryStartGame will only proceed if `questReady` is also set).
  LSQLogger.info("5. Received ReadyUp from PC");
  pcReady = true;
  tryStartGame();
}

void Manager::ReadyQuestUp() {
  // Called when Quest audio/scene reports it is ready (or player resumes).
  LSQLogger.info("5. Quest is ready");
  questReady = true;
  tryStartGame();
}

bool Manager::isPcReady() const {
  // No PCs mean ready
  if (!handler->hasConnection())
    return true;

  return pcReady;
}

bool Manager::isQuestReady() const { return questReady; }

bool Manager::isWaiting() const { return waiting; }

#pragma endregion