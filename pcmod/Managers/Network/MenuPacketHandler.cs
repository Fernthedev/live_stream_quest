﻿using System;
using System.Threading;
using System.Threading.Tasks;
using BeatSaverDownloader.Misc;
using BeatSaverSharp;
using BeatSaverSharp.Models;
using LiveStreamQuest.Protos;
using Polyglot;
using Zenject;

namespace LiveStreamQuest.Managers;

public class MenuPacketHandler : IPacketHandler, IDisposable
{
    private const string CustomLevelPrefix = "custom_level_";
    private readonly CancellationTokenSource _cancellationTokenSource = new();

    [Inject] private readonly BeatSaver _beatSaver;

    [Inject] private readonly BeatmapLevelsModel _beatmapLevelsModel;

    [Inject] private readonly MenuTransitionsHelper _menuTransitionsHelper;

    [Inject] private readonly BeatmapCharacteristicCollection _beatmapCharacteristicCollection;

    [Inject] private readonly PlayerDataModel _playerDataModel;

    [Inject] private readonly PlayerSettingsPanelController _playerSettingsPanelController;
    // [Inject] readonly LevelSelectionFlowCoordinator _levelSelectionFlow;

    public async void HandlePacket(PacketWrapper packetWrapper)
    {
        switch (packetWrapper.PacketCase)
        {
            case PacketWrapper.PacketOneofCase.StartBeatmap:
                await StartLevel(packetWrapper);

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
                var beatmap = await _beatSaver.BeatmapByHash(hash);

                await SongDownloader.Instance.DownloadSong(beatmap, _cancellationTokenSource.Token);
            }
        }

        var levelPreview = _beatmapLevelsModel.GetLevelPreviewForLevelId(id);

        if (levelPreview == null)
        {
            // TODO: User error dialog
            return;
        }

        var levelPack = custom ? SongCore.Loader.CustomLevelsPack : _beatmapLevelsModel.GetLevelPackForLevelId(id);

        if (levelPack == null)
        {
            // TODO: User error dialog
            return;
        }

        var beatmapResult = await _beatmapLevelsModel.GetBeatmapLevelAsync(id, _cancellationTokenSource.Token);

        if (beatmapResult.beatmapLevel == null || beatmapResult.isError)
        {
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

        // _levelSelectionFlow.Setup(
        //     new LevelSelectionFlowCoordinator.State(SelectLevelCategoryViewController.LevelCategory.All, levelPack,
        //         level, null));
        // _levelSelectionFlow.
    }

    public void Dispose()
    {
        _cancellationTokenSource.Dispose();
    }
}