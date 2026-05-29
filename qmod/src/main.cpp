#include "main.hpp"

#include "manager.hpp"

#include "beatsaber-hook/shared/utils/hooking.hpp"

#include "custom-types/shared/coroutine.hpp"
#include "custom-types/shared/delegate.hpp"
#include "custom-types/shared/register.hpp"

#include "UnityEngine/Events/UnityAction_2.hpp"
#include "UnityEngine/GameObject.hpp"
#include "UnityEngine/SceneManagement/LoadSceneMode.hpp"
#include "UnityEngine/SceneManagement/Scene.hpp"
#include "UnityEngine/SceneManagement/SceneManager.hpp"

#include "GlobalNamespace/AudioTimeSyncController.hpp"
#include "GlobalNamespace/BeatmapCharacteristicSO.hpp"
#include "GlobalNamespace/BeatmapDifficulty.hpp"
#include "GlobalNamespace/GameSongController.hpp"
#include "GlobalNamespace/IBeatmapLevelData.hpp"
#include "GlobalNamespace/MenuTransitionsHelper.hpp"
#include "GlobalNamespace/PauseController.hpp"
#include "GlobalNamespace/PlayerTransforms.hpp"

#include "MainThreadRunner.hpp"
#include "PlayerPositionUpdater.hpp"

#include "scotland2/shared/loader.hpp"

using namespace GlobalNamespace;
using namespace UnityEngine;

// Mod info
static modloader::ModInfo modInfo{"LiveStreamQuest", VERSION, 1};

/**
 * High-level flow summary:
 * - When a level is started on Quest we enter `StartWait` and notify PC
 *   to load the same level (`StartBeatmap`). Quest remains paused until
 *   the PC replies with `ReadyUp` and local audio/scene are ready.
 * - The three boolean flags tracked by `Manager` are `waiting`, `pcReady`,
 *   and `questReady`. `tryStartGame()` sends `StartMap` when all are set.
 */

/**
 * Determine whether the Quest should remain paused. Returns true when the
 * Manager is in `waiting` state AND either side is not ready.
 */
bool shouldBePaused() {
  auto manager = Manager::GetInstance();
  bool ready = manager->isPcReady() && manager->isQuestReady();

  return manager->isWaiting() && !ready;
}

/**
 * Coroutine that runs while the PauseController is active to poll the
 * readiness handshake. It yields until `shouldBePaused()` becomes false,
 * then exits. Resume is handled by the audio path (`ResumeSong`).
 */
custom_types::Helpers::Coroutine
updatePauseState(SafePtrUnity<PauseController> self) {
  while (true) {
    if (!self)
      co_return;
    if (!shouldBePaused())
      break;

    co_yield nullptr;
  }

  // Resume
  if (self->_paused == PauseController::PauseState::Paused ||
      self->wantsToPause) {
    self->HandlePauseMenuManagerDidPressContinueButton();
  }
}

MAKE_HOOK_MATCH(
    MenuTransitionsHelper_StartStandardLevel,
    static_cast<void (::GlobalNamespace::MenuTransitionsHelper::*)(
        ::StringW, ::ByRef<::GlobalNamespace::BeatmapKey>,
        ::GlobalNamespace::BeatmapLevel *,
        ::GlobalNamespace::OverrideEnvironmentSettings *,
        ::GlobalNamespace::ColorScheme *, bool,
        ::GlobalNamespace::ColorScheme *,
        ::GlobalNamespace::GameplayModifiers *,
        ::GlobalNamespace::PlayerSpecificSettings *,
        ::GlobalNamespace::PracticeSettings *,
        ::GlobalNamespace::EnvironmentsListModel *, ::StringW, bool, bool,
        ::System::Action *, ::System::Action_1<::Zenject::DiContainer *> *,
        ::System::Action_2<
            ::UnityW<
                ::GlobalNamespace::StandardLevelScenesTransitionSetupDataSO>,
            ::GlobalNamespace::LevelCompletionResults *> *,
        ::System::Action_2<
            ::UnityW<
                ::GlobalNamespace::StandardLevelScenesTransitionSetupDataSO>,
            ::GlobalNamespace::LevelCompletionResults *> *,
        ::System::Nullable_1<
            ::GlobalNamespace::RecordingToolManager_SetupData>)>(
        &::GlobalNamespace::MenuTransitionsHelper::StartStandardLevel),
    void, MenuTransitionsHelper *self, ::StringW gameMode,
    ::ByRef<::GlobalNamespace::BeatmapKey> beatmapKey,
    ::GlobalNamespace::BeatmapLevel *beatmapLevel,
    ::GlobalNamespace::OverrideEnvironmentSettings *overrideEnvironmentSettings,
    ::GlobalNamespace::ColorScheme *playerOverrideColorScheme,
    bool playerOverrideLightshowColors,
    ::GlobalNamespace::ColorScheme *beatmapOverrideColorScheme,
    ::GlobalNamespace::GameplayModifiers *gameplayModifiers,
    ::GlobalNamespace::PlayerSpecificSettings *playerSpecificSettings,
    ::GlobalNamespace::PracticeSettings *practiceSettings,
    ::GlobalNamespace::EnvironmentsListModel *environmentsListModel,
    ::StringW backButtonText, bool useTestNoteCutSoundEffects, bool startPaused,
    ::System::Action *beforeSceneSwitchToGameplayCallback,
    ::System::Action_1<::Zenject::DiContainer *>
        *afterSceneSwitchToGameplayCallback,
    ::System::Action_2<
        ::UnityW<::GlobalNamespace::StandardLevelScenesTransitionSetupDataSO>,
        ::GlobalNamespace::LevelCompletionResults *> *levelFinishedCallback,
    ::System::Action_2<
        ::UnityW<::GlobalNamespace::StandardLevelScenesTransitionSetupDataSO>,
        ::GlobalNamespace::LevelCompletionResults *> *levelRestartedCallback,
    ::System::Nullable_1<::GlobalNamespace::RecordingToolManager_SetupData>
        recordingToolData) {
  // When starting a standard level, put the Manager into waiting mode.
  // `questReady=false` here because the audio/StartSong hook will later
  // set quest readiness when the `AudioTimeSyncController` is ready.
  Manager::GetInstance()->StartWait(0, false);
  MenuTransitionsHelper_StartStandardLevel(
      self, gameMode, beatmapKey, beatmapLevel, overrideEnvironmentSettings,
      playerOverrideColorScheme, playerOverrideLightshowColors,
      beatmapOverrideColorScheme, gameplayModifiers, playerSpecificSettings,
      practiceSettings, environmentsListModel, backButtonText,
      useTestNoteCutSoundEffects, startPaused,
      beforeSceneSwitchToGameplayCallback, afterSceneSwitchToGameplayCallback,
      levelFinishedCallback, levelRestartedCallback, recordingToolData);

  // Start level on PC
  auto levelId = std::string(beatmapKey->levelId);

  std::string characteristicsName(
      beatmapKey->beatmapCharacteristic->serializedName);

  LOG_INFO("Sending level start {}", levelId);
  PacketWrapper packetWrapper;
  auto startBeatmap = packetWrapper.mutable_startbeatmap();
  startBeatmap->set_levelid(std::move(levelId));
  startBeatmap->set_characteristic(characteristicsName);
  startBeatmap->set_difficulty(beatmapKey->difficulty.value__);
  Manager::GetInstance()->GetHandler().sendPacket(packetWrapper);
}

MAKE_HOOK_MATCH(MenuTransitionsHelper_HandleMainGameSceneDidFinish,
                &MenuTransitionsHelper::HandleMainGameSceneDidFinish, void,
                MenuTransitionsHelper *self,
                StandardLevelScenesTransitionSetupDataSO
                    *standardLevelScenesTransitionSetupData,
                LevelCompletionResults *levelCompletionResults) {
  MenuTransitionsHelper_HandleMainGameSceneDidFinish(
      self, standardLevelScenesTransitionSetupData, levelCompletionResults);

  // Exit map
  Manager::GetInstance()->StopWait();

  PacketWrapper packetWrapper;
  packetWrapper.mutable_exitmap();
  Manager::GetInstance()->GetHandler().sendPacket(packetWrapper);
}

MAKE_HOOK_MATCH(AudioTimeSyncController_PauseSong,
                &AudioTimeSyncController::Pause, void,
                AudioTimeSyncController *self) {
  AudioTimeSyncController_PauseSong(self);

  if (Manager::GetInstance()->isWaiting()) {
    return;
  }

  // We entered a pause while playing; start waiting and poll for PC.
  // `questReady=false` because we are paused and will need to resume only
  // when the PC also reports ready.
  Manager::GetInstance()->StartWait(self->songTime, false);
  // Start the coroutine to watch for PC readiness and resume when done.
  auto pauseController =
      UnityEngine::Object::FindObjectOfType<PauseController *>();
  self->StartCoroutine(custom_types::Helpers::CoroutineHelper::New(
      updatePauseState, SafePtrUnity(pauseController)));

  // Notify PC that Quest paused.
  PacketWrapper packetWrapper;
  packetWrapper.mutable_pausemap();
  Manager::GetInstance()->GetHandler().sendPacket(packetWrapper);
}

MAKE_HOOK_MATCH(AudioTimeSyncController_ResumeSong,
                &AudioTimeSyncController::Resume, void,
                AudioTimeSyncController *self) {
  // ResumeSong is the readiness trigger for Quest-side audio.
  // Mark Quest ready first so the Manager can complete the handshake as
  // soon as the PC is also ready.
  Manager::GetInstance()->ReadyQuestUp();
  if (shouldBePaused()) {
    return;
  }

  AudioTimeSyncController_ResumeSong(self);
}

MAKE_HOOK_MATCH(AudioTimeSyncController_StartSong,
                &AudioTimeSyncController::StartSong, void,
                AudioTimeSyncController *self, float songTimeOffset) {
  // When the song starts normally, consider Quest audio ready and attempt
  // to complete the handshake.
  AudioTimeSyncController_StartSong(self, songTimeOffset);
  Manager::GetInstance()->ReadyQuestUp();
}

MAKE_HOOK_MATCH(AudioTimeSyncController_StopSong,
                &AudioTimeSyncController::StopSong, void,
                AudioTimeSyncController *self) {
  AudioTimeSyncController_StopSong(self);

  // Exit map
  PacketWrapper packetWrapper;
  packetWrapper.mutable_exitmap();
  Manager::GetInstance()->GetHandler().sendPacket(packetWrapper);
}
// MAKE_HOOK_MATCH(GameSongController_FailStopSong,
//                 &GameSongController::FailStopSong, void,
//                 GameSongController *self) {
//   GameSongController_FailStopSong(self);

//   // Exit map
//   PacketWrapper packetWrapper;
//   packetWrapper.mutable_exitmap();
//   Manager::GetInstance()->GetHandler().sendPacket(packetWrapper);
// }

MAKE_HOOK_MATCH(PlayerTransforms_Awake, &PlayerTransforms::Awake, void,
                PlayerTransforms *self) {
  PlayerTransforms_Awake(self);

  self->get_gameObject()
      ->AddComponent<LiveStreamQuest::PlayerPositionUpdater *>();
}

MAKE_HOOK_MATCH(PauseController_Start, &PauseController::Start, void,
                PauseController *self) {
  if (shouldBePaused()) {
    self->_initData->startPaused = true;
    self->StartCoroutine(custom_types::Helpers::CoroutineHelper::New(
        updatePauseState, SafePtrUnity(self)));
  }

  PauseController_Start(self);
}

void onSceneLoad(SceneManagement::Scene scene, SceneManagement::LoadSceneMode) {
  static bool loaded;
  if (loaded || !scene.IsValid())
    return;
  loaded = true;

  IL2CPP_CATCH_HANDLER(auto go =
                           UnityEngine::GameObject::New_ctor("LiveStreamQuest");
                       UnityEngine::Object::DontDestroyOnLoad(go);
                       go->AddComponent<LiveStreamQuest::MainThreadRunner *>();)
}

// MAKE_HOOK_MATCH(
//     Scene_Internal_SceneLoaded,
//     &UnityEngine::SceneManagement::SceneManager::Internal_SceneLoaded, void,
//     ::UnityEngine::SceneManagement::Scene scene,
//     ::UnityEngine::SceneManagement::LoadSceneMode mode) {
//   Scene_Internal_SceneLoaded(scene, mode);
//   onSceneLoad(scene, mode);
// }

// Called at the early stages of game loading
extern "C" void setup(CModInfo *info) {
  Paper::Logger::RegisterFileContextId("LiveStreamQuest");
  Paper::Logger::RegisterFileContextId("SocketLib");

  *info = modInfo.to_c();

  LOG_INFO("Completed setup!");
}

// Called later on in the game loading - a good time to install function hooks
extern "C" void load() {
  il2cpp_functions::Init();

  custom_types::Register::AutoRegister();

  Manager::GetInstance()->Init();

  LOG_INFO("Installing hooks...");
  INSTALL_HOOK(LSQLogger, PlayerTransforms_Awake)
  INSTALL_HOOK(LSQLogger, PauseController_Start)
  INSTALL_HOOK(LSQLogger, MenuTransitionsHelper_StartStandardLevel)
  INSTALL_HOOK(LSQLogger, MenuTransitionsHelper_HandleMainGameSceneDidFinish)
  INSTALL_HOOK(LSQLogger, AudioTimeSyncController_StartSong)
  INSTALL_HOOK(LSQLogger, AudioTimeSyncController_ResumeSong)
  INSTALL_HOOK(LSQLogger, AudioTimeSyncController_PauseSong)
  // INSTALL_HOOK(LSQLogger, GameSongController_FailStopSong)
  //   INSTALL_HOOK(LSQLogger, Scene_Internal_SceneLoaded)
  LOG_INFO("Installed all hooks!");

  std::function<void(SceneManagement::Scene scene,
                     SceneManagement::LoadSceneMode)>
      onSceneChanged = onSceneLoad;

  auto delegate = custom_types::MakeDelegate<Events::UnityAction_2<
      SceneManagement::Scene, SceneManagement::LoadSceneMode> *>(
      onSceneChanged);

  SceneManagement::SceneManager::add_sceneLoaded(delegate);
}
