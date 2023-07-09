#include "MainThreadRunner.hpp"
#include "main.hpp"

#include "socket_lib/shared/queue/concurrentqueue.h"

#include <functional>
#include <thread>

DEFINE_TYPE(LiveStreamQuest, MainThreadRunner)

using namespace LiveStreamQuest;

std::thread::id mainThreadId;

static moodycamel::ConcurrentQueue<std::function<void()>> scheduledFunctions;


void scheduleFunction(std::function<void()>&& func) {
    if (mainThreadId == std::this_thread::get_id()) {
        func();
        return;
    }

    scheduledFunctions.enqueue(func);
}

void MainThreadRunner::Update() {
    static moodycamel::ConsumerToken token(scheduledFunctions);

    std::function<void()> func;
    while (scheduledFunctions.try_dequeue(token, func)) {
        if (!func) continue;
        func();
    }
}
