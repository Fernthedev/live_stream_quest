using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using BeatSaverDownloader.Misc;
using BeatSaverSharp;
using BeatSaverSharp.Models;
using LiveStreamQuest.Network;
using LiveStreamQuest.Protos;
using Polyglot;
using SiraUtil.Logging;
using UnityEngine;
using Zenject;

namespace LiveStreamQuest.Managers;

public class MenuPacketHandler : IPacketHandler, IDisposable, IInitializable
{
    private const string CustomLevelPrefix = "custom_level_";
    private readonly CancellationTokenSource _cancellationTokenSource = new();

    [Inject] private readonly BeatSaver _beatSaver;

    [Inject] private readonly BeatmapLevelsModel _beatmapLevelsModel;

    [Inject] private readonly MenuTransitionsHelper _menuTransitionsHelper;

    [Inject] private readonly BeatmapCharacteristicCollection _beatmapCharacteristicCollection;

    [Inject] private readonly PlayerDataModel _playerDataModel;


    [Inject] private readonly NetworkManager _networkManager;

    [Inject] private readonly SiraLog _siraLog;

    [Inject] private readonly GlobalStateManager _globalStateManager;

    // [Inject] readonly LevelSelectionFlowCoordinator _levelSelectionFlow;
    private PlayerSettingsPanelController _playerSettingsPanelController;


    public void Initialize()
    {
        _playerSettingsPanelController = Resources.FindObjectsOfTypeAll<PlayerSettingsPanelController>().First();
    }

    public void Dispose()
    {
        _cancellationTokenSource.Dispose();
    }


    public async void HandlePacket(PacketWrapper packetWrapper)
    {
        switch (packetWrapper.PacketCase)
        {
            case PacketWrapper.PacketOneofCase.StartBeatmap:
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

    private async Task StartLevel(PacketWrapper packetWrapper)
    {
        var id = packetWrapper.StartBeatmap.LevelId;

        var custom = id.StartsWith(CustomLevelPrefix);

        if (custom)
        {
            var hash = id.Substring(CustomLevelPrefix.Length);
            if (!SongDownloader.IsSongDownloaded(hash))
            {
                var beatmap = await _beatSaver.BeatmapByHash(hash).ConfigureAwait(true);

                await SongDownloader.Instance.DownloadSong(beatmap, _cancellationTokenSource.Token);
            }
        }

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

        BeatmapCharacteristicSO beatmapCharacteristicSo =
            _beatmapCharacteristicCollection.GetBeatmapCharacteristicBySerializedName(packetWrapper.StartBeatmap
                .Characteristic);
        BeatmapDifficulty beatmapDifficulty = (BeatmapDifficulty)packetWrapper.StartBeatmap.Difficulty;
        var diffBeatmap =
            beatmapResult.beatmapLevel.beatmapLevelData.GetDifficultyBeatmap(beatmapCharacteristicSo,
                beatmapDifficulty);

        _menuTransitionsHelper.StartStandardLevel("Solo", diffBeatmap, levelPreview,
            _playerDataModel.playerData.overrideEnvironmentSettings,
            _playerDataModel.playerData.colorSchemesSettings.GetOverrideColorScheme(), null, new GameplayModifiers(),
            _playerSettingsPanelController.playerSpecificSettings, null, Localization.Get("BUTTON_MENU"), false, false,
            null, null, null);
    }

    private void SendBeatmapStartError(string message)
    {
        _globalStateManager.StartingGameFromQuest = false;
        var packetWrapper = new PacketWrapper
        {
            StartBeatmapFailure =
            {
                Error = message
            }
        };

        _networkManager.SendPacket(packetWrapper);
    }
}