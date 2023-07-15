using System;
using System.ComponentModel;
using BeatSaberMarkupLanguage.Attributes;
using BeatSaberMarkupLanguage.Settings;
using HMUI;
using LiveStreamQuest.Configuration;
using Zenject;

namespace LiveStreamQuest.UI
{
    [HotReload]
    internal class LiveStreamQuestViewController : IInitializable, IDisposable, INotifyPropertyChanged
    {
        public event PropertyChangedEventHandler? PropertyChanged; // Use this to notify BSML of a UI Value change;

        // PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Name of the Method)));
        private readonly PluginConfig _config;

        public LiveStreamQuestViewController(PluginConfig config)
        {
            _config = config;
        }
        
        [UIComponent("setupModal")]
        private readonly ModalView _modal = new();

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
            get => _config.ConnectionTimeout;
            set
            {
                _config.ConnectionTimeout = value;
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
            _modal.Show(true, true);
        }

        public void Dispose()
        {
        }
    }
}