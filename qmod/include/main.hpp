#pragma once

#include <cstdint>

#include "config.hpp"

#include "beatsaber-hook/shared/utils/logging.hpp"
#include "beatsaber-hook/shared/utils/il2cpp-utils.hpp"
#include "beatsaber-hook/shared/utils/typedefs.h"
#include "beatsaber-hook/shared/utils/typedefs-string.hpp"

#include "paper2_scotland2/shared/logger.hpp"



static constexpr auto LSQLogger = Paper::ConstLoggerContext("LiveStreamQuest");


#define LOG_INFO(...) LSQLogger.fmtLog<Paper::LogLevel::INF>(__VA_ARGS__)
#define LOG_DEBUG(...) LSQLogger.fmtLog<Paper::LogLevel::DBG>(__VA_ARGS__)
// #define LOG_DEBUG(...)

std::string_view GetDataPath();

extern std::thread::id mainThreadId;

