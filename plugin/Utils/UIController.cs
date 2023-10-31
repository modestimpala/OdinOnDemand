using System.Collections;
using System.Globalization;
using System.Linq;
using Jotunn.Managers;
using OdinOnDemand.Components;
using OdinOnDemand.Utils;
using UnityEngine;
using UnityEngine.UI;
using Logger = Jotunn.Logger;
using Random = System.Random;

namespace OdinOnDemand.Utils
{
    public class UIController
    {
        private MediaPlayerComponent mediaPlayerComponent;
        
        internal GameObject loadingIndicatorObj;
        internal GameObject lockedIconObj;
        private Slider masterVolumeSliderComponent;
        private GameObject mutedVolumeObj;
        private DefaultControls.Resources oodResources;
        private GameObject playlistIndexObj;
        private GameObject playlistStringObj;
        internal Text playlistTrackText;
        private GameObject previousPlaylistTrackObj;
        internal GameObject selectionPanelObj;
        private GameObject settingsCogButton;
        private GameObject settingsPanelObj;
        private RectTransform settingsPanelRT;
        private GameObject skipPlaylistTrackObj;
        private GameObject toggleLoopObj;
        internal GameObject toggleShuffleObj;
        internal GameObject toggleShuffleTextObj;
        internal GameObject unlockedIconObj;
        private GameObject unmutedVolumeObj;
        private GameObject urlInputFieldObj;
        private Slider volumeSlider;
        private Toggle adminOnlyToggle;
        
        private Toggle autoplayToggle;
        
        // Loading messages
        public readonly string[] loadingMessages = { "Processing", "Processing.", "Processing..", "Processing..." };
        
        private RpcHandler rpc  = OdinOnDemandPlugin.RPCHandlers;
        private GameObject playButton;
        private GameObject pauseButton;
        private GameObject stopButton;
        private GameObject trackForwardButton;
        private GameObject toggleMuteButton;
        private GameObject volumeSliderObj;
        private GameObject toogleLoopButton;
        private GameObject loopTextObj;
        private GameObject toggleShuffle;
        private GameObject toggleShuffleText;
        
        private GameObject unlinkButton;
        private GameObject linkedTextObj;
        private Text linkedText;

        public UIController(MediaPlayerComponent mediaPlayerComponent)
        {
            this.mediaPlayerComponent = mediaPlayerComponent;
        }

        public void Initialize()
        {
            oodResources = new DefaultControls.Resources
            {
                knob = OdinOnDemandPlugin.UISprites["handle"],
                background = OdinOnDemandPlugin.UISprites["background"],
                standard = OdinOnDemandPlugin.UISprites["fill"],
                checkmark = OdinOnDemandPlugin.UISprites["checkmark"]
            };
        }

        private void ToggleMute()
        {
            if (mediaPlayerComponent.mAudio.volume > 0f)
            {
                if (mutedVolumeObj != null)
                {
                    unmutedVolumeObj.SetActive(false);
                    mutedVolumeObj.SetActive(true);
                }

                mediaPlayerComponent.PlayerSettings.MuteVol = mediaPlayerComponent.mAudio.volume;
                mediaPlayerComponent.PlayerSettings.Volume = 0f;
                //float floatVol = ((float)volume) / 100f;
                mediaPlayerComponent.mAudio.volume = mediaPlayerComponent.PlayerSettings.Volume;
                volumeSlider.value = mediaPlayerComponent.PlayerSettings.Volume;
            }
            else
            {
                if (mutedVolumeObj != null)
                {
                    unmutedVolumeObj.SetActive(true);
                    mutedVolumeObj.SetActive(false);
                }

                mediaPlayerComponent.PlayerSettings.Volume = mediaPlayerComponent.PlayerSettings.MuteVol;
                mediaPlayerComponent.mAudio.volume = mediaPlayerComponent.PlayerSettings.Volume;
                volumeSlider.value = mediaPlayerComponent.PlayerSettings.Volume;
            }
        }

        internal void UpdatePlaylistInfo()
        {
            if (mediaPlayerComponent.PlayerSettings.IsPlayingPlaylist)
                if (selectionPanelObj)
                {
                    var length = mediaPlayerComponent.CurrentPlaylist.ElementAt(mediaPlayerComponent.PlaylistPosition).Title.Length;
                    if (length > 14) length = 14;
                    if (!mediaPlayerComponent.PlayerSettings.IsShuffling)
                    {
                        mediaPlayerComponent.PlaylistString = mediaPlayerComponent.PlaylistString = mediaPlayerComponent.CurrentPlaylist.ElementAt(mediaPlayerComponent.PlaylistPosition).Title;
                        mediaPlayerComponent.PlaylistString = "Playing '" + mediaPlayerComponent.CurrentPlaylist.ElementAt(mediaPlayerComponent.PlaylistPosition).Title
                            .Substring(0, length) + "...' ";
                        playlistIndexObj.GetComponent<Text>().text = mediaPlayerComponent.PlaylistPosition + 1 + "/" + mediaPlayerComponent.CurrentPlaylist.Count;
                        playlistTrackText.text = mediaPlayerComponent.PlaylistString;
                    }
                    else
                    {
                        mediaPlayerComponent.PlaylistString = mediaPlayerComponent.PlaylistString = mediaPlayerComponent.CurrentPlaylist.ElementAt(mediaPlayerComponent.PlaylistPosition).Title;
                        mediaPlayerComponent.PlaylistString = "Playing '" + mediaPlayerComponent.CurrentPlaylist.ElementAt(mediaPlayerComponent.PlaylistPosition).Title
                            .Substring(0, length) + "...' ";
                        playlistIndexObj.GetComponent<Text>().text = mediaPlayerComponent.PlaylistPosition + 1 + "/" + mediaPlayerComponent.CurrentPlaylist.Count + ", (shuffled)";
                        playlistTrackText.text = mediaPlayerComponent.PlaylistString;
                    }
                }
        }

        public void UpdatePlaylistUI()
        {
            playlistStringObj.SetActive(mediaPlayerComponent.PlayerSettings.IsPlayingPlaylist);
            skipPlaylistTrackObj.SetActive(mediaPlayerComponent.PlayerSettings.IsPlayingPlaylist);
            previousPlaylistTrackObj.SetActive(mediaPlayerComponent.PlayerSettings.IsPlayingPlaylist);
            toggleShuffleObj.SetActive(mediaPlayerComponent.PlayerSettings.IsPlayingPlaylist);
            toggleShuffleTextObj.SetActive(mediaPlayerComponent.PlayerSettings.IsPlayingPlaylist);
            playlistIndexObj.SetActive(mediaPlayerComponent.PlayerSettings.IsPlayingPlaylist);
        }

        public void SetPlaylistUIActive(bool state)
        {
            playlistStringObj.SetActive(state);
            skipPlaylistTrackObj.SetActive(state);
            previousPlaylistTrackObj.SetActive(state);
            toggleShuffleObj.SetActive(state);
            toggleShuffleTextObj.SetActive(state);
            playlistIndexObj.SetActive(state);
        }

        internal IEnumerator UnavailableIndicator(string message)
        {
            if (loadingIndicatorObj)
            {
                loadingIndicatorObj.GetComponent<Text>().text = message;
                loadingIndicatorObj.SetActive(true);
                yield return new WaitForSeconds(2);
                loadingIndicatorObj.SetActive(false);
            }
        }

        private void CreateMainGUI()
        {
            if (!selectionPanelObj)
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

                selectionPanelObj = GUIManager.Instance.CreateWoodpanel(
                    GUIManager.CustomGUIFront.transform,
                    new Vector2(0.5f, 0.5f),
                    new Vector2(0.5f, 0.5f),
                    new Vector2(0f, 0f),
                    375f,
                    155f,
                    true);
                selectionPanelObj.SetActive(false);
                var closeButton = GUIManager.Instance.CreateButton(
                    "X",
                    selectionPanelObj.transform,
                    new Vector2(0.5f, 0.5f),
                    new Vector2(0.5f, 0.5f),
                    new Vector2(168f, 90f),
                    32f,
                    32f);


                urlInputFieldObj = GUIManager.Instance.CreateInputField(
                    selectionPanelObj.transform,
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
                    selectionPanelObj.transform,
                    new Vector2(0.5f, 0.5f),
                    new Vector2(0.5f, 0.5f),
                    new Vector2(130f, 50f),
                    64f,
                    32f);

                /////////////////// MEDIA CONTROL ///////////////////////
                //PLAY
                playButton = GUIManager.Instance.CreateButton(
                    "",
                    selectionPanelObj.transform,
                    new Vector2(0.5f, 0.5f),
                    new Vector2(0.5f, 0.5f),
                    new Vector2(-150f, 10f),
                    34f,
                    34f);
                var iconObj = new GameObject("icon")
                {
                    transform =
                    {
                        parent = playButton.transform,
                        localPosition = new Vector3(0, 0, 0),
                        localScale = new Vector3(0.2f, 0.2f, 0.2f)
                    }
                };
                var icon = iconObj.AddComponent<Image>();
                icon.sprite = OdinOnDemandPlugin.UISprites["play"];

                // PAUSE
                pauseButton = GUIManager.Instance.CreateButton(
                    "",
                    selectionPanelObj.transform,
                    new Vector2(0.5f, 0.5f),
                    new Vector2(0.5f, 0.5f),
                    new Vector2(-112f, 10f),
                    34f,
                    34f);
                iconObj = new GameObject("icon")
                {
                    transform =
                    {
                        parent = pauseButton.transform,
                        localPosition = new Vector3(0, 0, 0),
                        localScale = new Vector3(0.2f, 0.2f, 0.2f)
                    }
                };
                icon = iconObj.AddComponent<Image>();
                icon.sprite = OdinOnDemandPlugin.UISprites["pause"];

                //STOP
                stopButton = GUIManager.Instance.CreateButton(
                    "",
                    selectionPanelObj.transform,
                    new Vector2(0.5f, 0.5f),
                    new Vector2(0.5f, 0.5f),
                    new Vector2(-74f, 10f),
                    34f,
                    34f);
                iconObj = new GameObject("icon")
                {
                    transform =
                    {
                        parent = stopButton.transform,
                        localPosition = new Vector3(0, 0, 0),
                        localScale = new Vector3(0.2f, 0.2f, 0.2f)
                    }
                };
                icon = iconObj.AddComponent<Image>();
                icon.sprite = OdinOnDemandPlugin.UISprites["stop"];

                //FAST FORWARD
                trackForwardButton = GUIManager.Instance.CreateButton(
                    "",
                    selectionPanelObj.transform,
                    new Vector2(0.5f, 0.5f),
                    new Vector2(0.5f, 0.5f),
                    new Vector2(-36f, 10f),
                    34f,
                    34f);
                iconObj = new GameObject("icon")
                {
                    transform =
                    {
                        parent = trackForwardButton.transform,
                        localPosition = new Vector3(0, 0, 0),
                        localScale = new Vector3(0.2f, 0.2f, 0.2f)
                    }
                };
                icon = iconObj.AddComponent<Image>();
                icon.sprite = OdinOnDemandPlugin.UISprites["fastforward"];

                //MUTE
                toggleMuteButton = GUIManager.Instance.CreateButton(
                    "",
                    selectionPanelObj.transform,
                    new Vector2(0.5f, 0.5f),
                    new Vector2(0.5f, 0.5f),
                    new Vector2(150f, 15f),
                    32f,
                    32f);
                iconObj = new GameObject("iconUnmuted")
                {
                    transform =
                    {
                        parent = toggleMuteButton.transform,
                        localPosition = new Vector3(0, 0, 0),
                        localScale = new Vector3(0.2f, 0.2f, 0.2f)
                    }
                };
                icon = iconObj.AddComponent<Image>();
                icon.sprite = OdinOnDemandPlugin.UISprites["volume"];
                unmutedVolumeObj = iconObj;
                iconObj = new GameObject("iconMuted")
                {
                    transform =
                    {
                        parent = toggleMuteButton.transform,
                        localPosition = new Vector3(0, 0, 0),
                        localScale = new Vector3(0.2f, 0.2f, 0.2f)
                    }
                };
                icon = iconObj.AddComponent<Image>();
                icon.sprite = OdinOnDemandPlugin.UISprites["mute"];
                mutedVolumeObj = iconObj;
                toggleMuteButton.GetComponent<Image>().enabled = false;
                mutedVolumeObj.SetActive(false);

                //toggle mute action
                var toggleMuteAction = toggleMuteButton.GetComponent<Button>();
                toggleMuteAction.onClick.AddListener(ToggleMute);

                //////////////////////
                ///// VOL SLIDER /////
                volumeSliderObj = DefaultControls.CreateSlider(oodResources);
                volumeSliderObj.transform.SetParent(selectionPanelObj.transform);
                volumeSliderObj.transform.localPosition = new Vector3(100f, 15f, 0f);
                volumeSliderObj.transform.localScale = new Vector3(0.4f, 1.4f, 1.17f);
                var slider = volumeSliderObj.GetComponent<Slider>();
                slider.value = mediaPlayerComponent.PlayerSettings.Volume;
                volumeSlider = slider;

                slider.onValueChanged.AddListener(OnVolumeSliderChanged);


                var loadingIndicator = GUIManager.Instance.CreateText(
                    "Processing",
                    selectionPanelObj.transform,
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

                toogleLoopButton = GUIManager.Instance.CreateButton(
                    "N",
                    selectionPanelObj.transform,
                    new Vector2(0.5f, 0.5f),
                    new Vector2(0.5f, 0.5f),
                    new Vector2(154f, -14f),
                    25f,
                    25f);
                loopTextObj = GUIManager.Instance.CreateText(
                    "Loop",
                    selectionPanelObj.transform,
                    new Vector2(0.5f, 0.5f),
                    new Vector2(0.5f, 0.5f),
                    new Vector2(120f, -24f),
                    GUIManager.Instance.AveriaSerifBold,
                    18,
                    GUIManager.Instance.ValheimOrange,
                    true,
                    Color.black,
                    42f,
                    40f,
                    false);

                toggleShuffle = GUIManager.Instance.CreateButton(
                    "N",
                    selectionPanelObj.transform,
                    new Vector2(0.5f, 0.5f),
                    new Vector2(0.5f, 0.5f),
                    new Vector2(154f, -43f),
                    25f,
                    25f);
                toggleShuffleText = GUIManager.Instance.CreateText(
                    "Shuffle",
                    selectionPanelObj.transform,
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

                toggleShuffleObj = toggleShuffle;
                toggleShuffleTextObj = toggleShuffleText;
                toggleShuffle.SetActive(false);
                toggleShuffleTextObj.SetActive(false);

                var toggleShuffleButton = toggleShuffle.GetComponent<Button>();
                toggleShuffleButton.onClick.AddListener(ToggleShuffle);

                var toogleLoopButtonAction = toogleLoopButton.GetComponent<Button>();
                toogleLoopButtonAction.onClick.AddListener(ToggleLoop);
                toggleLoopObj = toogleLoopButton;
                var inputUrl = urlInputFieldObj.GetComponent<InputField>();
                loadingIndicatorObj = loadingIndicator;
                loadingIndicatorText = loadingIndicatorObj.GetComponent<Text>();


                //create button listeners
                var button = closeButton.GetComponent<Button>();
                button.onClick.AddListener(ToggleMainPanel);

                //set vid
                var setButtonAction = setButton.GetComponent<Button>();
                setButtonAction.onClick.AddListener(() =>
                {
                    mediaPlayerComponent.SetURL(inputUrl.text);
                    mediaPlayerComponent.CurrentPlaylist = null;
                    mediaPlayerComponent.PlayerSettings.IsPlayingPlaylist = false;
                });
                //play 
                var playButtonAction = playButton.GetComponent<Button>();
                playButtonAction.onClick.AddListener(() => { mediaPlayerComponent.Play(); });
                //pause
                var pauseButtonAction = pauseButton.GetComponent<Button>();
                pauseButtonAction.onClick.AddListener(() => { mediaPlayerComponent.Pause(); });
                //stop
                var stopButtonAction = stopButton.GetComponent<Button>();
                stopButtonAction.onClick.AddListener(() => { mediaPlayerComponent.Stop(); });

                var trackForwardAction = trackForwardButton.GetComponent<Button>();
                trackForwardAction.onClick.AddListener(ToggleTrackForward);

                var playlistStringText = GUIManager.Instance.CreateText(
                    "",
                    selectionPanelObj.transform,
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
                playlistStringObj = playlistStringText;

                var playlistIndexText = GUIManager.Instance.CreateText(
                    "0/0",
                    selectionPanelObj.transform,
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
                playlistIndexObj = playlistIndexText;

                playlistTrackText = playlistStringObj.GetComponent<Text>();

                var skipPlaylistTrackButton = GUIManager.Instance.CreateButton(
                    "",
                    selectionPanelObj.transform,
                    new Vector2(0.5f, 0.5f),
                    new Vector2(0.5f, 0.5f),
                    new Vector2(44f, 10f),
                    34f,
                    34f);
                iconObj = new GameObject("icon")
                {
                    transform =
                    {
                        parent = skipPlaylistTrackButton.transform,
                        localPosition = new Vector3(0, 0, 0),
                        localScale = new Vector3(0.2f, 0.2f, 0.2f)
                    }
                };
                icon = iconObj.AddComponent<Image>();
                icon.sprite = OdinOnDemandPlugin.UISprites["next"];
                skipPlaylistTrackButton.SetActive(false);
                skipPlaylistTrackObj = skipPlaylistTrackButton;

                var previousPlaylistTrackButton = GUIManager.Instance.CreateButton(
                    "",
                    selectionPanelObj.transform,
                    new Vector2(0.5f, 0.5f),
                    new Vector2(0.5f, 0.5f),
                    new Vector2(6F, 10f),
                    34f,
                    34f);
                iconObj = new GameObject("icon")
                {
                    transform =
                    {
                        parent = previousPlaylistTrackButton.transform,
                        localPosition = new Vector3(0, 0, 0),
                        localScale = new Vector3(0.2f, 0.2f, 0.2f)
                    }
                };
                icon = iconObj.AddComponent<Image>();
                icon.sprite = OdinOnDemandPlugin.UISprites["previous"];
                previousPlaylistTrackButton.SetActive(false);
                previousPlaylistTrackObj = previousPlaylistTrackButton;

                //playlistScrollingText = playlistStringText.AddComponent<ScrollingText>();

                var skipPlaylistTrackAction = skipPlaylistTrackButton.GetComponent<Button>();
                skipPlaylistTrackAction.onClick.AddListener(OnClickSkipPlaylistTrack);
                var previousPlaylistTrackAction = previousPlaylistTrackButton.GetComponent<Button>();
                previousPlaylistTrackAction.onClick.AddListener(PreviousPlaylistTrack);


                // LOCK BUTTON
                var lockButton = GUIManager.Instance.CreateButton(
                    "",
                    selectionPanelObj.transform,
                    new Vector2(0.5f, 0.5f),
                    new Vector2(0.5f, 0.5f),
                    new Vector2(130f, 90f),
                    32f,
                    32f);
                lockButton.GetComponent<Image>().enabled = false;
                iconObj = new GameObject("lockedIcon")
                {
                    transform =
                    {
                        parent = lockButton.transform,
                        localPosition = new Vector3(0, 0, 0),
                        localScale = new Vector3(0.2f, 0.2f, 0.2f)
                    }
                };
                icon = iconObj.AddComponent<Image>();
                icon.sprite = OdinOnDemandPlugin.UISprites["lock"];
                lockedIconObj = iconObj;
                iconObj = new GameObject("unlockedIcon")
                {
                    transform =
                    {
                        parent = lockButton.transform,
                        localPosition = new Vector3(0, 0, 0),
                        localScale = new Vector3(0.2f, 0.2f, 0.2f)
                    }
                };
                icon = iconObj.AddComponent<Image>();
                icon.sprite = OdinOnDemandPlugin.UISprites["unlock"];
                unlockedIconObj = iconObj;
                unlockedIconObj.SetActive(false);

                var lockButtonAction = lockButton.GetComponent<Button>();
                lockButtonAction.onClick.AddListener(ToggleLock);

                settingsCogButton = GUIManager.Instance.CreateButton(
                    "",
                    selectionPanelObj.transform,
                    new Vector2(0.5f, 0.5f),
                    new Vector2(0.5f, 0.5f),
                    new Vector2(168f, -90f),
                    32f,
                    32f);
                settingsCogButton.GetComponent<Image>().enabled = false;
                iconObj = new GameObject("settingsIcon")
                {
                    transform =
                    {
                        parent = settingsCogButton.transform,
                        localPosition = new Vector3(0, 0, 0),
                        localScale = new Vector3(0.2f, 0.2f, 0.2f)
                    }
                };
                icon = iconObj.AddComponent<Image>();
                icon.sprite = OdinOnDemandPlugin.UISprites["settings"];

                var settingsButtonAction = settingsCogButton.GetComponent<Button>();
                settingsButtonAction.onClick.AddListener(ToggleSettingsPanel);
                

            }
        }

        private void ToggleSettingsPanel()
        {
            CreateSettingsGUI();

            mediaPlayerComponent.PlayerSettings.IsSettingsGuiActive = !settingsPanelObj.activeSelf;
            if (settingsPanelObj)
            {
                masterVolumeSliderComponent.value = mediaPlayerComponent.PlayerSettings.PlayerType == CinemaPackage.MediaPlayers.CinemaScreen
                    ? OODConfig.MasterVolumeScreen.Value
                    : OODConfig.MasterVolumeMusicplayer.Value;
                autoplayToggle.isOn = mediaPlayerComponent.PlayerSettings.AutoPlay;
                if (adminOnlyToggle) adminOnlyToggle.isOn = mediaPlayerComponent.PlayerSettings.AdminOnly;
            }

            settingsPanelObj.SetActive(mediaPlayerComponent.PlayerSettings.IsSettingsGuiActive);
        }

        private void CreateSettingsGUI()
        {
            if (settingsPanelObj == null)
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
                settingsPanelObj = DefaultControls.CreateScrollView(oodResources);
                settingsPanelRT = settingsPanelObj.GetComponent<RectTransform>();
                var selectionPanelRT = selectionPanelObj.GetComponent<RectTransform>();
                settingsPanelObj.transform.SetParent(selectionPanelObj.transform, false);

                settingsPanelRT.anchorMin = new Vector2(0.5f, 0.5f);
                settingsPanelRT.anchorMax = new Vector2(0.5f, 0.5f);
                settingsPanelRT.SetSizeWithCurrentAnchors(RectTransform.Axis.Horizontal,
                    selectionPanelRT.sizeDelta.x / 1.15f);
                settingsPanelRT.SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical,
                    selectionPanelRT.sizeDelta.y / 1.15f);
                settingsPanelRT.anchoredPosition = new Vector2(0, 0);

                var scrollviewBg = settingsPanelObj.GetComponent<Image>();
                scrollviewBg.color = new Color(0, 0, 0, 0.85f);
                var scrollRect = settingsPanelObj.GetComponent<ScrollRect>();
                scrollRect.vertical = true;
                scrollRect.horizontal = false;
                scrollRect.elasticity = 0.01f;
                scrollRect.scrollSensitivity = 20;
                scrollRect.horizontalScrollbar = null;
                scrollRect.verticalScrollbar = null;
                settingsPanelObj.transform.Find("Scrollbar Horizontal").gameObject.SetActive(false);
                settingsPanelObj.transform.Find("Scrollbar Vertical").gameObject.SetActive(false);


                //UpdateSettingsPanelPos();
                //settingsPanelObj.transform.localPosition = new Vector2((settingsRT.sizeDelta.x / 2) / ScreenScale.x, ((-settingsRT.sizeDelta.y / 2) / ScreenScale.y));
                // problem line
                //settingsPanelObj.transform.localPosition = new Vector2(110, -192); 

                settingsPanelObj.SetActive(false);
                var contentTransform = settingsPanelObj.transform.Find("Viewport/Content");
                contentTransform.gameObject.AddComponent<VerticalLayoutGroup>();
                var contentRT = contentTransform.gameObject.GetComponent<RectTransform>();
                contentRT.sizeDelta = new Vector2(0, 200);

                ////////////////////////
                /// AUTOPLAY TOGGLE ///
                var autoplayToggleObj = DefaultControls.CreateToggle(oodResources);
                autoplayToggleObj.transform.SetParent(contentTransform, false);
                var text = autoplayToggleObj.transform.Find("Label").GetComponent<Text>();
                autoplayToggle = autoplayToggleObj.GetComponent<Toggle>();
                autoplayToggle.isOn = mediaPlayerComponent.PlayerSettings.AutoPlay;
                autoplayToggle.onValueChanged.AddListener(OnAutoplayToggleChanged);
                GUIManager.Instance.ApplyTextStyle(text, GUIManager.Instance.AveriaSerifBold,
                    GUIManager.Instance.ValheimOrange);
                autoplayToggle.transform.Find("Background").GetComponent<RectTransform>().sizeDelta =
                    new Vector2(16, 16);
                autoplayToggle.transform.Find("Background/Checkmark").GetComponent<RectTransform>().sizeDelta =
                    new Vector2(16, 16);
                text.text = "Autoplay";
                //////////////////////////
                /// ADMIN ONLY TOGGLE ///
                if (SynchronizationManager.Instance.PlayerIsAdmin)
                {
                    /// toggle
                    var adminOnlyToggleObj = DefaultControls.CreateToggle(oodResources);
                    adminOnlyToggleObj.transform.SetParent(contentTransform, false);
                    var t = adminOnlyToggleObj.transform.Find("Label").GetComponent<Text>();
                    adminOnlyToggle = adminOnlyToggleObj.GetComponent<Toggle>();
                    adminOnlyToggle.isOn = mediaPlayerComponent.PlayerSettings.AdminOnly;
                    adminOnlyToggle.onValueChanged.AddListener(OnAdminOnlyToggleChanged);
                    GUIManager.Instance.ApplyTextStyle(t, GUIManager.Instance.AveriaSerifBold,
                        GUIManager.Instance.ValheimOrange);
                    adminOnlyToggle.transform.Find("Background").GetComponent<RectTransform>().sizeDelta =
                        new Vector2(16, 16);
                    adminOnlyToggle.transform.Find("Background/Checkmark").GetComponent<RectTransform>().sizeDelta =
                        new Vector2(16, 16);
                    t.text = "Admin Only";
                }

                /////////////////////////////
                /// AUDIO DISTANCE PANEL ///
                var panel = DefaultControls.CreatePanel(oodResources);
                panel.transform.SetParent(contentTransform, false);
                panel.GetComponent<RectTransform>().sizeDelta = new Vector2(0, 32);
                var image = panel.GetComponent<Image>();
                image.color = new Color(image.color.r, image.color.g, image.color.b, 0.0155f);

                GUIManager.Instance.CreateText(
                    "Listening Distance",
                    panel.transform,
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
                var str = mediaPlayerComponent.mAudio.maxDistance.ToString(CultureInfo.CurrentCulture);
                var inputObj = GUIManager.Instance.CreateInputField(
                    panel.transform,
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

                ////////////////////////////
                /// MASTER VOLUME PANEL ///
                panel = DefaultControls.CreatePanel(oodResources);
                panel.transform.SetParent(contentTransform, false);
                panel.GetComponent<RectTransform>().sizeDelta = new Vector2(0, 32);
                image = panel.GetComponent<Image>();
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
                var sliderObj = DefaultControls.CreateSlider(oodResources);
                sliderObj.transform.SetParent(panel.transform, false);
                sliderObj.transform.localPosition = new Vector2(0, -10);
                var sliderRT = sliderObj.GetComponent<RectTransform>();
                sliderRT.localScale = new Vector3(0.65f, 1, 1);
                masterVolumeSliderComponent = sliderObj.GetComponent<Slider>();
                masterVolumeSliderComponent.maxValue = 15f;
                masterVolumeSliderComponent.minValue = -15f;
                masterVolumeSliderComponent.value = mediaPlayerComponent.PlayerSettings.PlayerType == CinemaPackage.MediaPlayers.CinemaScreen
                    ? OODConfig.MasterVolumeScreen.Value
                    : OODConfig.MasterVolumeMusicplayer.Value;
                masterVolumeSliderComponent.onValueChanged.AddListener(OnMasterVolumeChanged);
                
            }
        }

        private void ToggleLock()
        {
            mediaPlayerComponent.PlayerSettings.IsLocked = !mediaPlayerComponent.PlayerSettings.IsLocked;
            UpdateLockIcon();
            mediaPlayerComponent.SaveZDO();
            rpc.SendData(CinemaPackage.RPCDataType.UpdateZDO, mediaPlayerComponent.PlayerSettings.PlayerType, mediaPlayerComponent.gameObject.transform.position);
        }

        private void UpdateLockIcon()
        {
            if (selectionPanelObj)
            {
                if (mediaPlayerComponent.PlayerSettings.IsLocked)
                {
                    lockedIconObj.SetActive(true);
                    unlockedIconObj.SetActive(false);
                }
                else
                {
                    lockedIconObj.SetActive(false);
                    unlockedIconObj.SetActive(true);
                }
            }
        }

        private void OnVolumeSliderChanged(float vol)
        {
            mediaPlayerComponent.mAudio.volume = vol;
            if (vol <= 0f)
            {
                unmutedVolumeObj.SetActive(false);
                mutedVolumeObj.SetActive(true);
            }
            else
            {
                unmutedVolumeObj.SetActive(true);
                mutedVolumeObj.SetActive(false);
            }
        }

        private void OnAutoplayToggleChanged(bool toggleValue)
        {
            mediaPlayerComponent.PlayerSettings.AutoPlay = toggleValue;
            mediaPlayerComponent.SaveZDO();
            if (mediaPlayerComponent.UnparsedURL != null) mediaPlayerComponent.ZNetView.GetZDO().Set("url", mediaPlayerComponent.PlayerSettings.AutoPlay ? mediaPlayerComponent.UnparsedURL : "");

            rpc.SendData(CinemaPackage.RPCDataType.UpdateZDO, mediaPlayerComponent.PlayerSettings.PlayerType, mediaPlayerComponent.gameObject.transform.position);
        }

        private void OnAudioDistanceInputEndEdit(string input)
        {
            if (input.Length < 1) return;
            var parse = float.Parse(input);
            if (parse == 0f) parse = 0.01f;
            if (parse > OODConfig.MaxListeningDistance.Value && !SynchronizationManager.Instance.PlayerIsAdmin)
                parse = OODConfig.MaxListeningDistance.Value;
            mediaPlayerComponent.mAudio.maxDistance = parse;
            mediaPlayerComponent.SaveZDO();
            rpc.SendData(CinemaPackage.RPCDataType.UpdateZDO, mediaPlayerComponent.PlayerSettings.PlayerType, mediaPlayerComponent.gameObject.transform.position);
        }

        private void OnAdminOnlyToggleChanged(bool toggleValue)
        {
            mediaPlayerComponent.PlayerSettings.AdminOnly = toggleValue;
            mediaPlayerComponent.SaveZDO();
            rpc.SendData(CinemaPackage.RPCDataType.UpdateZDO, mediaPlayerComponent.PlayerSettings.PlayerType, mediaPlayerComponent.gameObject.transform.position);
        }

        private void OnMasterVolumeChanged(float vol)
        {
            if (mediaPlayerComponent.PlayerSettings.PlayerType == CinemaPackage.MediaPlayers.CinemaScreen)
                OODConfig.MasterVolumeScreen.Value = vol;
            else
                OODConfig.MasterVolumeMusicplayer.Value = vol;
        }

        public void ToggleMainPanel()
        {
            mediaPlayerComponent.UpdateZDO();
            CreateMainGUI();
            UpdateLockIcon();
            UpdateLoopIndicator();

            if (mediaPlayerComponent.PlayerSettings.IsPlayingPlaylist)
            {
                urlInputFieldObj.GetComponent<InputField>().text = mediaPlayerComponent.PlaylistURL;
            }
            else if (mediaPlayerComponent.UnparsedURL != null)
            {
                urlInputFieldObj.GetComponent<InputField>().text = mediaPlayerComponent.UnparsedURL;
            }
            
            mediaPlayerComponent.PlayerSettings.IsGuiActive = !selectionPanelObj.activeSelf;
            selectionPanelObj.SetActive(mediaPlayerComponent.PlayerSettings.IsGuiActive);
            // Toggle input
            GUIManager.BlockInput(mediaPlayerComponent.PlayerSettings.IsGuiActive);
        }

        private void ToggleShuffle()
        {
            mediaPlayerComponent.PlayerSettings.IsShuffling = !mediaPlayerComponent.PlayerSettings.IsShuffling;
            if (mediaPlayerComponent.PlayerSettings.IsShuffling)
            {
                var rnd = new Random();
                mediaPlayerComponent.PreShufflePlaylist = mediaPlayerComponent.CurrentPlaylist;
                mediaPlayerComponent.CurrentPlaylist = mediaPlayerComponent.CurrentPlaylist.OrderBy(x => rnd.Next()).ToList();
                mediaPlayerComponent.PlaylistPosition = 0;
                if (OODConfig.DebugEnabled.Value) Logger.LogDebug("Shuffled Playlist");

                toggleShuffleObj.GetComponentInChildren<Text>().text = "Y";
            }
            else
            {
                var pos = mediaPlayerComponent.PreShufflePlaylist.FindIndex(v => v == mediaPlayerComponent.CurrentPlaylist.ElementAt(mediaPlayerComponent.PlaylistPosition));
                mediaPlayerComponent.PlaylistPosition = pos;
                mediaPlayerComponent.CurrentPlaylist = mediaPlayerComponent.PreShufflePlaylist;
                toggleShuffleObj.GetComponentInChildren<Text>().text = "N";
                mediaPlayerComponent.PreShufflePlaylist = null;
            }
        }

        public void ToggleLoop()
        {
            mediaPlayerComponent.PlayerSettings.IsLooping = !mediaPlayerComponent.PlayerSettings.IsLooping;
            if (!mediaPlayerComponent.PlayerSettings.IsPlayingPlaylist)
            {
                mediaPlayerComponent.mAudio.loop = mediaPlayerComponent.PlayerSettings.IsLooping;
                mediaPlayerComponent.mScreen.isLooping = mediaPlayerComponent.PlayerSettings.IsLooping;
                rpc.SendData(CinemaPackage.RPCDataType.SetLoop, mediaPlayerComponent.PlayerSettings.PlayerType, mediaPlayerComponent.gameObject.transform.position, "",
                    CinemaPackage.PlayerStatus.NULL, 1, mediaPlayerComponent.PlayerSettings.IsLooping);
            }

            mediaPlayerComponent.SaveZDO();
            UpdateLoopIndicator();
        }

        public void UpdateLoopIndicator()
        {
            toggleLoopObj.GetComponentInChildren<Text>().text = mediaPlayerComponent.PlayerSettings.IsLooping ? "Y" : "N";
        }

        public void SetLoop(bool looping)
        {
            mediaPlayerComponent.mAudio.loop = looping;
            mediaPlayerComponent.mScreen.isLooping = looping;
            if (selectionPanelObj)
                if (selectionPanelObj.activeInHierarchy)
                    toggleLoopObj.GetComponentInChildren<Text>().text = looping ? "Y" : "N";
        }

        public void ToggleTrackForward()
        {
            mediaPlayerComponent.PlayerSettings.TrackingForward = !mediaPlayerComponent.PlayerSettings.TrackingForward;
            mediaPlayerComponent.StartCoroutine(TrackFoward());
        }

        private IEnumerator TrackFoward()
        {
            while (mediaPlayerComponent.PlayerSettings.TrackingForward)
            {
                mediaPlayerComponent.mScreen.StepForward();
                yield return null;
            }
        }
        
        private Coroutine debounceCoroutine;
        
        public void OnClickSkipPlaylistTrack()
        {
            if (mediaPlayerComponent.PlaylistPosition + 1 < mediaPlayerComponent.CurrentPlaylist.Count)
            {
                mediaPlayerComponent.PlaylistPosition++;
                if (debounceCoroutine != null)
                {
                    mediaPlayerComponent.StopCoroutine(debounceCoroutine);
                }
                debounceCoroutine = mediaPlayerComponent.StartCoroutine(DebounceSkipVideo());
            }
            else if (mediaPlayerComponent.PlayerSettings.IsLooping)
            {
                mediaPlayerComponent.PlaylistPosition = 0;
                if (debounceCoroutine != null)
                {
                    mediaPlayerComponent.StopCoroutine(debounceCoroutine);
                }
                debounceCoroutine = mediaPlayerComponent.StartCoroutine(DebounceSkipVideo());
            }
        }
        
        public float debounceTime = 0.3f;  // time to wait for another input
        private bool isWaiting = false;
        private Text loadingIndicatorText;

        IEnumerator DebounceSkipVideo()
        {
            UpdatePlaylistInfo();
            yield return new WaitForSeconds(debounceTime);
            FinalizeSkip();
        }
        
        void FinalizeSkip()
        {
            if (isWaiting) return;  // don't play if we're still in the debounce period
            
            mediaPlayerComponent.SetURL(mediaPlayerComponent.CurrentPlaylist.ElementAt(mediaPlayerComponent.PlaylistPosition).Url);
        }

        public void PreviousPlaylistTrack()
        {
            if (mediaPlayerComponent.PlaylistPosition - 1 >= 0)
            {
                mediaPlayerComponent.PlaylistPosition--;
                mediaPlayerComponent.SetURL(mediaPlayerComponent.CurrentPlaylist.ElementAt(mediaPlayerComponent.PlaylistPosition).Url);
            }
            else if (mediaPlayerComponent.PlayerSettings.IsLooping)
            {
                mediaPlayerComponent.PlaylistPosition = mediaPlayerComponent.CurrentPlaylist.Count() - 1;
                mediaPlayerComponent.SetURL(mediaPlayerComponent.CurrentPlaylist.ElementAt(mediaPlayerComponent.PlaylistPosition).Url);
            }
        }
        
        public void SetLoadingIndicatorText(string text)
        {
            if(loadingIndicatorObj) loadingIndicatorText.text = text;
        }
        
        public void SetLoadingIndicatorActive(bool active)
        {
            if(loadingIndicatorObj) loadingIndicatorObj.SetActive(active);
        }
        
        public void ResetLoadingIndicatorText()
        {
            if (loadingIndicatorObj)
            {
                loadingIndicatorText.text = "Processing";
                loadingIndicatorObj.SetActive(false);
            }
        }
        
    }
}