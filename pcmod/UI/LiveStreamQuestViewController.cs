using System;
using System.ComponentModel;
using System.Reflection;
using BeatSaberMarkupLanguage;
using BeatSaberMarkupLanguage.Attributes;
using BeatSaberMarkupLanguage.Settings;
using HMUI;
using LiveStreamQuest.Configuration;
using LiveStreamQuest.Protos;
using SiraUtil.Logging;
using UnityEngine;
using Zenject;

namespace LiveStreamQuest.UI
{
    [HotReload]
    internal class LiveStreamQuestViewController : IInitializable, IDisposable, INotifyPropertyChanged
    {
        private const string UI_RESOURCE = "LiveStreamQuest.UI.BSML.LiveStreamQuestView.bsml";

        [Inject] private readonly SiraLog _siraLog;
        
        public event PropertyChangedEventHandler? PropertyChanged;
        private readonly PluginConfig _config;
        [Inject] private MainMenuViewController _mainMenu;

        public LiveStreamQuestViewController(PluginConfig config)
        {
            _config = config;
        }

        [UIComponent("setupModal")]
        private ModalView _modal;

        [UIValue("ipAddress")]
        internal string IPAddress
        {
            get => _config.Address;
            set
            {
                _config.Address = value;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IPAddress)));
            }
        }

        [UIValue("port")]
        internal int Port
        {
            get => _config.Port;
            set
            {
                _config.Port = value;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Port)));
            }
        }

        [UIValue("timeout")]
        internal int Timeout
        {
            get => _config.ConnectionTimeoutSeconds;
            set
            {
                _config.ConnectionTimeoutSeconds = value;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Timeout)));
            }
        }

        [UIValue("reconnectionAttempts")]
        internal int ReconnectionAttempts
        {
            get => _config.ReconnectionAttempts;
            set
            {
                _config.ReconnectionAttempts = value;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(ReconnectionAttempts)));
            }
        }

        [UIValue("connectOnStartup")]
        internal bool ConnectOnStartup
        {
            get => _config.ConnectOnStartup;
            set
            {
                _config.ConnectOnStartup = value;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(ConnectOnStartup)));
            }
        }
        
        public void Initialize()
        {
            try
            {
                ModalHelper.Parse(_mainMenu.transform, UI_RESOURCE, _modal);
                _modal.name = "LiveStreamQuestSetupModal";
                _modal.transform.localPosition = new UnityEngine.Vector3(0, 0, (float)-0.5);
            }
            catch (Exception e)
            {
                _siraLog.Error(e.Message);
                if (e.InnerException is not null)
                    _siraLog.Error(e.InnerException);
                _siraLog.Error(e.StackTrace);
            }
        }

        public void Dispose()
        {
        }
    }
}