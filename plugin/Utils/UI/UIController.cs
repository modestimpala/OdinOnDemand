using System;
using System.Collections;
using System.Globalization;
using System.Linq;
using Jotunn.Managers;
using OdinOnDemand.Dynamic;
using OdinOnDemand.Interfaces;
using OdinOnDemand.MPlayer;
using OdinOnDemand.Utils.Config;
using OdinOnDemand.Utils.Net;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using Logger = Jotunn.Logger;
using Object = UnityEngine.Object;
using Random = System.Random;

namespace OdinOnDemand.Utils.UI
{
    public class UIController
    {
        private BasePlayer _basePlayer;
        
        internal GameObject LoadingIndicatorObj;
        internal GameObject LockedIconObj;
        private Slider _masterVolumeSliderComponent;
        private GameObject _mutedVolumeObj;
        private DefaultControls.Resources _oodResources;
        private GameObject _playlistIndexObj;
        private GameObject _playlistStringObj;
        internal Text PlaylistTrackText;
        private GameObject _previousPlaylistTrackObj;
        internal GameObject URLPanelObj;
        private GameObject _dynamicPanelObj;
        private GameObject _dynamicScrollViewObj;
        private GameObject _settingsPanelObj;
        private RectTransform _settingsPanelRT;
        private GameObject _skipPlaylistTrackObj;
        private GameObject _toggleLoopObj;
        internal GameObject ToggleShuffleObj;
        internal GameObject ToggleShuffleTextObj;
        internal GameObject UnlockedIconObj;
        private GameObject _unmutedVolumeObj;
        private GameObject _urlInputFieldObj;
        private Slider _volumeSlider;
        private Slider _volumeSliderDynamic;
        private Toggle _adminOnlyToggle;
        internal Image RadioPanelThumbnail;
        
        private ToggleGroup _entryToggleGroup;
        
        private Toggle _entryToggle;
        
        // Loading messages
        public readonly string[] LoadingMessages = { "Processing", "Processing.", "Processing..", "Processing..." };
        
        private readonly RpcHandler _rpc  = OdinOnDemandPlugin.RPCHandlers;
        private GameObject _toggleMuteButton;
        private GameObject _volumeSliderMainObj;
        private GameObject _volumeSliderDynamicObj;
        private GameObject _toogleLoopButton;
        private GameObject _toggleShuffle;
        private GameObject _toggleShuffleText;

        public UIController(BasePlayer basePlayer)
        {
            _basePlayer = basePlayer;
        }

        public void Initialize()
        {
            _oodResources = new DefaultControls.Resources
            {
                knob = OdinOnDemandPlugin.UISprites["handle"],
                background = OdinOnDemandPlugin.UISprites["background"],
                standard = OdinOnDemandPlugin.UISprites["fill"],
                checkmark = OdinOnDemandPlugin.UISprites["checkmark"]
            };
        }
        
        public void DestroyUI()
        {
            if (URLPanelObj)
            {
                Object.Destroy(URLPanelObj);
            }
            if (_dynamicPanelObj)
            {
                Object.Destroy(_dynamicPanelObj);
            }
        }

        private void ToggleMute()
        {
            bool isMuted = _basePlayer.mAudio.volume > 0f;
            _basePlayer.mAudio.volume = isMuted ? 0f : _basePlayer.PlayerSettings.MuteVol;
    
            // Update the UI elements based on the mute state
            if (_mutedVolumeObj != null)
            {
                _mutedVolumeObj.SetActive(isMuted);
                _unmutedVolumeObj.SetActive(!isMuted);
            }

            // Set the correct volume in the player settings
            _basePlayer.PlayerSettings.Volume = _basePlayer.mAudio.volume;

            // Update the volume sliders
            _volumeSlider.value = _basePlayer.PlayerSettings.Volume;
            _volumeSliderDynamic.value = _basePlayer.PlayerSettings.Volume;

            // Store the unmuted volume if we just muted the audio
            if (isMuted)
            {
                _basePlayer.PlayerSettings.MuteVol = _basePlayer.PlayerSettings.Volume;
            }
        }


        internal void UpdatePlaylistInfo()
        {
            if (_basePlayer.PlayerSettings.IsPlayingPlaylist)
                if (URLPanelObj)
                {
                    var length = _basePlayer.CurrentPlaylist.ElementAt(_basePlayer.PlaylistPosition).Title.Length;
                    if (length > 14) length = 14;
                    if (!_basePlayer.PlayerSettings.IsShuffling)
                    {
                        _basePlayer.PlaylistString = _basePlayer.PlaylistString = _basePlayer.CurrentPlaylist.ElementAt(_basePlayer.PlaylistPosition).Title;
                        _basePlayer.PlaylistString = "Playing '" + _basePlayer.CurrentPlaylist.ElementAt(_basePlayer.PlaylistPosition).Title
                            .Substring(0, length) + "...' ";
                        _playlistIndexObj.GetComponent<Text>().text = _basePlayer.PlaylistPosition + 1 + "/" + _basePlayer.CurrentPlaylist.Count;
                        PlaylistTrackText.text = _basePlayer.PlaylistString;
                    }
                    else
                    {
                        _basePlayer.PlaylistString = _basePlayer.PlaylistString = _basePlayer.CurrentPlaylist.ElementAt(_basePlayer.PlaylistPosition).Title;
                        _basePlayer.PlaylistString = "Playing '" + _basePlayer.CurrentPlaylist.ElementAt(_basePlayer.PlaylistPosition).Title
                            .Substring(0, length) + "...' ";
                        _playlistIndexObj.GetComponent<Text>().text = _basePlayer.PlaylistPosition + 1 + "/" + _basePlayer.CurrentPlaylist.Count + ", (shuffled)";
                        PlaylistTrackText.text = _basePlayer.PlaylistString;
                    }
                }
        }

        public void UpdatePlaylistUI()
        {
            if(_playlistStringObj)  _playlistStringObj.SetActive(_basePlayer.PlayerSettings.IsPlayingPlaylist);
            if(_skipPlaylistTrackObj) _skipPlaylistTrackObj.SetActive(_basePlayer.PlayerSettings.IsPlayingPlaylist);
            if(_previousPlaylistTrackObj) _previousPlaylistTrackObj.SetActive(_basePlayer.PlayerSettings.IsPlayingPlaylist);
            if(ToggleShuffleObj) ToggleShuffleObj.SetActive(_basePlayer.PlayerSettings.IsPlayingPlaylist);
            if(ToggleShuffleTextObj) ToggleShuffleTextObj.SetActive(_basePlayer.PlayerSettings.IsPlayingPlaylist);
            if(_playlistIndexObj)  _playlistIndexObj.SetActive(_basePlayer.PlayerSettings.IsPlayingPlaylist);
        }

        internal IEnumerator UnavailableIndicator(string message)
        {
            if (LoadingIndicatorObj)
            {
                LoadingIndicatorObj.GetComponent<Text>().text = message;
                LoadingIndicatorObj.SetActive(true);
                yield return new WaitForSeconds(2);
                LoadingIndicatorObj.SetActive(false);
            }
        }

        private void CreateMainGUI()
        {
            if (!URLPanelObj)
            {
                if (GUIManager.Instance == null)
                {
                    Logger.LogDebug("GUIManager instance is null");
                    return;
                }

                if (!GUIManager.CustomGUIFront)
                {
                    Logger.LogDebug("GUIManager CustomGUI is null");
                    return;
                }

                CreateURLPanel();
                CreateDynamicPanel();
                PopulateDynamicPanel();
            }
        }

        private void PopulateDynamicPanel()
        {
            foreach (var station in StationManager.Instance.DynamicStations)
            {
                    CreateDynamicAudioEntry(station.Title, station.Thumbnail, _dynamicContentTransform, 
                        new Vector2(0,0),
                        DynamicEntryValueChanged);
            }
        }

        private void DynamicEntryValueChanged(bool value)
        {
            var active  = _entryToggleGroup.ActiveToggles().FirstOrDefault();
            if (active != null)
            {
                var station = StationManager.Instance.GetStation(active.GetComponentInChildren<Text>().text);
                _basePlayer.SetDynamicStation(station);
            }
        }
        
        private void CreateDynamicPanel()
        {
            if (_dynamicPanelObj)
                return;
            _dynamicPanelObj = GUIManager.Instance.CreateWoodpanel(
                URLPanelObj.transform,
                new Vector2(0.5f, 0.5f),
                new Vector2(0.5f, 0.5f),
                new Vector2(0f, 0f),
                375f,
                155f,
                true);
            _dynamicPanelObj.AddComponent<CustomDragHandler>().parentObj = URLPanelObj;
            _dynamicPanelObj.SetActive(false);
            
            _dynamicScrollViewObj = DefaultControls.CreateScrollView(_oodResources);
            var scrollRect = _dynamicScrollViewObj.GetComponent<ScrollRect>();
            scrollRect.vertical = true;
            scrollRect.horizontal = false;
            
            _dynamicScrollViewObj.transform.SetParent(_dynamicPanelObj.transform, false);
            var dynamicScrollViewRT = _dynamicScrollViewObj.GetComponent<RectTransform>();
            dynamicScrollViewRT.anchorMin = new Vector2(0f, 0f);
            dynamicScrollViewRT.anchorMax = new Vector2(1f, 1f);
            dynamicScrollViewRT.offsetMin = new Vector2(10f, 40f);
            dynamicScrollViewRT.offsetMax = new Vector2(-10f, -16f);
            var scrollviewBg = _dynamicScrollViewObj.GetComponent<Image>();
            scrollviewBg.color = new Color(0.1f, 0.1f, 0.1f, 0.85f);
            _dynamicScrollViewObj.transform.Find("Scrollbar Vertical").gameObject.SetActive(false);
            _dynamicContentTransform = _dynamicScrollViewObj.transform.Find("Viewport/Content");
            var verticalLayoutGroup = _dynamicContentTransform.gameObject.AddComponent<VerticalLayoutGroup>();
            verticalLayoutGroup.childForceExpandWidth = true;
            verticalLayoutGroup.childForceExpandHeight = true;
            verticalLayoutGroup.childControlWidth = true;
            verticalLayoutGroup.childControlHeight = true;
            verticalLayoutGroup.childScaleHeight = true;
            verticalLayoutGroup.childScaleWidth = true;
            verticalLayoutGroup.spacing = 1f;
            
            _entryToggleGroup = _dynamicContentTransform.gameObject.AddComponent<ToggleGroup>();
            _entryToggleGroup.allowSwitchOff = false;
            _entryToggleGroup.SetAllTogglesOff();

            CreateUISpriteButton("play", _dynamicPanelObj.transform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
                new Vector2(-150f, -57f), 34f, 34f,  () => 
                    _basePlayer.PlayStation(_entryToggleGroup.ActiveToggles().FirstOrDefault()?.GetComponentInChildren<Text>().text));
            
            CreateUISpriteButton("stop", _dynamicPanelObj.transform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
                new Vector2(-112f, -57f), 34f, 34f,  () => _basePlayer.Stop());
            
            CreateMuteButton(new Vector2(150f, -57f), _dynamicPanelObj.transform);
            // Volume slider
            _volumeSliderDynamicObj = DefaultControls.CreateSlider(_oodResources);
            _volumeSliderDynamicObj.transform.SetParent(_dynamicPanelObj.transform);
            _volumeSliderDynamicObj.transform.localPosition = new Vector3(100f, -57f, 0f);
            _volumeSliderDynamicObj.transform.localScale = new Vector3(0.4f, 1.4f, 1.17f);
            var slider = _volumeSliderDynamicObj.GetComponent<Slider>();
            slider.value = _basePlayer.PlayerSettings.Volume;
            _volumeSliderDynamic = slider;

            slider.onValueChanged.AddListener(OnVolumeSliderChanged);
        }
        
        private void CreateURLPanel()
        {
            URLPanelObj = GUIManager.Instance.CreateWoodpanel(
                GUIManager.CustomGUIFront.transform,
                new Vector2(0.5f, 0.5f),
                new Vector2(0.5f, 0.5f),
                new Vector2(0f, 0f),
                375f,
                155f,
                true);
            URLPanelObj.SetActive(false);
            var closeButton = GUIManager.Instance.CreateButton(
                "X",
                URLPanelObj.transform,
                new Vector2(0.5f, 0.5f),
                new Vector2(0.5f, 0.5f),
                new Vector2(168f, 90f),
                32f,
                32f);

            _urlInputFieldObj = GUIManager.Instance.CreateInputField(
                URLPanelObj.transform,
                new Vector2(0.5f, 0.5f),
                new Vector2(0.5f, 0.5f),
                new Vector2(-35f, 50f),
                InputField.ContentType.Standard,
                "Enter video/song/playlist url...",
                16,
                250f,
                30f);
            var setButton = GUIManager.Instance.CreateButton(
                "Set",
                URLPanelObj.transform,
                new Vector2(0.5f, 0.5f),
                new Vector2(0.5f, 0.5f),
                new Vector2(130f, 50f),
                64f,
                32f);
            
            _urlInputField = _urlInputFieldObj.GetComponent<InputField>();
            
            CreateUISpriteButton("play", URLPanelObj.transform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
                new Vector2(-150f, 10f), 34f, 34f,  () => _basePlayer.Play());
            
            CreateUISpriteButton("pause", URLPanelObj.transform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
                new Vector2(-112f, 10f), 34f, 34f,  () => _basePlayer.Pause());
            
            CreateUISpriteButton("stop", URLPanelObj.transform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
                new Vector2(-74f, 10f), 34f, 34f,  () => _basePlayer.Stop());

            CreateMuteButton(new Vector2(150f, 15f), URLPanelObj.transform);
            
            // Volume slider
            _volumeSliderMainObj = DefaultControls.CreateSlider(_oodResources);
            _volumeSliderMainObj.transform.SetParent(URLPanelObj.transform);
            _volumeSliderMainObj.transform.localPosition = new Vector3(100f, 15f, 0f);
            _volumeSliderMainObj.transform.localScale = new Vector3(0.4f, 1.4f, 1.17f);
            var slider = _volumeSliderMainObj.GetComponent<Slider>();
            slider.value = _basePlayer.PlayerSettings.Volume;
            _volumeSlider = slider;

            slider.onValueChanged.AddListener(OnVolumeSliderChanged);


            var loadingIndicator = GUIManager.Instance.CreateText(
                "Processing",
                URLPanelObj.transform,
                new Vector2(0.5f, 0.5f),
                new Vector2(0.5f, 0.5f),
                new Vector2(0f, -105f),
                GUIManager.Instance.AveriaSerifBold,
                22,
                GUIManager.Instance.ValheimOrange,
                true,
                Color.black,
                350f,
                40f,
                false);
            loadingIndicator.SetActive(false);

            _toogleLoopButton = GUIManager.Instance.CreateButton(
                "N",
                URLPanelObj.transform,
                new Vector2(0.5f, 0.5f),
                new Vector2(0.5f, 0.5f),
                new Vector2(154f, -14f),
                25f,
                25f);
            GUIManager.Instance.CreateText(
                "Loop",
                URLPanelObj.transform,
                new Vector2(0.5f, 0.5f),
                new Vector2(0.5f, 0.5f),
                new Vector2(115f, -24f),
                GUIManager.Instance.AveriaSerifBold,
                18,
                GUIManager.Instance.ValheimOrange,
                true,
                Color.black,
                48f,
                40f,
                false);

            _toggleShuffle = GUIManager.Instance.CreateButton(
                "N",
                URLPanelObj.transform,
                new Vector2(0.5f, 0.5f),
                new Vector2(0.5f, 0.5f),
                new Vector2(154f, -43f),
                25f,
                25f);
            _toggleShuffleText = GUIManager.Instance.CreateText(
                "Shuffle",
                URLPanelObj.transform,
                new Vector2(0.5f, 0.5f),
                new Vector2(0.5f, 0.5f),
                new Vector2(110f, -54f),
                GUIManager.Instance.AveriaSerifBold,
                18,
                GUIManager.Instance.ValheimOrange,
                true,
                Color.black,
                64f,
                40f,
                false);

            ToggleShuffleObj = _toggleShuffle;
            ToggleShuffleTextObj = _toggleShuffleText;
            _toggleShuffle.SetActive(false);
            ToggleShuffleTextObj.SetActive(false);

            var toggleShuffleButton = _toggleShuffle.GetComponent<Button>();
            toggleShuffleButton.onClick.AddListener(ToggleShuffle);

            var toogleLoopButtonAction = _toogleLoopButton.GetComponent<Button>();
            toogleLoopButtonAction.onClick.AddListener(ToggleLoop);
            _toggleLoopObj = _toogleLoopButton;
            LoadingIndicatorObj = loadingIndicator;
            _loadingIndicatorText = LoadingIndicatorObj.GetComponent<Text>();


            //create button listeners
            var button = closeButton.GetComponent<Button>();
            button.onClick.AddListener(ToggleMainPanel);

            //set vid
            var setButtonAction = setButton.GetComponent<Button>();
            setButtonAction.onClick.AddListener(() =>
            {
                _basePlayer.SetURL(_urlInputField.text);
                _basePlayer.CurrentPlaylist = null;
                _basePlayer.PlayerSettings.IsPlayingPlaylist = false;
            });

            var playlistStringText = GUIManager.Instance.CreateText(
                "",
                URLPanelObj.transform,
                new Vector2(0.5f, 0.5f),
                new Vector2(0.5f, 0.5f),
                new Vector2(-5f, -35f),
                GUIManager.Instance.AveriaSerifBold,
                18,
                GUIManager.Instance.ValheimOrange,
                true,
                Color.black,
                300f,
                40f,
                false);
            playlistStringText.SetActive(false);
            _playlistStringObj = playlistStringText;

            var playlistIndexText = GUIManager.Instance.CreateText(
                "0/0",
                URLPanelObj.transform,
                new Vector2(0.5f, 0.5f),
                new Vector2(0.5f, 0.5f),
                new Vector2(-5f, -62f),
                GUIManager.Instance.AveriaSerifBold,
                18,
                GUIManager.Instance.ValheimOrange,
                true,
                Color.black,
                300f,
                40f,
                false);
            playlistStringText.SetActive(false);
            _playlistIndexObj = playlistIndexText;

            PlaylistTrackText = _playlistStringObj.GetComponent<Text>();

            _skipPlaylistTrackObj = CreateUISpriteButton("next", URLPanelObj.transform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
                new Vector2(44f, 10f), 34f, 34f,  OnClickSkipPlaylistTrack);

            _previousPlaylistTrackObj = CreateUISpriteButton("previous", URLPanelObj.transform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
                new Vector2(6f, 10f), 34f, 34f,  PreviousPlaylistTrack);
            

            LockedIconObj = CreateUISpriteButton("unlock", URLPanelObj.transform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
                new Vector2(130f, 90f), 32f, 32f,  ToggleLock, false);
            UnlockedIconObj= CreateUISpriteButton("lock", URLPanelObj.transform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
                new Vector2(130f, 90f), 32f, 32f,  ToggleLock, false);
            UnlockedIconObj.SetActive(false);
            
            CreateUISpriteButton("settings", URLPanelObj.transform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
                new Vector2(168f, -90f), 32f, 32f,  ToggleSettingsPanel, false);
            
            _urlTabButtonObj = CreateUISpriteButton("url", URLPanelObj.transform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
                new Vector2(126f, -90f), 32f, 32f, () => { ChangePlayerMode();}, false);
            
            _dynamicTabButtonObj = CreateUISpriteButton("local", URLPanelObj.transform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
                new Vector2(126f, -90f), 32f, 32f,  () => { ChangePlayerMode();}, false);
            if (_basePlayer.PlayerSettings.CurrentMode != PlayerSettings.PlayerMode.Dynamic)
            {
                _dynamicTabButtonObj.gameObject.SetActive(false);
            }
            else
            {
                _urlTabButtonObj.gameObject.SetActive(false);
            }
            
        }

        private void ChangePlayerMode(bool zdo = true)
        {
            if (_dynamicPanelObj)
            {
                _basePlayer.Stop();
                _dynamicPanelObj.SetActive(!_dynamicPanelObj.activeSelf);
                _urlTabButtonObj.gameObject.SetActive(!_dynamicPanelObj.activeSelf);
                _dynamicTabButtonObj.gameObject.SetActive(_dynamicPanelObj.activeSelf);
                _basePlayer.PlayerSettings.CurrentMode = _dynamicPanelObj.activeSelf
                    ? PlayerSettings.PlayerMode.Dynamic
                    : PlayerSettings.PlayerMode.URL;
                _basePlayer.UpdateRadioPanel();
                if (zdo)
                {
                    _basePlayer.SaveZDO();  
                    _basePlayer.SendUpdateZDO_RPC();
                }
            }
        }

        private void CreateMuteButton(Vector2 pos, Transform parent)
        {
            //MUTE
            _toggleMuteButton = GUIManager.Instance.CreateButton(
                "",
                parent.transform,
                new Vector2(0.5f, 0.5f),
                new Vector2(0.5f, 0.5f),
                pos,
                32f,
                32f);
            var iconObj = new GameObject("iconUnmuted")
            {
                transform =
                {
                    parent = _toggleMuteButton.transform,
                    localPosition = new Vector3(0, 0, 0),
                    localScale = new Vector3(0.2f, 0.2f, 0.2f)
                }
            };
            var icon = iconObj.AddComponent<Image>();
            icon.sprite = OdinOnDemandPlugin.UISprites["volume"];
            _unmutedVolumeObj = iconObj;
            iconObj = new GameObject("iconMuted")
            {
                transform =
                {
                    parent = _toggleMuteButton.transform,
                    localPosition = new Vector3(0, 0, 0),
                    localScale = new Vector3(0.2f, 0.2f, 0.2f)
                }
            };
            icon = iconObj.AddComponent<Image>();
            icon.sprite = OdinOnDemandPlugin.UISprites["mute"];
            _mutedVolumeObj = iconObj;
            _toggleMuteButton.GetComponent<Image>().enabled = false;
            _mutedVolumeObj.SetActive(false);

            //toggle mute action
            var toggleMuteAction = _toggleMuteButton.GetComponent<Button>();
            toggleMuteAction.onClick.AddListener(ToggleMute);
        }

        private void ToggleSettingsPanel()
        {
            if (_basePlayer.PlayerSettings.PlayerType == CinemaPackage.MediaPlayers.BeltPlayer && 
                !SynchronizationManager.Instance.PlayerIsAdmin) // Regular players dont get audio Distance panel
            {
                CreateSettingsGUI(false, false, false);
            } else if (_basePlayer.PlayerSettings.PlayerType == CinemaPackage.MediaPlayers.BeltPlayer && 
                       SynchronizationManager.Instance.PlayerIsAdmin) // Admins do
            {
                CreateSettingsGUI(false, true, false);
            } else if (_basePlayer.PlayerSettings.PlayerType == CinemaPackage.MediaPlayers.CartPlayer)
            {
                CreateSettingsGUI(true, true, false);
            }
            else
            {
                CreateSettingsGUI();
            }
            

            _basePlayer.PlayerSettings.IsSettingsGuiActive = !_settingsPanelObj.activeSelf;
            if (_settingsPanelObj)
            {
                _masterVolumeSliderComponent.value = _basePlayer.PlayerSettings.PlayerType == CinemaPackage.MediaPlayers.CinemaScreen
                    ? OODConfig.MasterVolumeScreen.Value
                    : OODConfig.MasterVolumeMusicplayer.Value;
                if (_adminOnlyToggle) _adminOnlyToggle.isOn = _basePlayer.PlayerSettings.AdminOnly;
            }
            UpdateSpeakerCount();
            _settingsPanelObj.SetActive(_basePlayer.PlayerSettings.IsSettingsGuiActive);
        }

        internal void UpdateSpeakerCount()
        {
            if(_speakerText) _speakerText.text = "Speakers: " + _basePlayer.mSpeakers.Count;
        }

        private void CreateSettingsGUI(bool  adminOnlyEnabled = true, 
            bool audioDistanceEnabled = true, bool verticalDropOffEnabled = true)
        {
            if (_settingsPanelObj == null)
            {
                if (GUIManager.Instance == null)
                {
                    Logger.LogDebug("GUIManager instance is null");
                    return;
                }

                if (!GUIManager.CustomGUIFront)
                {
                    Logger.LogDebug("GUIManager CustomGUI is null");
                    return;
                }

                ///////////////////////////////
                /// MAIN SCROLLVIEW OBJECT ///
                _settingsPanelObj = DefaultControls.CreateScrollView(_oodResources);
                _settingsPanelRT = _settingsPanelObj.GetComponent<RectTransform>();
                var selectionPanelRT = URLPanelObj.GetComponent<RectTransform>();
                _settingsPanelObj.transform.SetParent(URLPanelObj.transform, false);

                _settingsPanelRT.anchorMin = new Vector2(0.5f, 0.5f);
                _settingsPanelRT.anchorMax = new Vector2(0.5f, 0.5f);
                _settingsPanelRT.SetSizeWithCurrentAnchors(RectTransform.Axis.Horizontal,
                    selectionPanelRT.sizeDelta.x / 1.15f);
                _settingsPanelRT.SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical,
                    selectionPanelRT.sizeDelta.y / 1.15f);
                _settingsPanelRT.anchoredPosition = new Vector2(0, 0);

                var scrollviewBg = _settingsPanelObj.GetComponent<Image>();
                scrollviewBg.color = new Color(0, 0, 0, 0.95f);
                var scrollRect = _settingsPanelObj.GetComponent<ScrollRect>();
                scrollRect.vertical = true;
                scrollRect.horizontal = false;
                scrollRect.elasticity = 0.01f;
                scrollRect.scrollSensitivity = 20;
                scrollRect.horizontalScrollbar = null;
                scrollRect.verticalScrollbar = null;
                _settingsPanelObj.transform.Find("Scrollbar Horizontal").gameObject.SetActive(false);
                _settingsPanelObj.transform.Find("Scrollbar Vertical").gameObject.SetActive(false);
                
                _settingsPanelObj.SetActive(false);
                var contentTransform = _settingsPanelObj.transform.Find("Viewport/Content");
                var verticalLayoutGroup = contentTransform.gameObject.AddComponent<VerticalLayoutGroup>();
                verticalLayoutGroup.childForceExpandWidth = true;
                verticalLayoutGroup.childForceExpandHeight = true;
                verticalLayoutGroup.childControlWidth = true;
                verticalLayoutGroup.childControlHeight = true;
                verticalLayoutGroup.childScaleHeight = true;
                verticalLayoutGroup.childScaleWidth = true;
                verticalLayoutGroup.spacing = 5f;
                
                //////////////////////////
                /// ADMIN ONLY TOGGLE ///
                if (SynchronizationManager.Instance.PlayerIsAdmin && adminOnlyEnabled)
                {
                    /// toggle
                    var adminOnlyToggleObj = DefaultControls.CreateToggle(_oodResources);
                    adminOnlyToggleObj.transform.SetParent(contentTransform, false);
                    var t = adminOnlyToggleObj.transform.Find("Label").GetComponent<Text>();
                    _adminOnlyToggle = adminOnlyToggleObj.GetComponent<Toggle>();
                    _adminOnlyToggle.GetComponent<RectTransform>().sizeDelta = new Vector2(0, 32);
                    _adminOnlyToggle.isOn = _basePlayer.PlayerSettings.AdminOnly;
                    _adminOnlyToggle.onValueChanged.AddListener(OnAdminOnlyToggleChanged);
                    GUIManager.Instance.ApplyTextStyle(t, GUIManager.Instance.AveriaSerifBold,
                        GUIManager.Instance.ValheimOrange);
                    _adminOnlyToggle.transform.Find("Background").GetComponent<RectTransform>().sizeDelta =
                        new Vector2(16, 16);
                    _adminOnlyToggle.transform.Find("Background/Checkmark").GetComponent<RectTransform>().sizeDelta =
                        new Vector2(16, 16);
                    t.text = "Admin Only";
                }

                /////////////////////////////
                /// AUDIO DISTANCE PANEL ///
                if (audioDistanceEnabled)
                {
                    var panelDistance = DefaultControls.CreatePanel(_oodResources);
                    panelDistance.transform.SetParent(contentTransform, false);
                    panelDistance.GetComponent<RectTransform>().sizeDelta = new Vector2(0, 85);
                    var imageDistance = panelDistance.GetComponent<Image>();
                    imageDistance.color = new Color(imageDistance.color.r, imageDistance.color.g, imageDistance.color.b, 0.0155f);

                    GUIManager.Instance.CreateText(
                        "Listening Distance",
                        panelDistance.transform,
                        new Vector2(0.5f, 0.5f),
                        new Vector2(0.5f, 0.5f),
                        new Vector2(8f, 10f),
                        GUIManager.Instance.AveriaSerifBold,
                        12,
                        GUIManager.Instance.ValheimOrange,
                        true,
                        Color.black,
                        120f,
                        18f,
                        false);
                    var str = _basePlayer.mAudio.maxDistance.ToString(CultureInfo.CurrentCulture);
                    var inputObj = GUIManager.Instance.CreateInputField(
                        panelDistance.transform,
                        new Vector2(0.5f, 0.5f),
                        new Vector2(0.5f, 0.5f),
                        new Vector2(0f, -10f),
                        InputField.ContentType.Standard,
                        str,
                        14,
                        110f,
                        26f);
                    var input = inputObj.GetComponent<InputField>();
                    input.contentType = InputField.ContentType.DecimalNumber;
                    input.onEndEdit.AddListener(OnAudioDistanceInputEndEdit);
                }
                ////////////////////////////
                /// MASTER VOLUME PANEL ///
                var panel = DefaultControls.CreatePanel(_oodResources);
                panel.transform.SetParent(contentTransform, false);
                panel.GetComponent<RectTransform>().sizeDelta = new Vector2(0, 85);
                var image = panel.GetComponent<Image>();
                image.color = new Color(image.color.r, image.color.g, image.color.b, 0.0155f);

                GUIManager.Instance.CreateText(
                    "Master Volume",
                    panel.transform,
                    new Vector2(0.5f, 0.5f),
                    new Vector2(0.5f, 0.5f),
                    new Vector2(8f, 6f),
                    GUIManager.Instance.AveriaSerifBold,
                    12,
                    GUIManager.Instance.ValheimOrange,
                    true,
                    Color.black,
                    120f,
                    18f,
                    false);
                var sliderObj = DefaultControls.CreateSlider(_oodResources);
                sliderObj.transform.SetParent(panel.transform, false);
                sliderObj.transform.localPosition = new Vector2(0, -10);
                var sliderRT = sliderObj.GetComponent<RectTransform>();
                sliderRT.localScale = new Vector3(0.65f, 1, 1);
                _masterVolumeSliderComponent = sliderObj.GetComponent<Slider>();
                _masterVolumeSliderComponent.maxValue = 15f;
                _masterVolumeSliderComponent.minValue = -15f;
                _masterVolumeSliderComponent.value = _basePlayer.PlayerSettings.PlayerType == CinemaPackage.MediaPlayers.CinemaScreen
                    ? OODConfig.MasterVolumeScreen.Value
                    : OODConfig.MasterVolumeMusicplayer.Value;
                _masterVolumeSliderComponent.onValueChanged.AddListener(OnMasterVolumeChanged);
                
                //////////////////////////////
                /// VERTICAL DROPOFF PANEL ///
                if (verticalDropOffEnabled)
                {
                    panel = DefaultControls.CreatePanel(_oodResources);
                    panel.transform.SetParent(contentTransform, false);
                    panel.GetComponent<RectTransform>().sizeDelta = new Vector2(0, 85);
                    image = panel.GetComponent<Image>();
                    image.color = new Color(image.color.r, image.color.g, image.color.b, 0.0155f);
                    GUIManager.Instance.CreateText(
                        "Vertical Drop-off",
                        panel.transform,
                        new Vector2(0.5f, 0.5f),
                        new Vector2(0.5f, 0.5f),
                        new Vector2(8f, 6f),
                        GUIManager.Instance.AveriaSerifBold,
                        12,
                        GUIManager.Instance.ValheimOrange,
                        true,
                        Color.black,
                        120f,
                        18f,
                        false);
                    var inputObj = GUIManager.Instance.CreateInputField(
                        panel.transform,
                        new Vector2(0.5f, 0.5f),
                        new Vector2(0.5f, 0.5f),
                        new Vector2(0f, -10f),
                        InputField.ContentType.Standard,
                        _basePlayer.PlayerSettings.VerticalDistanceDropoff.ToString(CultureInfo.CurrentCulture),
                        14,
                        110f,
                        26f);
                    var input = inputObj.GetComponent<InputField>();
                    input.contentType = InputField.ContentType.DecimalNumber;
                    input.onEndEdit.AddListener(OnVerticalDistanceDropoffInputEndEdit);
                    //////////////////////////////
                    /// VERTICAL DROPOFF POWER PANEL ///
                    panel = DefaultControls.CreatePanel(_oodResources);
                    panel.transform.SetParent(contentTransform, false);
                    panel.GetComponent<RectTransform>().sizeDelta = new Vector2(0, 85);
                    image = panel.GetComponent<Image>();
                    image.color = new Color(image.color.r, image.color.g, image.color.b, 0.0155f);
                    GUIManager.Instance.CreateText(
                        "Drop-off Power",
                        panel.transform,
                        new Vector2(0.5f, 0.5f),
                        new Vector2(0.5f, 0.5f),
                        new Vector2(8f, 6f),
                        GUIManager.Instance.AveriaSerifBold,
                        12,
                        GUIManager.Instance.ValheimOrange,
                        true,
                        Color.black,
                        120f,
                        18f,
                        false);
                    inputObj = GUIManager.Instance.CreateInputField(
                        panel.transform,
                        new Vector2(0.5f, 0.5f),
                        new Vector2(0.5f, 0.5f),
                        new Vector2(0f, -10f),
                        InputField.ContentType.Standard,
                        _basePlayer.PlayerSettings.DropoffPower.ToString(CultureInfo.CurrentCulture),
                        14,
                        110f,
                        26f);
                    input = inputObj.GetComponent<InputField>();
                    input.contentType = InputField.ContentType.DecimalNumber;
                    input.onEndEdit.AddListener(OnDropoffPowerInputEndEdit);
                }
                //////////////////////////////
                /// SPEAKERS PANEL ///
                panel = DefaultControls.CreatePanel(_oodResources);
                panel.transform.SetParent(contentTransform, false);
                panel.GetComponent<RectTransform>().sizeDelta = new Vector2(0, 85);
                image = panel.GetComponent<Image>();
                image.color = new Color(image.color.r, image.color.g, image.color.b, 0.0155f);
                var speakerTextObj = GUIManager.Instance.CreateText(
                    "Speakers: 0",
                    panel.transform,
                    new Vector2(0.5f, 0.5f),
                    new Vector2(0.5f, 0.5f),
                    new Vector2(8f, 6f),
                    GUIManager.Instance.AveriaSerifBold,
                    12,
                    GUIManager.Instance.ValheimOrange,
                    true,
                    Color.black,
                    120f,
                    18f,
                    false);
                _speakerText = speakerTextObj.GetComponent<Text>();
                var unlinkAllButton = GUIManager.Instance.CreateButton(
                    "Unlink All",
                    panel.transform,
                    new Vector2(0.5f, 0.5f),
                    new Vector2(0.5f, 0.5f),
                    new Vector2(0f, -10f),
                    120f,
                    26f);
                var unlinkAllButtonAction = unlinkAllButton.GetComponent<Button>();
                unlinkAllButtonAction.onClick.AddListener(() => _basePlayer.UnlinkAllSpeakers());
                //////////////////////////////
                /// TIME PANEL ///
                panel = DefaultControls.CreatePanel(_oodResources);
                panel.transform.SetParent(contentTransform, false);
                panel.GetComponent<RectTransform>().sizeDelta = new Vector2(0, 85);
                image = panel.GetComponent<Image>();
                image.color = new Color(image.color.r, image.color.g, image.color.b, 0.0155f);
                var timeInputObj = GUIManager.Instance.CreateInputField(
                    panel.transform,
                    new Vector2(0.5f, 0.5f),
                    new Vector2(0.5f, 0.5f),
                    new Vector2(0f, -2f),
                    InputField.ContentType.Standard,
                    _basePlayer.mScreen.time.ToString(CultureInfo.CurrentCulture),
                    14,
                    110f,
                    26f);
                var timeInput = timeInputObj.GetComponent<InputField>();
                timeInput.contentType = InputField.ContentType.DecimalNumber;
                var submitTimeButton = GUIManager.Instance.CreateButton(
                    "Set Time",
                    panel.transform,
                    new Vector2(0.5f, 0.5f),
                    new Vector2(0.5f, 0.5f),
                    new Vector2(120f, -2f),
                    120f,
                    26f);
                var submitTimeButtonAction = submitTimeButton.GetComponent<Button>();
                submitTimeButtonAction.onClick.AddListener(() =>
                {
                    if(_basePlayer.PlayerSettings.CurrentMode == PlayerSettings.PlayerMode.Dynamic) return;
                    if (timeInput.text.Length < 1) return;
                    var parse = float.Parse(timeInput.text);
                    _rpc.SendData(0, CinemaPackage.RPCDataType.SyncTime, _basePlayer.PlayerSettings.PlayerType, _basePlayer.MediaPlayerID, _basePlayer.gameObject.transform.position, parse);
                });
                //////////////////////////////
                /// RESYNC PANEL ///
                panel = DefaultControls.CreatePanel(_oodResources);
                panel.transform.SetParent(contentTransform, false);
                panel.GetComponent<RectTransform>().sizeDelta = new Vector2(0, 75);
                image = panel.GetComponent<Image>();
                image.color = new Color(image.color.r, image.color.g, image.color.b, 0.0155f);
                var reloadPlayerObj = GUIManager.Instance.CreateButton(
                    "Reload Player",
                    panel.transform,
                    new Vector2(0.5f, 0.5f),
                    new Vector2(0.5f, 0.5f),
                    new Vector2(-85f, 0),
                    120f,
                    26f);
                var reloadPlayerButton = reloadPlayerObj.GetComponent<Button>();
                reloadPlayerButton.onClick.AddListener(() =>
                {
                    _basePlayer.LoadZDO();
                });
                var resyncPlayerObj = GUIManager.Instance.CreateButton(
                    "Sync Time",
                    panel.transform,
                    new Vector2(0.5f, 0.5f),
                    new Vector2(0.5f, 0.5f),
                    new Vector2(85f, 0),
                    120f,
                    26f);
                var resyncPlayerButton = resyncPlayerObj.GetComponent<Button>();
                resyncPlayerButton.onClick.AddListener(() =>
                {
                    _basePlayer.SendRequestTimeSync_RPC();
                });
            }
        }

        private void ToggleLock()
        {
            _basePlayer.PlayerSettings.IsLocked = !_basePlayer.PlayerSettings.IsLocked;
            UpdateLockIcon();
            _basePlayer.SaveZDO();
            _rpc.SendData(0, CinemaPackage.RPCDataType.UpdateZDO, _basePlayer.PlayerSettings.PlayerType, _basePlayer.MediaPlayerID, _basePlayer.gameObject.transform.position, (float)_basePlayer.mScreen.time);
        }

        private void UpdateLockIcon()
        {
            if (URLPanelObj)
            {
                if (_basePlayer.PlayerSettings.IsLocked)
                {
                    LockedIconObj.SetActive(true);
                    UnlockedIconObj.SetActive(false);
                }
                else
                {
                    LockedIconObj.SetActive(false);
                    UnlockedIconObj.SetActive(true);
                }
            }
        }

        private void OnVolumeSliderChanged(float vol)
        {
            _basePlayer.mAudio.volume = vol;
            _basePlayer.PlayerSettings.Volume = vol;
            if (vol <= 0f)
            {
                _unmutedVolumeObj.SetActive(false);
                _mutedVolumeObj.SetActive(true);
            }
            else
            {
                _unmutedVolumeObj.SetActive(true);
                _mutedVolumeObj.SetActive(false);
            }
        }

        private void OnAudioDistanceInputEndEdit(string input)
        {
            if (input.Length < 1) return;
            var parse = float.Parse(input);
            if (parse == 0f) parse = 0.01f;
            if (parse > OODConfig.MaxListeningDistance.Value && !SynchronizationManager.Instance.PlayerIsAdmin)
                parse = OODConfig.MaxListeningDistance.Value;
            _basePlayer.mAudio.maxDistance = parse;
            _basePlayer.SaveZDO();
            _basePlayer.SendUpdateZDO_RPC();
        }

        private void OnVerticalDistanceDropoffInputEndEdit(string input)
        {
            if (input.Length < 1) return;
            var parse = float.Parse(input);
            _basePlayer.PlayerSettings.VerticalDistanceDropoff = parse;
            _basePlayer.SaveZDO();
            _basePlayer.SendUpdateZDO_RPC();
        }

        private void OnDropoffPowerInputEndEdit(string input)
        {
            if (input.Length < 1) return;
            var parse = float.Parse(input);
            _basePlayer.PlayerSettings.DropoffPower = parse;
            _basePlayer.SaveZDO();
            _basePlayer.SendUpdateZDO_RPC();
        }

        private void OnAdminOnlyToggleChanged(bool toggleValue)
        {
            _basePlayer.PlayerSettings.AdminOnly = toggleValue;
            _basePlayer.SaveZDO();
            _basePlayer.SendUpdateZDO_RPC();
        }

        private void OnMasterVolumeChanged(float vol)
        {
            if (_basePlayer.PlayerSettings.PlayerType == CinemaPackage.MediaPlayers.CinemaScreen)
                OODConfig.MasterVolumeScreen.Value = vol;
            else if(_basePlayer.PlayerSettings.PlayerType == CinemaPackage.MediaPlayers.BeltPlayer || _basePlayer.PlayerSettings.PlayerType == CinemaPackage.MediaPlayers.CartPlayer)
                OODConfig.MasterVolumeTransport.Value = vol;
            else 
                OODConfig.MasterVolumeMusicplayer.Value = vol;
            
            OODConfig.ReadAndWriteConfigValues(OdinOnDemandPlugin.OdinConfig);
        }
        
        public void ToggleMainPanel()
        {
            _basePlayer.UpdateZDO();
            CreateMainGUI();
            UpdateLockIcon();
            UpdateLoopIndicator();
            UpdateSpeakerCount();
            
            if (_basePlayer.PlayerSettings.IsPlayingPlaylist)
            {
                if(_urlInputField) _urlInputField.text = _basePlayer.PlaylistURL;
            }
            else if (_basePlayer.UnparsedURL != null)
            { 
                if(_urlInputField)   _urlInputField.text = _basePlayer.UnparsedURL;
            }
            _basePlayer.PlayerSettings.IsGuiActive = !URLPanelObj.activeSelf;
            URLPanelObj.SetActive(_basePlayer.PlayerSettings.IsGuiActive);
            if(_basePlayer.PlayerSettings.CurrentMode == PlayerSettings.PlayerMode.Dynamic)
                _dynamicPanelObj.SetActive(_basePlayer.PlayerSettings.IsGuiActive);
            // Toggle input
            GUIManager.BlockInput(_basePlayer.PlayerSettings.IsGuiActive);
        }

        private void ToggleShuffle()
        {
            _basePlayer.PlayerSettings.IsShuffling = !_basePlayer.PlayerSettings.IsShuffling;
            if (_basePlayer.PlayerSettings.IsShuffling)
            {
                var rnd = new Random();
                _basePlayer.PreShufflePlaylist = _basePlayer.CurrentPlaylist;
                _basePlayer.CurrentPlaylist = _basePlayer.CurrentPlaylist.OrderBy(x => rnd.Next()).ToList();
                _basePlayer.PlaylistPosition = 0;
                if (OODConfig.DebugEnabled.Value) Logger.LogDebug("Shuffled Playlist");

                ToggleShuffleObj.GetComponentInChildren<Text>().text = "Y";
            }
            else
            {
                var pos = _basePlayer.PreShufflePlaylist.FindIndex(v => v == _basePlayer.CurrentPlaylist.ElementAt(_basePlayer.PlaylistPosition));
                _basePlayer.PlaylistPosition = pos;
                _basePlayer.CurrentPlaylist = _basePlayer.PreShufflePlaylist;
                ToggleShuffleObj.GetComponentInChildren<Text>().text = "N";
                _basePlayer.PreShufflePlaylist = null;
            }
        }

        public void ToggleLoop()
        {
            _basePlayer.PlayerSettings.IsLooping = !_basePlayer.PlayerSettings.IsLooping;
            if (!_basePlayer.PlayerSettings.IsPlayingPlaylist)
            {
                _basePlayer.mAudio.loop = _basePlayer.PlayerSettings.IsLooping;
                ((IPlayer)_basePlayer).mScreen.isLooping = _basePlayer.PlayerSettings.IsLooping;
                _rpc.SendData(0, CinemaPackage.RPCDataType.SetLoop, _basePlayer.PlayerSettings.PlayerType, _basePlayer.MediaPlayerID, _basePlayer.gameObject.transform.position, (float)_basePlayer.mScreen.time , "",
                    CinemaPackage.PlayerStatus.NULL, 1, _basePlayer.PlayerSettings.IsLooping);
            }

            _basePlayer.SaveZDO();
            UpdateLoopIndicator();
        }

        public void UpdateLoopIndicator()
        {
            _toggleLoopObj.GetComponentInChildren<Text>().text = _basePlayer.PlayerSettings.IsLooping ? "Y" : "N";
        }

        public void SetLoop(bool looping)
        {
            _basePlayer.mAudio.loop = looping;
            ((IPlayer)_basePlayer).mScreen.isLooping = looping;
            if (URLPanelObj)
                if (URLPanelObj.activeInHierarchy)
                    _toggleLoopObj.GetComponentInChildren<Text>().text = looping ? "Y" : "N";
        }
        
        private Coroutine _debounceCoroutine;
        
        public void OnClickSkipPlaylistTrack()
        {
            if (_basePlayer.PlaylistPosition + 1 < _basePlayer.CurrentPlaylist.Count)
            {
                _basePlayer.PlaylistPosition++;
                if (_debounceCoroutine != null)
                {
                    _basePlayer.StopPlayerCoroutine(_debounceCoroutine);
                }
                _debounceCoroutine = _basePlayer.StartPlayerCoroutine(DebounceSkipVideo());
            }
            else if (_basePlayer.PlayerSettings.IsLooping)
            {
                _basePlayer.PlaylistPosition = 0;
                if (_debounceCoroutine != null)
                {
                    _basePlayer.StopPlayerCoroutine(_debounceCoroutine);
                }
                _debounceCoroutine = _basePlayer.StartPlayerCoroutine(DebounceSkipVideo());
            }
        }

        public const float DebounceTime = 0.3f; // time to wait for another input
        private const bool IsWaiting = false;
        private Text _loadingIndicatorText;
        private Transform _dynamicContentTransform;
        private GameObject _urlTabButtonObj;
        private GameObject _dynamicTabButtonObj;
        private InputField _urlInputField;
        private Text _speakerText;

        public void SetInputFieldText(string text)
        {
            if (_urlInputField) _urlInputField.text = text;
        }

        IEnumerator DebounceSkipVideo()
        {
            UpdatePlaylistInfo();
            yield return new WaitForSeconds(DebounceTime);
            FinalizeSkip();
        }
        
        void FinalizeSkip()
        {
            if (IsWaiting) return;  // don't play if we're still in the debounce period
            
            _basePlayer.SetURL(_basePlayer.CurrentPlaylist.ElementAt(_basePlayer.PlaylistPosition).Url);
        }

        public void PreviousPlaylistTrack()
        {
            if (_basePlayer.PlaylistPosition - 1 >= 0)
            {
                _basePlayer.PlaylistPosition--;
                _basePlayer.SetURL(_basePlayer.CurrentPlaylist.ElementAt(_basePlayer.PlaylistPosition).Url);
            }
            else if (_basePlayer.PlayerSettings.IsLooping)
            {
                _basePlayer.PlaylistPosition = _basePlayer.CurrentPlaylist.Count() - 1;
                _basePlayer.SetURL(_basePlayer.CurrentPlaylist.ElementAt(_basePlayer.PlaylistPosition).Url);
            }
        }
        
        public void SetLoadingIndicatorText(string text)
        {
            if(LoadingIndicatorObj) _loadingIndicatorText.text = text;
        }
        
        public void SetLoadingIndicatorActive(bool active)
        {
            if(LoadingIndicatorObj) LoadingIndicatorObj.SetActive(active);
        }
        
        public void ResetLoadingIndicator()
        {
            if (LoadingIndicatorObj)
            {
                _loadingIndicatorText.text = "Processing";
                LoadingIndicatorObj.SetActive(false);
            }
            if (_basePlayer.ScreenUICanvasObj && _basePlayer.LoadingCircleObj)
            {
                _basePlayer. LoadingCircleObj.SetActive(false);
            }
        }
        private GameObject CreateUISpriteButton(string iconName, Transform parentTransform, Vector2 anchorMin, Vector2 anchorMax, Vector2 position, float width, float height, Action onClickAction, bool imageOn = true)
        {
            // Create the button
            var button = GUIManager.Instance.CreateButton(
                "",
                parentTransform,
                anchorMin,
                anchorMax,
                position,
                width,
                height);

            // Create and set up the icon
            var iconObj = new GameObject("icon")
            {
                transform =
                {
                    parent = button.transform,
                    localPosition = Vector3.zero,
                    localScale = new Vector3(0.2f, 0.2f, 0.2f)
                }
            };

            var icon = iconObj.AddComponent<Image>();
            icon.sprite = OdinOnDemandPlugin.UISprites[iconName];

            // Attach the click event listener
            var buttonComponent = button.GetComponent<Button>();
            
            if (onClickAction != null)
            {
                buttonComponent.onClick.AddListener(() => onClickAction());
            }
            
            if(!imageOn) button.GetComponent<Image>().enabled = false;

            return button;
        }
        private GameObject CreateDynamicAudioEntry(string text, Sprite sp, Transform parentTransform, Vector2 position, UnityAction<bool> onValueChanged)
        {
            var panelDistance = DefaultControls.CreatePanel(_oodResources);
            panelDistance.transform.SetParent(parentTransform, false);
            var imageDistance = panelDistance.GetComponent<Image>();
            imageDistance.color = new Color(imageDistance.color.r, imageDistance.color.g, imageDistance.color.b, 0.0155f);
            
            var toggle     = DefaultControls.CreateToggle(_oodResources);
            toggle.transform.SetParent(panelDistance.transform, false);
            toggle.transform.localPosition = position;
             var toggleRT = toggle.GetComponent<RectTransform>();
             toggleRT.anchorMin = new Vector2(0f, 1f);
             toggleRT.anchorMax = new Vector2(1f, 1f);
             toggleRT.offsetMin = new Vector2(12.5f, -37.5f);
             toggleRT.offsetMax = new Vector2(-12.5f, -2.5f);
            var textComp = toggle.transform.Find("Label").GetComponent<Text>();
            textComp.text = text;
            _entryToggle = toggle.GetComponent<Toggle>();
            _entryToggle.isOn = false;
            _entryToggle.group = _entryToggleGroup;
            _entryToggle.onValueChanged.AddListener(onValueChanged);
            GUIManager.Instance.ApplyTextStyle(textComp, GUIManager.Instance.AveriaSerifBold,
                GUIManager.Instance.ValheimOrange);
            if (sp != null)
            {
                _entryToggle.transform.Find("Background").GetComponent<Image>().sprite = sp;
            }
            else
            {
                _entryToggle.transform.Find("Background").gameObject.SetActive(false);
            }
            
            //compare title to current playing track
            if(_basePlayer.PlayerSettings?.DynamicStation?.Title == text)
            {
                _entryToggle.isOn = true;
            }
            
            return toggle;
        }
    }

    public class CustomDragHandler : MonoBehaviour, IDragHandler
    {
        public GameObject parentObj;

        public void OnDrag(PointerEventData eventData)
        {
            if (parentObj)
            {
                parentObj.transform.position = gameObject.transform.position;
            }
        }
    }
}