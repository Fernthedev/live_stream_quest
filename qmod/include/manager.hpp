#pragma once

#include "packethandler.hpp"

#include "packethandlers/socketlib_handler.hpp"
#include "packethandlers/websocket_handler.hpp"

#include <sstream>

class Manager {
private:
    void processMessage(const PacketWrapper &packet);


    bool initialized;
    bool pcReady;
    bool questReady;
    bool waiting;

    std::unique_ptr<SocketLibHandler> handler;

    // Called internally
    void readyPCUp();

    void tryStartGame();

public:
    [[nodiscard]] bool isPcReady() const;

    [[nodiscard]] bool isQuestReady() const;

    [[nodiscard]] bool isWaiting() const;

    void Init();

    PacketHandler &GetHandler() {
        return *handler;
    }

    void StartWait();

    void ReadyQuestUp();

    static Manager *GetInstance();
};
