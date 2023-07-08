#pragma once

#include <cstdint>

#include "beatsaber-hook/shared/utils/logging.hpp"
#include "beatsaber-hook/shared/utils/il2cpp-utils.hpp"
#include "beatsaber-hook/shared/utils/typedefs.h"
#include "beatsaber-hook/shared/utils/typedefs-string.hpp"

#include "paper/shared/logger.hpp"

#include "fmt/format.h"

static inline auto PaperQLogger = Paper::Logger::WithContext<"LiveStreamQuest", false>();

Logger& getLogger();

#define LOG_INFO(...) PaperQLogger.fmtLog<Paper::LogLevel::INF>(__VA_ARGS__)
#define LOG_DEBUG(...) PaperQLogger.fmtLog<Paper::LogLevel::DBG>(__VA_ARGS__)
// #define LOG_DEBUG(...)

std::string_view GetDataPath();

extern std::thread::id mainThreadId;

