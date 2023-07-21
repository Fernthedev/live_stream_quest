using System;
using System.ComponentModel;
using System.Reflection;
using BeatSaberMarkupLanguage;
using BeatSaberMarkupLanguage.Attributes;
using BeatSaberMarkupLanguage.Parser;
using BeatSaberMarkupLanguage.Settings;
using BeatSaberMarkupLanguage.ViewControllers;
using HMUI;
using LiveStreamQuest.Configuration;
using LiveStreamQuest.Protos;
using SiraUtil.Logging;
using UnityEngine;
using Zenject;

namespace LiveStreamQuest.UI
{
    [ViewDefinition("LiveStreamQuest.UI.BSML.LiveStreamQuestView.bsml")]
    [HotReload(RelativePathToLayout = @"..\UI\BSML\LiveStreamQuestView.bsml")]
    internal class LiveStreamQuestViewController : BSMLAutomaticViewController, IInitializable, IDisposable
    {
        private const string UIResource = "LiveStreamQuest.UI.BSML.LiveStreamQuestView.bsml";

        [Inject] private readonly SiraLog _siraLog;

        // public event PropertyChangedEventHandler? PropertyChanged;
        [Inject] private readonly PluginConfig _config = null!;
        [Inject] private MainMenuViewController _mainMenu;

        [UIComponent("setupModal")] private ModalView _modal;

        [UIValue("ipAddress")]
        internal string IPAddress
        {
            get => _config.Address;
            set
            {
                _config.Address = value;
                NotifyPropertyChanged();
            }
        }

        [UIValue("port")]
        internal int Port
        {
            get => _config.Port;
            set
            {
                _config.Port = value;
                NotifyPropertyChanged();
            }
        }

        [UIValue("timeout")]
        internal int Timeout
        {
            get => _config.ConnectionTimeoutSeconds;
            set
            {
                _config.ConnectionTimeoutSeconds = value;
                NotifyPropertyChanged();
            }
        }

        [UIValue("reconnectionAttempts")]
        internal int ReconnectionAttempts
        {
            get => _config.ReconnectionAttempts;
            set
            {
                _config.ReconnectionAttempts = value;
                NotifyPropertyChanged();
            }
        }

        [UIValue("connectOnStartup")]
        internal bool ConnectOnStartup
        {
            get => _config.ConnectOnStartup;
            set
            {
                _config.ConnectOnStartup = value;
                NotifyPropertyChanged();
            }
        }

        [UIParams] private readonly BSMLParserParams parserParams;

        [UIAction("#post-parse")]
        private void PostParse()
        {
            _modal.name = "LiveStreamQuestSetupModal";
            _modal.transform.localPosition = new UnityEngine.Vector3(0, 0, 0.5f);
            // parserParams.EmitEvent("close-modal");
            // parserParams.EmitEvent("open-modal");
            _siraLog.Info($"Opening modal {_modal.name}");
            _modal.Show(true, true);
            _siraLog.Info($"Opened modal {_modal.name}!");
        }

        public void Initialize()
        {
            if (_mainMenu.wasActivatedBefore)
            {
                InitializeModalUI();
            }
            else
            {
                _mainMenu.didActivateEvent += (firstActivation, hierarchy, enabling) =>
                {
                    if (firstActivation) InitializeModalUI();
                };
            }
        }

        private void InitializeModalUI()
        {
            try
            {
                ModalHelper.Parse(_mainMenu.transform, UIResource, this);
            }
            catch (Exception e)
            {
                _siraLog.Error(e);
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