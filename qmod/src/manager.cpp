#include "manager.hpp"
#include "MainThreadRunner.hpp"

#include <fmt/ranges.h>

#include "packethandlers/socketlib_handler.hpp"
#include "packethandlers/websocket_handler.hpp"

#include "sombrero/shared/linq.hpp"
#include "sombrero/shared/linq_functional.hpp"

#include "UnityEngine/Transform.hpp"

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
    handler->listen(3306);
    LOG_INFO("Server fully initialized");
}

#pragma region parsing

void Manager::processMessage(const PacketWrapper &packet) {

    auto id = packet.queryresultid();
    LOG_INFO("Processing packet type {}", (int) packet.Packet_case());
    LOG_DEBUG("Packet is {}", packet.DebugString());

    switch (packet.Packet_case()) {
        case PacketWrapper::kReadyUp: {
            readyPCUp();
            break;
        }
        case PacketWrapper::kStartBeatmapFailure: {
            LOG_INFO("Failed to start beatmap! {}", packet.startbeatmapfailure().error());
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
//                getGameObjectComponents(packet.getgameobjectcomponents(), id);
//                break;
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
    if (!waiting) return;
    if (!pcReady || !questReady) return;

    waiting = false;

    PacketWrapper packetWrapper;
    packetWrapper.startmap();
    handler->sendPacket(packetWrapper);
}

void Manager::StartWait() {
    waiting = true;
    pcReady = false;
    questReady = false;
}


void Manager::readyPCUp() {
    pcReady = true;
    tryStartGame();
}


void Manager::ReadyQuestUp() {
    questReady = true;
    tryStartGame();
}


bool Manager::isPcReady() const {
    return pcReady;
}

bool Manager::isQuestReady() const {
    return questReady;
}

bool Manager::isWaiting() const {
    return waiting;
}

#pragma endregion