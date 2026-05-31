#include "config-utils/shared/config-utils.hpp"

DECLARE_CONFIG(LiveStreamQuestConfig) {
  CONFIG_VALUE(sendScores, bool, "Automatically send scores to the server",
               true);
  CONFIG_VALUE(
      frequency, int,
      "Frequency (in hz) to send player position updates to the server", 60);
  CONFIG_VALUE(networkTransport, int, "Network transport", 0,
               "0 = TCP, 1 = UDP");
};