using System;
using SiraUtil.Logging;
using SiraUtil.Tools.FPFC;
using Zenject;

namespace LiveStreamQuest.Managers;

public class FpfcManager : IInitializable, IDisposable
{
    [Inject] private readonly IFPFCSettings _siraFpfc;
    // [Inject(Optional = true)] private SmoothCameraController? _smoothCameraController;
    
    [Inject] private readonly PauseController _pauseController;
    [Inject] private readonly SiraLog _siraLog;

    public void Initialize()
    {
        _pauseController.didPauseEvent -= PauseControllerOndidPauseEvent;
        _pauseController.didPauseEvent += PauseControllerOndidPauseEvent;
        _pauseController.didResumeEvent -= PauseControllerOndidResumeEvent;
        _pauseController.didResumeEvent += PauseControllerOndidResumeEvent;

        _siraLog.Info("Setting FPFC mode");
        _siraFpfc.Enabled = _pauseController._paused || _pauseController._wantsToPause;
    }
    
    private void PauseControllerOndidPauseEvent()
    {
        _siraLog.Info("Smooth camera disabled");
        _siraFpfc.Enabled = true;
    }

    private void PauseControllerOndidResumeEvent()
    {
        _siraLog.Info("Smooth camera enabled");
        _siraFpfc.Enabled = false;
    }


    public void Dispose()
    {
        _siraFpfc.Enabled = true;
    }
}