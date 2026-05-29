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
 *   to load the same level (`StartBeatmap`). If a PC is connected we start
 *   the Quest side paused, then both sides wait until the handshake finishes.
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
  LSQLogger.info("8. Handshake complete, resuming Quest");
  if (self->_paused == PauseController::PauseState::Paused ||
      self->wantsToPause) {
    self->HandlePauseMenuManagerDidPressContinueButton();
  }
  co_return;
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
  // Called from Beat Saber when Quest starts a standard level.
  // This is the first handshake event: Quest records the pending start,
  // optionally starts paused, and then tells the PC to load the same map.
  LSQLogger.info("1. MenuTransitionsHelper_StartStandardLevel");

  // If `startPaused` is already true, we can skip starting paused here because
  auto shouldStartPaused = Manager::GetInstance()->GetHandler().hasConnection();

  Manager::GetInstance()->StartWait(0, false);
  MenuTransitionsHelper_StartStandardLevel(
      self, gameMode, beatmapKey, beatmapLevel, overrideEnvironmentSettings,
      playerOverrideColorScheme, playerOverrideLightshowColors,
      beatmapOverrideColorScheme, gameplayModifiers, playerSpecificSettings,
      practiceSettings, environmentsListModel, backButtonText,
      useTestNoteCutSoundEffects, shouldStartPaused,
      beforeSceneSwitchToGameplayCallback, afterSceneSwitchToGameplayCallback,
      levelFinishedCallback, levelRestartedCallback, recordingToolData);

  // Start level on PC
  auto levelId = std::string(beatmapKey->levelId);

  std::string characteristicsName(
      beatmapKey->beatmapCharacteristic->serializedName);

  LOG_INFO("2. Sending level start {}", levelId);
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
  // Called when the gameplay scene finishes. This is the cleanup side of the
  // lifecycle, so we clear the wait state and tell the PC to exit too.
  MenuTransitionsHelper_HandleMainGameSceneDidFinish(
      self, standardLevelScenesTransitionSetupData, levelCompletionResults);

  // Exit map
  Manager::GetInstance()->StopWait();

  PacketWrapper packetWrapper;
  packetWrapper.mutable_exitmap();
  Manager::GetInstance()->GetHandler().sendPacket(packetWrapper);
}

// Called when AudioTimeSyncController::Pause fires because Quest paused during
// level load or gameplay. We enter the waiting state and ask the PC to pause
// and respond with ReadyUp.
MAKE_HOOK_MATCH(AudioTimeSyncController_PauseSong,
                &AudioTimeSyncController::Pause, void,
                AudioTimeSyncController *self) {
  AudioTimeSyncController_PauseSong(self);

  if (Manager::GetInstance()->isWaiting()) {
    return;
  }

  LSQLogger.info("3. Audio paused, entering waiting state");
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

// Called when Quest audio resumes. This marks Quest as ready, which lets the
// manager complete the handshake once the PC is also ready.
MAKE_HOOK_MATCH(AudioTimeSyncController_ResumeSong,
                &AudioTimeSyncController::Resume, void,
                AudioTimeSyncController *self) {
  // ResumeSong is the readiness trigger for Quest-side audio.
  // Mark Quest ready first so the Manager can complete the handshake as
  // soon as the PC is also ready.
  LSQLogger.info("5. Audio resumed, marking Quest ready");
  Manager::GetInstance()->ReadyQuestUp();
  if (shouldBePaused()) {
    return;
  }

  AudioTimeSyncController_ResumeSong(self);
}

// Called when the song starts normally instead of resuming from a pause.
// This is the other Quest-ready signal for the handshake.
MAKE_HOOK_MATCH(AudioTimeSyncController_StartSong,
                &AudioTimeSyncController::StartSong, void,
                AudioTimeSyncController *self, float songTimeOffset) {
  // When the song starts normally, consider Quest audio ready and attempt
  // to complete the handshake.
  AudioTimeSyncController_StartSong(self, songTimeOffset);
  LSQLogger.info("5. Song started, marking Quest ready");
  Manager::GetInstance()->ReadyQuestUp();
}

// Called when Quest stops the song. This exits the handshake flow and tells
// the PC to leave the map as well.
MAKE_HOOK_MATCH(AudioTimeSyncController_StopSong,
                &AudioTimeSyncController::StopSong, void,
                AudioTimeSyncController *self) {
  AudioTimeSyncController_StopSong(self);

  // Exit map
  PacketWrapper packetWrapper;
  packetWrapper.mutable_exitmap();
  Manager::GetInstance()->GetHandler().sendPacket(packetWrapper);
}

MAKE_HOOK_MATCH(PlayerTransforms_Awake, &PlayerTransforms::Awake, void,
                PlayerTransforms *self) {
  PlayerTransforms_Awake(self);

  // Called when the player transform object is created in the gameplay scene.
  // Attach the updater here so remote pose packets can drive the avatar.
  self->get_gameObject()
      ->AddComponent<LiveStreamQuest::PlayerPositionUpdater *>();
}

// Called when PauseController starts. If the level enters gameplay while the
// handshake is still incomplete, force the initial paused state and keep
// polling until both sides are ready.
MAKE_HOOK_MATCH(PauseController_Start, &PauseController::Start, void,
                PauseController *self) {
  // Enforce a paused state on Quest map load while the handshake is still
  // pending, even if `startPaused` was not enough to keep the song paused.
  LSQLogger.info("4. PauseController_Start");
  Manager::GetInstance()->ReadyQuestUp();

  if (shouldBePaused()) {
    //  force the paused state and start the coroutine to watch for PC readiness
    self->_initData->startPaused = true;
    self->_wantsToPause = true;

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
  Paper::Logger::RegisterFileContextId(LSQLogger.tag);
  Paper::Logger::RegisterFileContextId("SocketLib");

  getLiveStreamQuestConfig().Init(modInfo);

  *info = modInfo.to_c();

  LOG_INFO("Completed setup!");
}

// Called later on in the game loading - a good time to install function hooks
extern "C" void load() {
  il2cpp_functions::Init();

  custom_types::Register::AutoRegister();

  Manager::GetInstance()->Init();

  LOG_INFO("Installing hooks...");
  // Install hooks in protocol order to make logs and flow easier to follow:
  // 1) Level start
  INSTALL_HOOK(LSQLogger, MenuTransitionsHelper_StartStandardLevel)
  // 2) PauseController enforcement (on scene start)
  INSTALL_HOOK(LSQLogger, PauseController_Start)
  // 2.1) Player transforms (avatar updates)
  INSTALL_HOOK(LSQLogger, PlayerTransforms_Awake)
  // 3) Audio pause -> enter waiting
  INSTALL_HOOK(LSQLogger, AudioTimeSyncController_PauseSong)
  // 4) Song start (quest ready) and resume (quest ready)
  INSTALL_HOOK(LSQLogger, AudioTimeSyncController_StartSong)
  INSTALL_HOOK(LSQLogger, AudioTimeSyncController_ResumeSong)
  // 5) Song stop / exit
  INSTALL_HOOK(LSQLogger, AudioTimeSyncController_StopSong)
  // 6) Gameplay finished (cleanup)
  INSTALL_HOOK(LSQLogger, MenuTransitionsHelper_HandleMainGameSceneDidFinish)

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
