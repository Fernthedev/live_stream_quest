using System;
using System.Linq;
using LiveStreamQuest.Managers;
using LiveStreamQuest.Managers.Network;
using SiraUtil.Tools.FPFC;
using UnityEngine;
using Zenject;

namespace LiveStreamQuest.Installers;

internal class GameInstaller : Installer, IDisposable, IInitializable
{
    [Inject] private readonly GlobalStateManager _globalStateManager;
    [Inject] private readonly MainSettingsModelSO _mainSettingsModelSo;
    [Inject] private readonly IFPFCSettings _fpfcSettings;

    [Inject(Optional = true)]
    private SmoothCameraController? _smoothCameraController;

    public override void InstallBindings()
    {
        if (!_globalStateManager.StartingGameFromQuest) return;

        if (_smoothCameraController != null)
        {
            Container.BindInstance(_smoothCameraController);
        }
        Container.BindInterfacesAndSelfTo<VRControllerManager>().AsSingle();
        Container.BindInterfacesAndSelfTo<GamePacketHandler>().AsSingle().NonLazy();
        
        // Only enable if smooth camera is enabled
        // if (!_mainSettingsModelSo.smoothCameraThirdPersonEnabled) return;
        
        // Delegate control to FpfcManager
        // _fpfcSettings.Enabled = false;
        Container.BindInterfacesAndSelfTo<FpfcManager>().AsSingle().NonLazy();
        
        
    }

    public void Dispose()
    {
        _globalStateManager.StartingGameFromQuest = false;
    }

    public void Initialize()
    {
        // For some reason the game doesn't provide this already
        _smoothCameraController ??= Resources.FindObjectsOfTypeAll<SmoothCameraController>().FirstOrDefault();
    }
}