#include "PlayerPositionUpdater.hpp"

#include "manager.hpp"

#include "custom-types/shared/coroutine.hpp"

#include "UnityEngine/WaitForSecondsRealtime.hpp"

DEFINE_TYPE(LiveStreamQuest, PlayerPositionUpdater)

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

    auto *updatePosition = packetWrapper.mutable_updateposition();

    auto playerTransform = self->playerTransforms;

    // Head
    *updatePosition->mutable_headtransform()->mutable_position() =
        toProtoVec3(playerTransform->headPseudoLocalPos);
    *updatePosition->mutable_headtransform()->mutable_rotation() =
        toProtoQuaternion(playerTransform->headPseudoLocalRot);

    // Left hand
    *updatePosition->mutable_lefttransform()->mutable_position() =
        toProtoVec3(playerTransform->leftHandPseudoLocalPos);
    *updatePosition->mutable_lefttransform()->mutable_rotation() =
        toProtoQuaternion(playerTransform->leftHandPseudoLocalRot);

    // Right hand
    *updatePosition->mutable_righttransform()->mutable_position() =
        toProtoVec3(playerTransform->rightHandPseudoLocalPos);
    *updatePosition->mutable_righttransform()->mutable_rotation() =
        toProtoQuaternion(playerTransform->rightHandPseudoLocalRot);

    Manager::GetInstance()->GetHandler().sendPacket(packetWrapper);

    co_yield UnityEngine::WaitForSecondsRealtime::New_ctor(1.0f / 60.0f)
        ->i_IEnumerator();
  }

  // Better safe than sorry
  co_return;
}

void LiveStreamQuest::PlayerPositionUpdater::Awake() {
  this->playerTransforms = GetComponent<GlobalNamespace::PlayerTransforms *>();
  CRASH_UNLESS(this->playerTransforms);

  this->StartCoroutine(
      custom_types::Helpers::CoroutineHelper::New(updatePositionCoro, this));
}
