#pragma once

#include "custom-types/shared/macros.hpp"

#include "UnityEngine/Camera.hpp"
#include "UnityEngine/MonoBehaviour.hpp"

#include "GlobalNamespace/AudioTimeSyncController.hpp"
#include "GlobalNamespace/BadCutScoringElement.hpp"
#include "GlobalNamespace/GoodCutScoringElement.hpp"
#include "GlobalNamespace/MissScoringElement.hpp"
#include "GlobalNamespace/PlayerTransforms.hpp"
#include "GlobalNamespace/ScoreController.hpp"
#include "GlobalNamespace/ScoringElement.hpp"

#include "beatsaber-hook/shared/utils/typedefs-list.hpp"

// Attached to PlayerTransforms
DECLARE_CLASS_CODEGEN(LiveStreamQuest, PlayerPositionUpdater,
                      UnityEngine::MonoBehaviour) {
  DECLARE_INSTANCE_METHOD(void, Awake);
  DECLARE_INSTANCE_METHOD(void, OnScoreChange,
                          GlobalNamespace::ScoringElement *scoreElement);

  DECLARE_INSTANCE_FIELD(UnityW<GlobalNamespace::PlayerTransforms>, playerTransforms);
  DECLARE_INSTANCE_FIELD(UnityW<GlobalNamespace::ScoreController>, scoreController);
  DECLARE_INSTANCE_FIELD(UnityW<GlobalNamespace::AudioTimeSyncController>,
                         audioTimeSyncController);
};
