﻿using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using BeatSaberMarkupLanguage.Attributes;
using BeatSaberMarkupLanguage.Components;
using BeatSaberMarkupLanguage.Components.Settings;
using BeatSaberMarkupLanguage.MenuButtons;
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

        private Task _initializeTask;
        private MenuButton _menuButton;

        [Inject] private readonly SiraLog _siraLog;


        // public event PropertyChangedEventHandler? PropertyChanged;
        [Inject] private readonly PluginConfig _config = null!;
        [Inject] private MainMenuViewController _mainMenu;
        [Inject] private MainFlowCoordinator _mainMenuFlowCoordinator;

        [UIComponent("setupModal")] private ModalView _modal;
        [UIComponent("vert")] private VerticalLayoutGroup _vert;
        [UIComponent("portField")] private StringSetting _portField;

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
        
        [UIValue("showMenuOnStartup")]
        internal bool ShowMenuOnStartup
        {
            get => _config.ShowMenuOnStartup;
            set
            {
                _config.ShowMenuOnStartup = value;
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

                _menuButton = new MenuButton("LiveStreamQuest", ShowPage);
                MenuButtons.instance.RegisterButton(_menuButton);
            });
        }

        // Setup modal if possible or wait
        // Display our new view coordinator as a child of the main menu view
        public void Initialize()
        {
            transform.localPosition = new UnityEngine.Vector3(0, 0, 5.5f);
            
            if (!ShowMenuOnStartup) return;
            if (_mainMenu.wasActivatedBefore)
            {
                ShowPage();
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

            ShowPage();
        }

        private void ShowPage()
        {
            _mainMenuFlowCoordinator.PresentViewController(this, immediately: true);
        }

        // Fixup modal after construction
        [UIAction("#post-parse")]
        private void PostParse()
        {
            _modal.name = "LiveStreamQuestSetupModal";

            _modal.blockerClickedEvent -= OnModalOnblockerClickedEvent;
            _modal.blockerClickedEvent += OnModalOnblockerClickedEvent;


            
            
            var oldKeyboard = _portField.modalKeyboard.keyboard;
            // _portField.modalKeyboard.keyboard.UpdateKeyText(KEYBOARD.NUMPAD);
            // _portField.modalKeyboard.keyboard.keys.Clear();
            // _portField.modalKeyboard.keyboard.AddKeys(KEYBOARD.NUMPAD);
            // _portField.modalKeyboard.keyboard = new KEYBOARD(oldKeyboard.container, KEYBOARD.NUMPAD);
            // _portField.gameObject.SetActive(true);
            // _portField.modalKeyboard.OnEnable();
        }

        // Dismiss view controller when modal is dismissed
        private void OnModalOnblockerClickedEvent()
        {
            _mainMenuFlowCoordinator.DismissViewController(this, AnimationDirection.Vertical);
        }

        // Display modal
        public override async void DidActivate(bool firstActivation, bool addedToHierarchy,
            bool screenSystemEnabling)
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
        public override void DidDeactivate(bool removedFromHierarchy, bool screenSystemDisabling)
        {
            base.DidDeactivate(removedFromHierarchy, screenSystemDisabling);
            _modal.Hide(true);
        }

        public void Dispose()
        {
            MenuButtons.instance.UnregisterButton(_menuButton);
            _modal.Hide(true);
            _mainMenuFlowCoordinator.DismissViewController(this, AnimationDirection.Vertical);
        }
    }
}