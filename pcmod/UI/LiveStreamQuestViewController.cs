using System;
using BeatSaberMarkupLanguage.Attributes;
using BeatSaberMarkupLanguage.Parser;
using BeatSaberMarkupLanguage.ViewControllers;
using HMUI;
using LiveStreamQuest.Configuration;
using SiraUtil.Logging;
using UnityEngine.UI;
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
        [Inject] private MainFlowCoordinator _mainMenuFlowCoordinator;

        [UIComponent("setupModal")] private ModalView _modal;
        [UIComponent("vert")] private VerticalLayoutGroup _vert;

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
        internal string Port
        {
            get => _config.Port.ToString();
            set
            {
                _config.Port = int.Parse(value);
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

            // parserParams.EmitEvent("close-modal");
            // parserParams.EmitEvent("open-modal");
            _siraLog.Info($"Opening modal {_modal.name}");
            _modal.Show(true, true);
            _modal.blockerClickedEvent -= OnModalOnblockerClickedEvent;
            _modal.blockerClickedEvent += OnModalOnblockerClickedEvent;
            _siraLog.Info($"Opened modal {_modal.name}!");
        }

        private void OnModalOnblockerClickedEvent()
        {
            _mainMenuFlowCoordinator.DismissViewController(this, AnimationDirection.Vertical);
        }

        protected override void DidActivate(bool firstActivation, bool addedToHierarchy, bool screenSystemEnabling)
        {
            base.DidActivate(firstActivation, addedToHierarchy, screenSystemEnabling);
            InitializeModalUI();
        }

        public void Initialize()
        {
            transform.localPosition = new UnityEngine.Vector3(0, 0, 5.5f);
            if (_mainMenu.wasActivatedBefore)
            {
                _mainMenuFlowCoordinator.PresentViewController(this, immediately: true);
            }
            else
            {
                _mainMenu.didActivateEvent -= MainMenuDidActivate;
                _mainMenu.didActivateEvent += MainMenuDidActivate;
            }
        }

        private void MainMenuDidActivate(bool firstActivation, bool hierarchy, bool enabling)
        {
            if (!firstActivation) return;
            
            _mainMenuFlowCoordinator.PresentViewController(this, immediately: true);
        }

        private void InitializeModalUI()
        {
            try
            {
                ModalHelper.Parse(transform, UIResource, this);
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