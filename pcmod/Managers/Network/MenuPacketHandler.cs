using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using BeatSaverDownloader.Misc;
using BeatSaverSharp;
using BGLib.Polyglot;
using JetBrains.Annotations;
using LiveStreamQuest.Protos;
using SiraUtil.Logging;
using SongCore;
using UnityEngine;
using Zenject;

namespace LiveStreamQuest.Managers.Network;

public class MenuPacketHandler : IDisposable, IInitializable
{
    private const string CustomLevelPrefix = "custom_level_";
    private readonly CancellationTokenSource _cancellationTokenSource = new();

    private readonly BeatSaver _beatSaver;

    private readonly BeatmapLevelsModel _beatmapLevelsModel;

    private readonly MenuTransitionsHelper _menuTransitionsHelper;

#if BS_1_29
    [UsedImplicitly]
    [Inject] 
    private readonly BeatmapCharacteristicCollectionSO _beatmapCharacteristicCollection = null!;
#else
    [UsedImplicitly] [Inject] private readonly BeatmapCharacteristicCollection _beatmapCharacteristicCollection = null!;
#endif

    private readonly PlayerDataModel _playerDataModel;
    private readonly GameplaySetupViewController _gameplaySetupViewController;
    private readonly EnvironmentsListModel _environmentsListModel;


    private readonly NetworkManager _networkManager;

    private readonly SiraLog _siraLog;

    private readonly GlobalStateManager _globalStateManager;
    private readonly LSQMainThreadDispatcher _mainThreadDispatcher;


    // [Inject] readonly LevelSelectionFlowCoordinator _levelSelectionFlow;

    // optional since we can try to find it later
    [Inject(Optional = true)] private PlayerSettingsPanelController? _playerSettingsPanelController;

    [Inject]
    public MenuPacketHandler(BeatSaver beatSaver, BeatmapLevelsModel beatmapLevelsModel,
        MenuTransitionsHelper menuTransitionsHelper, PlayerDataModel playerDataModel,
        GameplaySetupViewController gameplaySetupViewController, EnvironmentsListModel environmentsListModel,
        NetworkManager networkManager, SiraLog siraLog, GlobalStateManager globalStateManager,
        LSQMainThreadDispatcher mainThreadDispatcher)
    {
        _beatSaver = beatSaver;
        _beatmapLevelsModel = beatmapLevelsModel;
        _menuTransitionsHelper = menuTransitionsHelper;
        _playerDataModel = playerDataModel;
        _gameplaySetupViewController = gameplaySetupViewController;
        _environmentsListModel = environmentsListModel;
        _networkManager = networkManager;
        _siraLog = siraLog;
        _globalStateManager = globalStateManager;
        _mainThreadDispatcher = mainThreadDispatcher;
    }


    /// <summary>
    /// Subscribes to Quest protocol events so menu-side start packets can be handled
    /// as soon as the PC mod is initialized.
    /// </summary>
    public void Initialize()
    {
        _playerSettingsPanelController ??= Resources.FindObjectsOfTypeAll<PlayerSettingsPanelController>().First();
        _networkManager.PacketReceivedEvent -= HandlePacket;
        _networkManager.PacketReceivedEvent += HandlePacket;
    }

    public void Dispose()
    {
        _networkManager.PacketReceivedEvent -= HandlePacket;
        _cancellationTokenSource.Cancel();
        _cancellationTokenSource.Dispose();
    }


    /// <summary>
    /// Routes incoming Quest packets to the menu flow.
    /// Currently this handles <see cref="PacketWrapper.PacketOneofCase.StartBeatmap"/>,
    /// which is sent by Quest when a level start has been requested and the PC should
    /// load the same beatmap before the gameplay handshake begins.
    /// </summary>
    public void HandlePacket(PacketWrapper packetWrapper)
    {
#if BS_1_29
        _mainThreadDispatcher.Enqueue(() => HandlePacketMainThread(packetWrapper));
#else
        _mainThreadDispatcher.DispatchOnMainThread(HandlePacketMainThread, packetWrapper);
#endif
    }

    private async void HandlePacketMainThread(PacketWrapper packetWrapper)
    {
        switch (packetWrapper.PacketCase)
        {
            case PacketWrapper.PacketOneofCase.StartBeatmap:
                _siraLog.Info("2. Start beatmap packet received");
                try
                {
                    _globalStateManager.StartingGameFromQuest = true;
                    await StartLevel(packetWrapper, _cancellationTokenSource.Token).ConfigureAwait(true);
                }
                catch (Exception e)
                {
                    _siraLog.Error(e);
                    SendBeatmapStartError(e.Message);
                }

                break;
        }
    }

    /// <summary>
    /// Loads the beatmap requested by Quest.
    /// Called after a <see cref="PacketWrapper.PacketOneofCase.StartBeatmap"/> packet arrives
    /// and the handler has been marshalled onto the main thread.
    /// </summary>
    private async ValueTask StartLevel(PacketWrapper packetWrapper, CancellationToken token = default)
    {
        var id = packetWrapper.StartBeatmap.LevelId;

        var custom = id.StartsWith(CustomLevelPrefix);

        if (custom)
        {
            var hash = id.Substring(CustomLevelPrefix.Length);
            if (!SongDownloader.IsSongDownloaded(hash))
            {
                _siraLog.Info($"Song not downloaded {hash}");
                var beatmap = await _beatSaver.BeatmapByHash(hash, token).ConfigureAwait(true);

                if (beatmap is null)
                {
                    _siraLog.Error($"Beatmap with hash {hash} not found on BeatSaver");
                    SendBeatmapStartError($"Beatmap with hash {hash} not found on BeatSaver");
                    return;
                }

                await SongDownloader.Instance.DownloadSong(beatmap, token)
                    .ConfigureAwait(true);
                token.ThrowIfCancellationRequested();
                // refresh is necessary
                // https://github.com/Top-Cat/BeatSaverDownloader/blob/fd9ed043924c01f6b20b3ec037bf3fa8e032bdf2/BeatSaverDownloader/UI/ViewControllers/DownloadQueue/QueueManager.cs#L47
                Loader.Instance.RefreshSongs(false);
                // now await until FinishLoad is called
                while (!Loader.AreSongsLoaded)
                {
                    await Task.Delay(100, token).ConfigureAwait(true);
                }
            }
        }
#if BS_1_29
          var levelPreview = _beatmapLevelsModel.GetLevelPreviewForLevelId(id);

        if (levelPreview == null)
        {
            SendBeatmapStartError("levelPreview is null");
            // TODO: User error dialog
            return;
        }

        var levelPack = custom ? SongCore.Loader.CustomLevelsPack : _beatmapLevelsModel.GetLevelPackForLevelId(id);

        if (levelPack == null)
        {
            SendBeatmapStartError("levelPack is null");
            // TODO: User error dialog
            return;
        }

        var beatmapResult = await _beatmapLevelsModel.GetBeatmapLevelAsync(id, _cancellationTokenSource.Token)
            .ConfigureAwait(true);

        if (beatmapResult.beatmapLevel == null || beatmapResult.isError)
        {
            SendBeatmapStartError("beatmap level is null");
            // TODO: User error dialog
            return;
        }

        var beatmapCharacteristicSo =
            _beatmapCharacteristicCollection.GetBeatmapCharacteristicBySerializedName(packetWrapper.StartBeatmap
                .Characteristic);

        var beatmapDifficulty = (BeatmapDifficulty)packetWrapper.StartBeatmap.Difficulty;
        var diffBeatmap =
            beatmapResult.beatmapLevel.beatmapLevelData.GetDifficultyBeatmap(beatmapCharacteristicSo,
                beatmapDifficulty);

        if (beatmapCharacteristicSo == null)
        {
            SendBeatmapStartError("beatmapCharacteristicSo is null");
            // TODO: User error dialog
            return;
        }

        if (diffBeatmap == null)
        {
            SendBeatmapStartError("diffBeatmap is null");
            // TODO: User error dialog
            return;
        }

        // TODO: Figure out why this null refs if single player hasn't been opened

        // multiplayerLevelSelectionFlowCoordinator.Setup(x);
        // _soloFreePlayFlowCoordinator.Setup(state);

        _playerDataModel.Load();
        _gameplaySetupViewController.Init();
        _playerSettingsPanelController.SetIsDirty();
        _playerSettingsPanelController.Refresh();

        _menuTransitionsHelper.StartStandardLevel("Solo", diffBeatmap, levelPreview,
            _playerDataModel.playerData.overrideEnvironmentSettings, null,
            _gameplaySetupViewController.gameplayModifiers, //TODO: Fix
            _playerSettingsPanelController.playerSpecificSettings, null, Localization.Get("BUTTON_MENU"), false,
            true,
            null, null, null);
#else

        token.ThrowIfCancellationRequested();
        var beatmapResult = _beatmapLevelsModel.GetBeatmapLevel(id);

        if (beatmapResult == null)
        {
            SendBeatmapStartError($"beatmap level is null {id}");
            // TODO: User error dialog
            return;
        }

        var beatmapCharacteristicSo =
            _beatmapCharacteristicCollection.GetBeatmapCharacteristicBySerializedName(packetWrapper.StartBeatmap
                .Characteristic);

        var beatmapDifficulty = (BeatmapDifficulty)packetWrapper.StartBeatmap.Difficulty;
        var beatmapKey = new BeatmapKey(id, beatmapCharacteristicSo,
            beatmapDifficulty);

        // TODO: Figure out why this null refs if single player hasn't been opened

        // multiplayerLevelSelectionFlowCoordinator.Setup(x);
        // _soloFreePlayFlowCoordinator.Setup(state);

        await _playerDataModel.playerDataFileModel.LoadAsync().ConfigureAwait(true);
        token.ThrowIfCancellationRequested();

        _gameplaySetupViewController.Init();
        if (_playerSettingsPanelController is null)
        {
            _siraLog.Error("PlayerSettingsPanelController is null, cannot refresh player settings");
            SendBeatmapStartError("PlayerSettingsPanelController is null, cannot refresh player settings");
            return;
        }

        _playerSettingsPanelController.SetIsDirty();
        _playerSettingsPanelController.Refresh();

        _siraLog.Info($"Starting level with key {beatmapKey}");
        _menuTransitionsHelper.StartStandardLevel("Solo", in beatmapKey, beatmapResult,
            _playerDataModel.playerData.overrideEnvironmentSettings,
            _playerDataModel.playerData.colorSchemesSettings.GetOverrideColorScheme(),
            _gameplaySetupViewController.colorSchemesSettings.ShouldOverrideLightshowColors(),
            beatmapResult.GetColorScheme(beatmapKey.beatmapCharacteristic, beatmapKey.difficulty),
            _gameplaySetupViewController.gameplayModifiers, //TODO: Fix
            _playerSettingsPanelController.playerSpecificSettings, null, _environmentsListModel,
            Localization.Get("BUTTON_MENU"),
            false,
            true,
            null, null, null, null);
#endif
    }

    /// <summary>
    /// Reports a beatmap loading or setup failure back to Quest through
    /// <see cref="PacketWrapper.PacketOneofCase.StartBeatmapFailure"/>.
    /// Called from the start-level path when the requested beatmap cannot be loaded,
    /// resolved, or initialized.
    /// </summary>
    private void SendBeatmapStartError(string message)
    {
        _siraLog.Error($"Suffered beatmap error {message}");
        _globalStateManager.StartingGameFromQuest = false;
        var packetWrapper = new PacketWrapper
        {
            StartBeatmapFailure = new StartBeatmapFailure
            {
                Error = message
            }
        };

        _siraLog.Error("Sending error packet");
        _networkManager.SendPacket(packetWrapper);
    }
}