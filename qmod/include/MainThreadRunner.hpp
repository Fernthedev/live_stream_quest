#pragma once

#include "custom-types/shared/macros.hpp"

#include "UnityEngine/MonoBehaviour.hpp"
#include "System/Collections/Generic/List_1.hpp"

#include "beatsaber-hook/shared/utils/typedefs-list.hpp"

DECLARE_CLASS_CODEGEN(LiveStreamQuest, MainThreadRunner, UnityEngine::MonoBehaviour,
    DECLARE_INSTANCE_METHOD(void, Update);


)
void scheduleFunction(std::function<void()>&& func);