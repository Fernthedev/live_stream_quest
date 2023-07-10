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

#include "GlobalNamespace/BeatmapCharacteristicSO.hpp"
#include "GlobalNamespace/BeatmapDifficulty.hpp"
#include "GlobalNamespace/GameSongController.hpp"
#include "GlobalNamespace/IBeatmapLevel.hpp"
#include "GlobalNamespace/IDifficultyBeatmap.hpp"
#include "GlobalNamespace/IDifficultyBeatmapSet.hpp"
#include "GlobalNamespace/IPreviewBeatmapLevel.hpp"
#include "GlobalNamespace/MenuTransitionsHelper.hpp"
#include "GlobalNamespace/PauseController.hpp"
#include "GlobalNamespace/PauseController_InitData.hpp"
#include "GlobalNamespace/PlayerTransforms.hpp"

#include "MainThreadRunner.hpp"
#include "PlayerPositionUpdater.hpp"

using namespace GlobalNamespace;
using namespace UnityEngine;

static ModInfo modInfo; // Stores the ID and version of our mod, and is sent to
                        // the modloader upon startup

// Returns a logger, useful for printing debug messages
Logger &getLoggerOld() {
  static Logger *logger = new Logger(modInfo);
  return *logger;
}

MAKE_HOOK_MATCH(
    MenuTransitionsHelper_StartStandardLevel,
    static_cast<void (MenuTransitionsHelper::*)(
        StringW, IDifficultyBeatmap *, IPreviewBeatmapLevel *,
        OverrideEnvironmentSettings *, ColorScheme *, GameplayModifiers *,
        PlayerSpecificSettings *, PracticeSettings *, StringW, bool, bool,
        System::Action *, System::Action_1<Zenject::DiContainer *> *,
        System::Action_2<StandardLevelScenesTransitionSetupDataSO *,
                         LevelCompletionResults *> *,
        System::Action_2<LevelScenesTransitionSetupDataSO *,
                         LevelCompletionResults *> *)>(
        &MenuTransitionsHelper::StartStandardLevel),
    void, MenuTransitionsHelper *self, StringW f1, IDifficultyBeatmap *f2,
    IPreviewBeatmapLevel *f3, OverrideEnvironmentSettings *f4, ColorScheme *f5,
    GameplayModifiers *f6, PlayerSpecificSettings *f7, PracticeSettings *f8,
    StringW f9, bool f10, bool f11, System::Action *f12,
    System::Action_1<Zenject::DiContainer *> *f13,
    System::Action_2<StandardLevelScenesTransitionSetupDataSO *,
                     LevelCompletionResults *> *f14,
    System::Action_2<LevelScenesTransitionSetupDataSO *,
                     LevelCompletionResults *> *f15) {
  Manager::GetInstance()->StartWait();
  MenuTransitionsHelper_StartStandardLevel(self, f1, f2, f3, f4, f5, f6, f7, f8,
                                           f9, f10, f11, f12, f13, f14, f15);

  // Start level on PC
  auto levelId =
      std::string(f2->get_level()->i_IPreviewBeatmapLevel()->get_levelID());

  std::string characteristicsName(f2->get_parentDifficultyBeatmapSet()
                                      ->get_beatmapCharacteristic()
                                      ->get_serializedName());

  LOG_INFO("Sending level start {}", levelId);
  PacketWrapper packetWrapper;
  packetWrapper.mutable_startbeatmap()->set_levelid(std::move(levelId));
  packetWrapper.mutable_startbeatmap()->set_characteristic(characteristicsName);
  packetWrapper.mutable_startbeatmap()->set_difficulty(f2->get_difficulty());
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
  PacketWrapper packetWrapper;
  packetWrapper.mutable_exitmap();
  Manager::GetInstance()->GetHandler().sendPacket(packetWrapper);
}

MAKE_HOOK_MATCH(GameSongController_StartSong, &GameSongController::StartSong,
                void, GameSongController *self, float songTimeOffset) {
  GameSongController_StartSong(self, songTimeOffset);
  Manager::GetInstance()->ReadyQuestUp();
}

MAKE_HOOK_MATCH(GameSongController_StopSong, &GameSongController::StopSong,
                void, GameSongController *self) {
  GameSongController_StopSong(self);

  // Exit map
  PacketWrapper packetWrapper;
  packetWrapper.mutable_exitmap();
  Manager::GetInstance()->GetHandler().sendPacket(packetWrapper);
}
MAKE_HOOK_MATCH(GameSongController_FailStopSong,
                &GameSongController::FailStopSong, void,
                GameSongController *self) {
  GameSongController_FailStopSong(self);

  // Exit map
  PacketWrapper packetWrapper;
  packetWrapper.mutable_exitmap();
  Manager::GetInstance()->GetHandler().sendPacket(packetWrapper);
}

MAKE_HOOK_MATCH(PlayerTransforms_Awake, &PlayerTransforms::Awake, void,
                PlayerTransforms *self) {
  PlayerTransforms_Awake(self);

  self->get_gameObject()
      ->AddComponent<LiveStreamQuest::PlayerPositionUpdater *>();
}

bool shouldBePaused() {
  auto manager = Manager::GetInstance();
  bool ready = manager->isPcReady() && manager->isQuestReady();

  return manager->isWaiting() && !ready;
}

custom_types::Helpers::Coroutine
updatePositionCoro(SafePtrUnity<PauseController> self) {
  while (true) {
    if (!self)
      co_return;
    if (!shouldBePaused())
      break;

    co_yield nullptr;
  }

  // Resume
  self->HandlePauseMenuManagerDidPressContinueButton();
}

MAKE_HOOK_MATCH(PauseController_Start, &PauseController::Start, void,
                PauseController *self) {
  if (shouldBePaused()) {
    self->initData->startPaused = true;
    self->StartCoroutine(custom_types::Helpers::CoroutineHelper::New(
        updatePositionCoro, SafePtrUnity(self)));
  }

  PauseController_Start(self);
}

MAKE_HOOK_MATCH(PauseController_HandlePauseMenuManagerDidPressContinueButton,
                &PauseController::HandlePauseMenuManagerDidPressContinueButton,
                void, PauseController *self) {
  // Do not resume
  if (shouldBePaused()) {
    return;
  }

  PauseController_HandlePauseMenuManagerDidPressContinueButton(self);
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

MAKE_HOOK_MATCH(
    Scene_Internal_SceneLoaded,
    &UnityEngine::SceneManagement::SceneManager::Internal_SceneLoaded, void,
    ::UnityEngine::SceneManagement::Scene scene,
    ::UnityEngine::SceneManagement::LoadSceneMode mode) {
  Scene_Internal_SceneLoaded(scene, mode);
  onSceneLoad(scene, mode);
}

// Called at the early stages of game loading
extern "C" void setup(ModInfo &info) {
  Paper::Logger::RegisterFileContextId("LiveStreamQuest");
  Paper::Logger::RegisterFileContextId("SocketLib");

  info.id = MOD_ID;
  info.version = VERSION;
  modInfo = info;

  LOG_INFO("Completed setup!");
}

// Called later on in the game loading - a good time to install function hooks
extern "C" void load() {
  il2cpp_functions::Init();

  custom_types::Register::AutoRegister();

  Manager::GetInstance()->Init();

  LOG_INFO("Installing hooks...");
  INSTALL_HOOK(getLoggerOld(), PlayerTransforms_Awake)
  INSTALL_HOOK(getLoggerOld(), PauseController_Start)
  INSTALL_HOOK(getLoggerOld(),
               PauseController_HandlePauseMenuManagerDidPressContinueButton)
  INSTALL_HOOK(getLoggerOld(), MenuTransitionsHelper_StartStandardLevel)
  INSTALL_HOOK(getLoggerOld(),
               MenuTransitionsHelper_HandleMainGameSceneDidFinish)
  INSTALL_HOOK(getLoggerOld(), GameSongController_StartSong)
  // INSTALL_HOOK(getLoggerOld(), GameSongController_StopSong)
  // INSTALL_HOOK(getLoggerOld(), GameSongController_FailStopSong)
  //   INSTALL_HOOK(getLoggerOld(), Scene_Internal_SceneLoaded)
  LOG_INFO("Installed all hooks!");

  std::function<void(SceneManagement::Scene scene,
                     SceneManagement::LoadSceneMode)>
      onSceneChanged = onSceneLoad;

  auto delegate = custom_types::MakeDelegate<Events::UnityAction_2<
      SceneManagement::Scene, SceneManagement::LoadSceneMode> *>(
      onSceneChanged);

  SceneManagement::SceneManager::add_sceneLoaded(delegate);
}