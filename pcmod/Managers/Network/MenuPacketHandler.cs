using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using BeatSaverDownloader.Misc;
using BeatSaverSharp;
using BGLib.Polyglot;
using LiveStreamQuest.Protos;
using SiraUtil.Logging;
using UnityEngine;
using Zenject;

namespace LiveStreamQuest.Managers.Network;

public class MenuPacketHandler : IDisposable, IInitializable
{
    private const string CustomLevelPrefix = "custom_level_";
    private readonly CancellationTokenSource _cancellationTokenSource = new();

    [Inject] private readonly BeatSaver _beatSaver;

    [Inject] private readonly BeatmapLevelsModel _beatmapLevelsModel;

    [Inject] private readonly MenuTransitionsHelper _menuTransitionsHelper;

#if BS_1_29
    [Inject] private readonly BeatmapCharacteristicCollectionSO _beatmapCharacteristicCollection;
#else
    [Inject] private readonly BeatmapCharacteristicCollection _beatmapCharacteristicCollection;
#endif

    [Inject] private readonly PlayerDataModel _playerDataModel;
    [Inject] private readonly GameplaySetupViewController _gameplaySetupViewController;
    [Inject] private readonly EnvironmentsListModel _environmentsListModel;


    [Inject] private readonly NetworkManager _networkManager;

    [Inject] private readonly SiraLog _siraLog;

    [Inject] private readonly GlobalStateManager _globalStateManager;
    [Inject] private readonly LSQMainThreadDispatcher _mainThreadDispatcher;


    // [Inject] readonly LevelSelectionFlowCoordinator _levelSelectionFlow;
    [Inject(Optional = true)] private PlayerSettingsPanelController _playerSettingsPanelController = null!;


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
                    await StartLevel(packetWrapper).ConfigureAwait(true);
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
    private async ValueTask StartLevel(PacketWrapper packetWrapper)
    {
        var id = packetWrapper.StartBeatmap.LevelId;

        var custom = id.StartsWith(CustomLevelPrefix);

        if (custom)
        {
            var hash = id.Substring(CustomLevelPrefix.Length);
            if (!SongDownloader.IsSongDownloaded(hash))
            {
                _siraLog.Info($"Song not downloaded {hash}");
                var beatmap = await _beatSaver.BeatmapByHash(hash, _cancellationTokenSource.Token).ConfigureAwait(true);

                await SongDownloader.Instance.DownloadSong(beatmap, _cancellationTokenSource.Token)
                    .ConfigureAwait(true);
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

        var beatmapResult = _beatmapLevelsModel.GetBeatmapLevel(id);

        if (beatmapResult == null)
        {
            SendBeatmapStartError("beatmap level is null");
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
        _gameplaySetupViewController.Init();
        _playerSettingsPanelController.SetIsDirty();
        _playerSettingsPanelController.Refresh();

        _siraLog.Info($"Starting level with key {beatmapKey}");
        _menuTransitionsHelper.StartStandardLevel("Solo", in beatmapKey, beatmapResult,
            _playerDataModel.playerData.overrideEnvironmentSettings,
            _playerDataModel.playerData.colorSchemesSettings.GetOverrideColorScheme(), _gameplaySetupViewController.colorSchemesSettings.ShouldOverrideLightshowColors(), beatmapResult.GetColorScheme(beatmapKey.beatmapCharacteristic, beatmapKey.difficulty),
            _gameplaySetupViewController.gameplayModifiers, //TODO: Fix
            _playerSettingsPanelController.playerSpecificSettings, null, _environmentsListModel, Localization.Get("BUTTON_MENU"), 
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

        _siraLog.Error($"Sending error packet");
        _networkManager.SendPacket(packetWrapper);
    }
}