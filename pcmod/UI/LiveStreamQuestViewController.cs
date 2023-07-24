using System;
using System.Threading.Tasks;
using BeatSaberMarkupLanguage;
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

        private Task _initializeTask;

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
        
        [Inject]
        private void Construct()
        {
            InitializeUI();
        }
        
        // Initialize BSML early on
        private void InitializeUI()
        {
            _initializeTask = Task.Run(() =>
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
            });

        }
        
        // Setup modal if possible or wait
        // Display our new view coordinator as a child of the main menu view
        public void Initialize()
        {
            transform.localPosition = new UnityEngine.Vector3(0, 0, 5.5f);
            if (_mainMenu.wasActivatedBefore)
            {
                _mainMenuFlowCoordinator.PresentViewController(this, immediately: true);
            }
            else
            {
                _mainMenu.didActivateEvent -= OnMainMenuDidActivate;
                _mainMenu.didActivateEvent += OnMainMenuDidActivate;
            }
        }
        
        private void OnMainMenuDidActivate(bool firstActivation, bool hierarchy, bool enabling)
        {
            if (!firstActivation) return;
            
            _mainMenuFlowCoordinator.PresentViewController(this, immediately: true);
        }
        
        // Fixup modal after construction
        [UIAction("#post-parse")]
        private void PostParse()
        {
            _modal.name = "LiveStreamQuestSetupModal";
            
            _modal.blockerClickedEvent -= OnModalOnblockerClickedEvent;
            _modal.blockerClickedEvent += OnModalOnblockerClickedEvent;
        }
        
        // Dismiss view controller when modal is dismissed
        private void OnModalOnblockerClickedEvent()
        {
            _mainMenuFlowCoordinator.DismissViewController(this, AnimationDirection.Vertical);
        }

        // Display modal
        protected override async void DidActivate(bool firstActivation, bool addedToHierarchy, bool screenSystemEnabling)
        {
            base.DidActivate(firstActivation, addedToHierarchy, screenSystemEnabling);

            // Don't block the main thread with BSML nonsense
            await _initializeTask.ConfigureAwait(true);
            _siraLog.Info($"Opening modal {_modal.name}");
            _modal.Show(true, true);
            _siraLog.Info($"Opened modal {_modal.name}!");
            
            // parserParams.EmitEvent("close-modal");
            // parserParams.EmitEvent("open-modal");
        }

        // Close modal
        protected override void DidDeactivate(bool removedFromHierarchy, bool screenSystemDisabling)
        {
            base.DidDeactivate(removedFromHierarchy, screenSystemDisabling);
            _modal.Hide(true);
        }

        public void Dispose()
        {
            _modal.Hide(true);
            _mainMenuFlowCoordinator.DismissViewController(this, AnimationDirection.Vertical);
        }
    }
}