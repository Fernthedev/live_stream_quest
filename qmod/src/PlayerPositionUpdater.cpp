#include "PlayerPositionUpdater.hpp"

#include "manager.hpp"

#include "custom-types/shared/coroutine.hpp"
#include "custom-types/shared/delegate.hpp"

#include "System/Action_2.hpp"

#include "UnityEngine/Resources.hpp"
#include "UnityEngine/Transform.hpp"
#include "UnityEngine/WaitForSecondsRealtime.hpp"

#include <chrono>
#include <functional>

DEFINE_TYPE(LiveStreamQuest, PlayerPositionUpdater);

google::protobuf::Timestamp ConvertToProtobufTimestamp(
    const std::chrono::time_point<std::chrono::system_clock> &timePoint) {
  std::chrono::nanoseconds duration = timePoint.time_since_epoch();
  std::chrono::seconds seconds =
      std::chrono::duration_cast<std::chrono::seconds>(duration);
  std::chrono::nanoseconds fractionalNanos = duration - seconds;

  google::protobuf::Timestamp timestamp;
  timestamp.set_seconds(seconds.count());
  timestamp.set_nanos(static_cast<int32_t>(fractionalNanos.count()));

  return timestamp;
}

inline ::Vector3 toProtoVec3(UnityEngine::Vector3 const &vec) {
  auto protoVec = ::Vector3();
  protoVec.set_x(vec.x);
  protoVec.set_y(vec.y);
  protoVec.set_z(vec.z);

  return protoVec;
}

inline ::Quaternion toProtoQuaternion(UnityEngine::Quaternion const &quat) {
  auto protoQuat = ::Quaternion();
  protoQuat.set_x(quat.x);
  protoQuat.set_y(quat.y);
  protoQuat.set_z(quat.z);
  protoQuat.set_w(quat.w);

  return protoQuat;
}

custom_types::Helpers::Coroutine
updatePositionCoro(LiveStreamQuest::PlayerPositionUpdater *self) {
  PacketWrapper packetWrapper;
  size_t packetId = 0;
  while (true) {
    packetWrapper.set_queryresultid(packetId);
    packetId++;

    auto &updatePosition = *packetWrapper.mutable_updateposition();

    // Song Time
    if (self->audioTimeSyncController) {
      updatePosition.set_songtime(self->audioTimeSyncController->songTime);
    }

    auto playerTransform = self->playerTransforms;

    // Head
    *updatePosition.mutable_headtransform()->mutable_position() =
        toProtoVec3(playerTransform->headPseudoLocalPos);
    *updatePosition.mutable_headtransform()->mutable_rotation() =
        toProtoQuaternion(playerTransform->headPseudoLocalRot);

    // Left hand
    *updatePosition.mutable_lefttransform()->mutable_position() =
        toProtoVec3(playerTransform->leftHandPseudoLocalPos);
    *updatePosition.mutable_lefttransform()->mutable_rotation() =
        toProtoQuaternion(playerTransform->leftHandPseudoLocalRot);

    // Right hand
    *updatePosition.mutable_righttransform()->mutable_position() =
        toProtoVec3(playerTransform->rightHandPseudoLocalPos);
    *updatePosition.mutable_righttransform()->mutable_rotation() =
        toProtoQuaternion(playerTransform->rightHandPseudoLocalRot);

    auto current_time = std::chrono::system_clock::now(); //.time_since_epoch();

    *updatePosition.mutable_time() = ConvertToProtobufTimestamp(current_time);

    Manager::GetInstance()->GetHandler().sendPacket(packetWrapper);

    co_yield UnityEngine::WaitForSecondsRealtime::New_ctor(1.0f / 60.0f)
        ->i_IEnumerator();
  }

  // Better safe than sorry
  co_return;
}

void LiveStreamQuest::PlayerPositionUpdater::OnScoreChange(
    GlobalNamespace::ScoringElement *element) {
  if (!element)
    return;

  PacketWrapper packetWrapper;
  packetWrapper.set_queryresultid(-1);

  auto &scoreUpdate = *packetWrapper.mutable_scoreupdate();
  scoreUpdate.set_totalscore(scoreController->modifiedScore);
  scoreUpdate.set_combo(scoreController->scoreMultiplierCounter->multiplier);

  auto current_time = std::chrono::system_clock::now(); //.time_since_epoch()
  *scoreUpdate.mutable_time() = ConvertToProtobufTimestamp(current_time);

  auto isBad =
      il2cpp_utils::AssignableFrom<GlobalNamespace::BadCutScoringElement>(
          element->klass) ||
      il2cpp_utils::AssignableFrom<GlobalNamespace::MissScoringElement>(
          element->klass);
  
  // bad score
  if (isBad) {
    scoreUpdate.set_ismiss(true);
    // good score
  } else if (auto goodScore =
                 il2cpp_utils::try_cast<GlobalNamespace::GoodCutScoringElement>(
                     element)
                     .value_or(nullptr)) {
    scoreUpdate.set_score(goodScore->get_cutScore());
  }
}
void LiveStreamQuest::PlayerPositionUpdater::Awake() {
  this->playerTransforms = GetComponent<GlobalNamespace::PlayerTransforms *>();
  this->scoreController = UnityEngine::Resources::FindObjectsOfTypeAll<
                              GlobalNamespace::ScoreController *>()
                              .FirstOrDefault();
  this->audioTimeSyncController =
      UnityEngine::Resources::FindObjectsOfTypeAll<
          GlobalNamespace::AudioTimeSyncController *>()
          .FirstOrDefault();
  CRASH_UNLESS(this->playerTransforms);

  // on score event
  std::function<void(GlobalNamespace::ScoringElement * element)> onScore =
      [this](GlobalNamespace::ScoringElement *scoringElement) constexpr {
        return this->OnScoreChange(scoringElement);
      };
  this->scoreController->add_scoringForNoteFinishedEvent(
      custom_types::MakeDelegate<
          System::Action_1<GlobalNamespace::ScoringElement *> *>(onScore));

  this->StartCoroutine(
      custom_types::Helpers::CoroutineHelper::New(updatePositionCoro, this));
}
