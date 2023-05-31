using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using BepInEx;
using Jotunn.Managers;
using OdinOnDemand.Utils;
using UnityEngine;
using UnityEngine.Networking;
using UnityEngine.UI;
using UnityEngine.Video;
using Logger = Jotunn.Logger;

namespace OdinOnDemand
{
    public class MediaPlayerComponent : MonoBehaviour, Hoverable, Interactable
    {
        // Main components
        internal string mName = "Media Player";
        internal VideoPlayer mScreen;
        internal AudioSource mAudio;
        
        // Screen
        private GameObject screenPlaneObj;
        private GameObject screenUICanvasObj;
        private GameObject loadingCircleObj;
        private Material targetTexMat;
        private RenderTexture RenderTexture { get; set; }

        //Gramophone animator
        private Animator animator;
        
        public string UnparsedURL { get; private set; }
        
        //Utils
        private RpcHandler rpc;
        private URLGrab urlGrab;
        
        // URIs
        private Uri downloadURL;
        private Uri youtubeSoundDirectUri;
        private Uri youtubeVideoDirectUri;
        
        private string youtubeURLNode;
        
        public MediaPlayerComponent()
        {
            UIController = new UIController(this);
            PlayerSettings = new PlayerSettings();
        }
        
        // Various properties
        public Piece mPiece { get; private set; }
        public int PlaylistPosition {  set; get; } 
        public string PlaylistString { set; get; } // TODO move this to player settings
        public List<VideoInfo> CurrentPlaylist {  set; get; }
        public List<VideoInfo> PreShufflePlaylist { set; get; }
        public string PlaylistURL { private set; get; }
        public ZNetView ZNetView { private set; get; }
        public PlayerSettings PlayerSettings { get; }
        public UIController UIController { get; }
        
        public void Awake()
        {
            //init components
            mPiece = gameObject.GetComponentInChildren<Piece>();
            mScreen = gameObject.GetComponentInChildren<VideoPlayer>();
            mName = mPiece.m_name;
            //Network
            ZNetView = GetComponent<ZNetView>();
            var zdo = ZNetView.GetZDO();
            // If the player is freshly placed, set the hasData flag to false. Could also just check if we have any data in the zdo?
            if (zdo != null)
            {
                var hasData = zdo.GetBool("hasData");
                PlayerSettings.IsFreshlyPlaced = !hasData;
            }
            
            // Controllers handlers and utils
            UIController.Initialize();
            rpc = OdinOnDemandPlugin.RPCHandlers;
            urlGrab = new URLGrab();

            // Identify and set the type of player
            if (mName.Contains("boombox") || mName.Contains("radio"))
            {
                PlayerSettings.PlayerType = CinemaPackage.MediaPlayers.Radio;
            }
            else if (mName.Contains("gramophone")) // If it's a gramophone, we need to set the animator 
            {
                PlayerSettings.PlayerType = CinemaPackage.MediaPlayers.Radio;
                animator = gameObject.GetComponentInChildren<Animator>();
                animator.SetBool(PlayerSettings.Playing, false);
            }
            else
            {
                PlayerSettings.PlayerType = CinemaPackage.MediaPlayers.CinemaScreen;
            }

            if (PlayerSettings.PlayerType == CinemaPackage.MediaPlayers.CinemaScreen) SetupScreen();
            else if(PlayerSettings.PlayerType == CinemaPackage.MediaPlayers.Radio) SetupRadioScreen();

            SetupAudio();

            // Repeating tasks
            InvokeRepeating(nameof(UpdateLoadingIndicator), 0.5f, 0.5f);
            InvokeRepeating(nameof(UpdateChecks), 1f, 1f);
            
            if (PlayerSettings.IsFreshlyPlaced && ZNetScene.instance) //If we're freshly placed set some default data and flip bool
            {
                SaveZDO();
                PlayerSettings.IsFreshlyPlaced = false;
                zdo = ZNetView.GetZDO();
                if (zdo != null)
                {
                    ClaimOwnership(zdo);
                    zdo.Set("hasData", true);
                }
            }
        }
        
        public void Start()
        {
            RpcHandler.mediaPlayerList.Add(this); // Add this player to the list of players for networking

            if (OODConfig.ScreenDisableOutOfRange.Value) // if the screen is set to disable when out of range, add a collider so we can handle that
            {
                var collider = gameObject.AddComponent<SphereCollider>();
                collider.isTrigger = true;
                collider.radius = OODConfig.DefaultDistance.Value;
            }
        }
        
        public void OnEnable()
        {
            if (mPiece.IsPlacedByPlayer()) LoadZDO(); // If the player is placed by a player, load the zdo data to init
        }
        
        public void OnDisable()
        {
            RpcHandler.mediaPlayerList.Remove(this); //Remove this player from the list of players if it's disabled, so we don't try to update it
            SaveZDO();
        }

        private void OnDestroy() 
        {
            //cLean up
            if (RenderTexture) RenderTexture.Release();
            if (PlayerSettings.IsGuiActive) // If the GUI is active, close it so it's not stuck open. Solves edge case where player is destroyed while GUI is open
            {
                PlayerSettings.IsGuiActive = false;
                if (UIController.selectionPanelObj) UIController.selectionPanelObj.SetActive(false);
                GUIManager.BlockInput(PlayerSettings.IsGuiActive);
            }

            if (UIController.selectionPanelObj) Destroy(UIController.selectionPanelObj);
            
        }

        public void SaveZDO() // Save all our ZDO settings
        {
            var zdo = ZNetView.GetZDO();
            if (zdo != null && mAudio != null)
            {
                ClaimOwnership(zdo);
                zdo.Set("distance", mAudio.maxDistance);
                zdo.Set("adminOnly", PlayerSettings.AdminOnly); 
                zdo.Set("autoPlay", PlayerSettings.AutoPlay);
                zdo.Set("isLooping", PlayerSettings.IsLooping);
                zdo.Set("isLocked", PlayerSettings.IsLocked);
                if (!string.IsNullOrEmpty(UnparsedURL)) zdo.Set("url", UnparsedURL);
            }
        }
        
        public void LoadZDO()
        {
            var zdo = ZNetView.GetZDO();
            if (zdo != null)
            {
                PlayerSettings.AutoPlay = zdo.GetBool("autoPlay");
                PlayerSettings.AdminOnly = zdo.GetBool("adminOnly");
                var zdoFloat = zdo.GetFloat("distance");
                if (zdoFloat != 0f) mAudio.maxDistance = zdoFloat;
                PlayerSettings.IsLocked = zdo.GetBool("isLocked");
                if (PlayerSettings.AutoPlay && zdo.GetString("unparsedURL") != null)
                {
                    UnparsedURL = zdo.GetString("url");
                    //Logger.LogInfo("loaded url from zdo: " + UnparsedURL);
                    RPC_SetURL(UnparsedURL);
                }

                PlayerSettings.IsLooping = zdo.GetBool("isLooping");
                mAudio.loop = PlayerSettings.IsLooping;
                mScreen.isLooping = PlayerSettings.IsLooping;
            }
        }

        public void UpdateZDO() // For periodic updates to the ZDO, usually from RPC when a player changes something
        {
            var zdo = ZNetView.GetZDO();
            if (zdo != null)
            {
                PlayerSettings.AutoPlay = zdo.GetBool("autoPlay");
                PlayerSettings.AdminOnly = zdo.GetBool("adminOnly");
                var zdoFloat = zdo.GetFloat("distance");
                if (zdoFloat != 0f) mAudio.maxDistance = zdoFloat;
                PlayerSettings.IsLocked = zdo.GetBool("isLocked");
                PlayerSettings.IsLooping = zdo.GetBool("isLooping");
                mAudio.loop = PlayerSettings.IsLooping;
                mScreen.isLooping = PlayerSettings.IsLooping;
            }
        }

        private void UpdateLoadingIndicator() //update loading indicator, called every half second
        {
            if (urlGrab.LoadingBool && UIController.loadingIndicatorObj != null && !urlGrab.FailBool) //if urlgrab is loading and not failed, update loading indicators 
            {
                if (screenUICanvasObj && loadingCircleObj)
                {
                    screenUICanvasObj.SetActive(true); //Show the loading circle on screen
                    loadingCircleObj.SetActive(true);
                }
                
                UIController.loadingIndicatorObj.SetActive(true); //Show the loading indicator in the GUI
                
                var loadingMessageIndex = PlayerSettings.LoadingCount % 4; // Cycle through the loading messages
                UIController.loadingIndicatorObj.GetComponent<Text>().text = UIController.loadingMessages[loadingMessageIndex];
                PlayerSettings.LoadingCount++;
            }
            else if (!urlGrab.LoadingBool && UIController.loadingIndicatorObj && UIController.loadingIndicatorObj.activeSelf)
            {
                UIController.loadingIndicatorObj.SetActive(false);
            }
        }

        private void UpdateChecks() //1second checks, screen render distance, master volume updates and playlist gui updates
        {
            //Master volume updates from config
            mAudio.outputAudioMixerGroup.audioMixer.GetFloat("MasterVolume", out var masterVolumeCheck);
            if (PlayerSettings.PlayerType == CinemaPackage.MediaPlayers.CinemaScreen)
            {
                if (masterVolumeCheck != OODConfig.MasterVolumeScreen.Value)
                    mAudio.outputAudioMixerGroup.audioMixer.SetFloat("MasterVolume",
                        OODConfig.MasterVolumeScreen.Value);
            }
            else
            {
                if (masterVolumeCheck != OODConfig.MasterVolumeMusicplayer.Value)
                    mAudio.outputAudioMixerGroup.audioMixer.SetFloat("MasterVolume",
                        OODConfig.MasterVolumeMusicplayer.Value);
            }

            //Playlist GUI checks and updates
            if (UIController.selectionPanelObj)
            {
                if (PlayerSettings.IsPlayingPlaylist)
                {
                    UIController.UpdatePlaylistUI();
                    UIController.playlistTrackText.text = PlaylistString;
                }
                else 
                {
                    UIController.UpdatePlaylistUI();
                }
            }
        }

        private void SetupScreen() //Setup the screen, called on awake
        {
            //grab all our objects
            screenPlaneObj = mScreen.transform.Find("Plane").gameObject;
            screenUICanvasObj = transform.Find("screenUICanvas").gameObject;
            loadingCircleObj = screenUICanvasObj.transform.Find("mainCanvas/Loading Circle").gameObject;

            //Loading progress circle for screen display
            var progressCircle = loadingCircleObj.transform.Find("Progress").gameObject;
            progressCircle.AddComponent<LoadingCircle>();

            //Depending on config value, we have two different types of materials for our screen. One is affected by in-game light, the other is not.
            if (OODConfig.VideoBacklight.Value) 
            {
                //Render texture with Unlit shader
                RenderTexture = new RenderTexture(1920, 1080, 0, RenderTextureFormat.ARGB32);
                RenderTexture.Create();
                RenderTexture.Release();
                targetTexMat = new Material(OdinOnDemandPlugin.screenMaterial.shader)
                {
                    mainTexture = RenderTexture
                };
                //set to our screen
                screenPlaneObj.GetComponent<MeshRenderer>().material = targetTexMat;
                mScreen.targetTexture = RenderTexture;
            }
            else
            {
                // Render texture with Standard shader
                RenderTexture = new RenderTexture(256, 256, 0, RenderTextureFormat.ARGB32)
                {
                    depth = 0,
                    width = 1920,
                    height = 1080
                };
                RenderTexture.Create();
                RenderTexture.Release();
                mScreen.targetTexture = RenderTexture;

                var shader = Shader.Find("Standard");
                targetTexMat = new Material(shader);
                targetTexMat.EnableKeyword("_EMISSION");
                targetTexMat.SetTexture(PlayerSettings.MainTex, RenderTexture);
                targetTexMat.SetTexture(PlayerSettings.EmissiveColorMap, RenderTexture);
                //set to our screen
                screenPlaneObj.GetComponent<MeshRenderer>().material = targetTexMat;
            }
            
            screenPlaneObj.SetActive(false); //hide the screen plane until we have a video to play
            
            //Screen events
            mScreen.prepareCompleted += ScreenPrepareCompleted;
            mScreen.loopPointReached += EndReached;
            
            mScreen.url = "";
            mScreen.Pause();
        }
        
        private void SetupRadioScreen() //The radio doesn't need anything but events, so we don't need to setup a render texture
        {
            //Screen events
            mScreen.prepareCompleted += ScreenPrepareCompleted; 
            mScreen.loopPointReached += EndReached;
            mScreen.url = "";
            mScreen.Pause();
        }

        private void SetupAudio()
        {
            //Grab our audio source and set up values from config
            mAudio = gameObject.GetComponentInChildren<AudioSource>();
            mAudio.maxDistance = OODConfig.DefaultDistance.Value;
            PlayerSettings.Volume = OODConfig.DefaultAudioSourceVolume.Value;
            
            mAudio.spatialBlend = 1;
            mAudio.spatialize = true;
            mAudio.spatializePostEffects = true;
            mAudio.volume = PlayerSettings.Volume;
        }

        private void EndReached(VideoPlayer source) //End of video reached event
        {
            if (PlayerSettings.IsPlayingPlaylist) //Playlist next video logic
            {
                if (PlaylistPosition < CurrentPlaylist.Count() - 1)
                {
                    PlaylistPosition++;
                    SetURL(CurrentPlaylist.ElementAt(PlaylistPosition).Url);
                }
                else if (PlayerSettings.IsLooping)
                {
                    PlaylistPosition = 0;
                    SetURL(CurrentPlaylist.ElementAt(PlaylistPosition).Url);
                }
            }

            if (PlayerSettings.PlayerType == CinemaPackage.MediaPlayers.Radio) //If we're a radio and not looping stop playing animation
                if (animator && (!mScreen.isLooping || !mAudio.loop) && !PlayerSettings.IsPlayingPlaylist)
                    animator.SetBool(PlayerSettings.Playing, false);
        }

        private void ScreenPrepareCompleted(VideoPlayer source) //Video prepare complete event. Called once the video is ready to play after being set 
        {
            //Set our screen plane to active
            if (PlayerSettings.PlayerType == CinemaPackage.MediaPlayers.CinemaScreen) screenPlaneObj.SetActive(true);
            
            //If autoplay, save url to zdo. do we need to do this here? every user on the server saves the zdo then if they have it nearby. TODO: fix repeated zdo saving
            if (PlayerSettings.AutoPlay)
            {
                ClaimOwnership(ZNetView.GetZDO());
                ZNetView.GetZDO().Set("url", UnparsedURL);
            }
            
            //Play the video
            source.Play();
            mAudio.Play();
            
            //Hide the loading indicators
            if (UIController.loadingIndicatorObj) UIController.loadingIndicatorObj.SetActive(false);
            if (screenUICanvasObj && loadingCircleObj)
            {
                screenUICanvasObj.SetActive(false);
                loadingCircleObj.SetActive(false);
            }
            
            //If we're a radio, play the animation
            if (animator) animator.SetBool(PlayerSettings.Playing, true);
        }

        public void Play(bool isRPC = false) //Play the video (if paused)
        {
            //If not RPC, send play RPC command
            if (!isRPC) rpc.SendData(CinemaPackage.RPCDataType.Play, PlayerSettings.PlayerType, gameObject.transform.position);
            
            //Enable screen plane object
            if (PlayerSettings.PlayerType == CinemaPackage.MediaPlayers.CinemaScreen)
            {
                screenPlaneObj.SetActive(true);
            }
            
            // If link type is youtube, play just the screen
            if (PlayerSettings.playerLinkType == PlayerSettings.LinkType.Youtube)
            {
                mScreen.Play();
                if (animator) animator.SetBool(PlayerSettings.Playing, true);
                //m_audio.Play();
            }
            else //If not youtube, play both screen and audio to handle mp3 and such
            {
                if (animator) animator.SetBool(PlayerSettings.Playing, true);
                if (PlayerSettings.IsPaused)
                {
                    mAudio.UnPause();
                    PlayerSettings.IsPaused = false;
                }
                else
                {
                    mAudio.Play();
                }
            }
            
            //We are no longer tracking forward if we're playing
            PlayerSettings.TrackingForward = false;
        }

        public void Pause(bool isRPC = false) //Pause the video (if playing)
        {
            //If not RPC, send pause RPC command
            if (!isRPC) rpc.SendData(CinemaPackage.RPCDataType.Pause, PlayerSettings.PlayerType, gameObject.transform.position);
            //If we're a radio, stop the animation
            if (animator) animator.SetBool(PlayerSettings.Playing, false);
            
            //Pause the video and audio, set bools
            mScreen.Pause();
            mAudio.Pause();
            PlayerSettings.TrackingForward = false;
            PlayerSettings.IsPaused = true;
        }

        public void Stop(bool isRPC = false) //Stop the video (if playing)
        {
            //If not RPC, send stop RPC command
            if (!isRPC) rpc.SendData(CinemaPackage.RPCDataType.Stop, PlayerSettings.PlayerType, gameObject.transform.position);
            
            //Stop the video and audio, hide indicators
            mScreen.Stop();
            mAudio.Stop();
            if (PlayerSettings.PlayerType == CinemaPackage.MediaPlayers.CinemaScreen)
            {
                screenPlaneObj.SetActive(false);
                if (screenUICanvasObj && loadingCircleObj)
                {
                    screenUICanvasObj.SetActive(false);
                    loadingCircleObj.SetActive(false);
                }
            }

            if (animator) animator.SetBool(PlayerSettings.Playing, false);
            
            
            PlayerSettings.TrackingForward = false;
            PlayerSettings.IsPlayingPlaylist = false;
            CurrentPlaylist = null;
            
            urlGrab.Reset();
        }

        public void SetURL(string url, bool isRPC = false, bool playVideo = true) //Set the video URL, with optional RPC and play on set
        {
            UnparsedURL = url; //Save the unparsed url for later use

            // If we're on a server, just send RPC. It will be sent back to us and we'll handle it there.
            if (ZNet.instance.IsClientInstance() && !isRPC && UnparsedURL != null)
            {
                if (url.Contains("soundcloud") && PlayerSettings.PlayerType == CinemaPackage.MediaPlayers.Radio)
                {
                    rpc.SendData(CinemaPackage.RPCDataType.SetAudioUrl, PlayerSettings.PlayerType, gameObject.transform.position, UnparsedURL);
                }

                if (mScreen != null || mAudio != null)
                {
                    if (url.Contains("youtube.com/watch?v=") || url.Contains("youtube.com/shorts/") ||
                        url.Contains("youtu.be") || url.Contains("youtube.com/playlist"))
                    {
                        UIController.UpdatePlaylistInfo();
                        if (url.Contains("?list=") || url.Contains("&list="))
                        {
                            SetPlaylist(url);
                            return;
                        }
                        
                        rpc.SendData(CinemaPackage.RPCDataType.SetVideoUrl, PlayerSettings.PlayerType, gameObject.transform.position, UnparsedURL);
                    }

                }

                rpc.SendData(CinemaPackage.RPCDataType.SetVideoUrl, PlayerSettings.PlayerType, gameObject.transform.position, UnparsedURL);
                 
                return;
            }
            
            //Otherwise on singleplayer just set the url normally
            if (url != null)
            {
                //Soundcloud
                if (url.Contains("soundcloud") && PlayerSettings.PlayerType == CinemaPackage.MediaPlayers.Radio)
                {
                    PlayerSettings.playerLinkType = PlayerSettings.LinkType.Soundcloud;
                    PlaySoundcloud(url, isRPC);
                    return;
                }

                if (mScreen != null || mAudio != null) 
                {
                    //Youtube
                    if (url.Contains("youtube.com/watch?v=") || url.Contains("youtube.com/shorts/") ||
                        url.Contains("youtu.be") || url.Contains("youtube.com/playlist"))
                    {
                        PlayerSettings.playerLinkType = PlayerSettings.LinkType.Youtube;
                        UIController.UpdatePlaylistInfo();

                        //Playlists
                        if (url.Contains("?list=") || url.Contains("&list="))
                        {
                            SetPlaylist(url);
                            return;
                        }

                        if (OODConfig.DebugEnabled.Value) Logger.LogDebug("Playing youtube: " + url);
                        if(UIController.loadingIndicatorObj) UIController.loadingIndicatorObj.GetComponent<Text>().text = "Processing";
                
                        PlayYoutube(url);
                        return;
                    }
                    
                    // Direct links of all other types
                    if (url.Contains("\\") || url.Contains(".") || url.Contains("/"))
                    {
                        PlayerSettings.playerLinkType = PlayerSettings.LinkType.Direct;
                        //Radio logic
                        if (PlayerSettings.PlayerType == CinemaPackage.MediaPlayers.Radio)
                        {
                            //This shouldn't be needed anymore since we store raw links TODO: Remove and test
                            if (url.Contains("googlevideo.com/videoplayback?"))
                            {
                                PlayerSettings.playerLinkType = PlayerSettings.LinkType.Youtube;
                                mScreen.url = url;
                                mScreen.Prepare();
                                return;
                            }
                            
                            downloadURL = urlGrab.CleanUrl(url);
                            if (downloadURL != null)
                            {
                                StartCoroutine(AudioWebRequest());
                                return;
                            }
                            
                            Logger.LogDebug("download url is null");
                            return;
                        }
                        
                        //Cinemascreen logic
                        
                        //Creating relative paths for local files
                        mScreen.url = null;
                        var relativeURL = "";
                        if (url.Contains("local:\\\\"))
                        {
                            relativeURL =  Path.Combine(Paths.PluginPath, url).Replace("local:\\\\", "");;
                            
                        } else if (url.Contains("local://"))
                        {
                            relativeURL =  Path.Combine(Paths.PluginPath, url).Replace("local://", "");;
                            
                        }

                        if (relativeURL != "")
                        {
                            mScreen.url = relativeURL;
                            if (OODConfig.DebugEnabled.Value) Logger.LogDebug("Playing: " + relativeURL);
                        }
                        else
                        {
                            mScreen.url = url;
                            if (OODConfig.DebugEnabled.Value) Logger.LogDebug("Playing: " + url);
                        }
                        
                        mScreen.Prepare();
                        if (screenUICanvasObj && loadingCircleObj)
                        {
                            screenUICanvasObj.SetActive(true);
                            loadingCircleObj.SetActive(true);
                        }
                        
                        //m_screen.transform.Find("Plane").gameObject.SetActive(true);
                        if (!playVideo) mScreen.Pause();
                    }
                    else
                    {
                        Logger.LogWarning("Could not play. Is this not a url or local file?");
                    }
                }
                else
                {
                    Logger.LogWarning("SetURL Error: can't find the screen!");
                }
            }
        }

        public void RPC_SetURL(string url, bool playVideo = true) // RPC SetURL
        {
            if (PlayerSettings.PlayerType == CinemaPackage.MediaPlayers.Radio)
            {
                if (url.Contains("soundcloud"))
                {
                    PlayerSettings.playerLinkType = PlayerSettings.LinkType.Soundcloud;
                    PlaySoundcloud(url, true);
                    return;
                }
                PlayerSettings.playerLinkType = PlayerSettings.LinkType.Direct;
                downloadURL = urlGrab.CleanUrl(url);
                if (downloadURL != null) StartCoroutine(AudioWebRequest());
                return;
            }

            //Relative paths for local files
            var relativeURL = "";
            if (url.Contains("local:\\\\"))
            {
                relativeURL =  Path.Combine(Paths.PluginPath, url).Replace("local:\\\\", "");;
                            
            } else if (url.Contains("local://"))
            {
                relativeURL =  Path.Combine(Paths.PluginPath, url).Replace("local://", "");;
                            
            }
            if (relativeURL != "")
            {
                mScreen.url = relativeURL;
                PlayerSettings.playerLinkType = PlayerSettings.LinkType.Direct;
                if (OODConfig.DebugEnabled.Value) Logger.LogDebug("Playing: " + relativeURL);
            }
            
            else if (url.Contains("\\") || url.Contains(".") || url.Contains("/"))
            {
                if (url.Contains("youtube.com/watch?v=") || url.Contains("youtube.com/shorts/") ||
                    url.Contains("youtu.be") && OODConfig.IsYtEnabled.Value)
                {
                    if (PlayerSettings.IsLooping && (!mScreen.isLooping || !mAudio.loop))
                    {
                        mScreen.isLooping = true;
                        mAudio.loop = true;
                    }
                    PlayerSettings.playerLinkType = PlayerSettings.LinkType.Youtube;
                    if (OODConfig.YoutubeAPI.Value == OODConfig.YouTubeAPI.YouTubeExplode)
                    {
                            
                        StartYoutubeAsync(url, true);
                    }
                    else
                    {
                        youtubeURLNode = url;
                        StartCoroutine(YoutubeNodeQuery(true));
                    }
                }
                else
                {
                    PlayerSettings.playerLinkType = PlayerSettings.LinkType.Direct;
                    mScreen.url = url;
                    if (OODConfig.DebugEnabled.Value) Logger.LogDebug("Playing: " + url);
                }
            }

            mScreen.Prepare();
            if (playVideo)
            {
                if (screenUICanvasObj && loadingCircleObj)
                {
                    screenUICanvasObj.SetActive(true);
                    loadingCircleObj.SetActive(true);
                }
            }
            else
            {
                mScreen.Pause();
            }
        }

        private async void SetPlaylist(string url) // Set playlist from url
        {
            CurrentPlaylist = await urlGrab.GetYouTubePlaylist(url); 
            PlayerSettings.IsShuffling = false;

            if (CurrentPlaylist != null)
            {
                PlayerSettings.IsPlayingPlaylist = true;
                PlaylistPosition = 0;
                if (OODConfig.DebugEnabled.Value) // Debug playlist info
                {
                    Logger.LogDebug("Playlist info");
                    Logger.LogDebug("Count: " + CurrentPlaylist.Count);
                    Logger.LogDebug(CurrentPlaylist.ToString());
                    Logger.LogDebug("Playing first url of " + CurrentPlaylist.ElementAt(PlaylistPosition).Url);
                }
                PlaylistURL = url;
                SetURL(CurrentPlaylist.ElementAt(PlaylistPosition).Url); // Play first url, when it finishes it will play the next one with the OnVideoEnd event
                if (UIController.selectionPanelObj)
                {
                    UIController.UpdatePlaylistUI();
                    UIController.toggleShuffleObj.GetComponentInChildren<Text>().text = "N";
                    UIController.toggleShuffleObj.SetActive(true);
                    UIController.toggleShuffleTextObj.SetActive(true);
                }
            }
        }

        public void PlayYoutube(string url) // Youtube urlgrab and play
        {
            if (urlGrab.LoadingBool) return;
            if (OODConfig.IsYtEnabled.Value)
            {
                if (PlayerSettings.IsLooping && (!mScreen.isLooping || !mAudio.loop))
                {
                    mScreen.isLooping = true;
                    mAudio.loop = true;
                }

                if (OODConfig.YoutubeAPI.Value == OODConfig.YouTubeAPI.YouTubeExplode)
                {
                    StartYoutubeAsync(url); // YoutubeExplode
                }
                else
                {
                    youtubeURLNode = url;
                    StartCoroutine(YoutubeNodeQuery()); // Youtube-dl nodejs
                }
            }
            else
            {
                StartCoroutine(UIController.UnavailableIndicator("YouTube disabled")); 
            }
        }

        public async void PlaySoundcloud(string sentUrl, bool isRPC) // Soundcloud urlgrab and play
        {
            downloadURL = null;

            UIController.loadingIndicatorObj.GetComponent<Text>().text = "Processing";
            UIController.loadingIndicatorObj.SetActive(true);
            //Jotunn.Logger.LogDebug("playing soundcloud");
            downloadURL = await urlGrab.GetSoundcloudExplode(sentUrl);
            if (downloadURL != null)
            {
                StartCoroutine(AudioWebRequest()); // Play audio
            }
            else
            {
                var message = "Error";
                if (urlGrab.StatusMessage.Contains("Timeout"))
                    message = "Soundcloud Timeout";
                else if (urlGrab.StatusMessage.Contains("Null"))
                    message = "Soundcloud not found";
                else if (urlGrab.StatusMessage.Contains("Error")) message = "Soundcloud Error";

                if (OODConfig.DebugEnabled.Value) Logger.LogDebug(message);

                StartCoroutine(UIController.UnavailableIndicator(message));
            }
        }

        private IEnumerator AudioWebRequest() // Pipes downloadhandler to audio clip and plays it
        {
            var dh = new DownloadHandlerAudioClip(downloadURL, AudioType.MPEG)
            {
                compressed = true // This
            };
            //Jotunn.Logger.LogDebug("testing coroutine");
            using var wr = new UnityWebRequest(downloadURL, "GET", dh, null);
            yield return wr.SendWebRequest();

            if (wr.result == UnityWebRequest.Result.ProtocolError ||
                wr.result == UnityWebRequest.Result.ConnectionError)
            {
                Logger.LogDebug(wr.error);
            }
            else
            {
                mAudio.clip = dh.audioClip;
                mScreen.url = "";
                mScreen.Stop();
                mAudio.Play();

                if (UIController.loadingIndicatorObj) UIController.loadingIndicatorObj.SetActive(false);
                if (animator) animator.SetBool(PlayerSettings.Playing, true);

                //Jotunn.Logger.LogDebug("playing??");
            }
        }

        public void RPC_BoomboxPlayVideo(string url) // RPC play, radios have different methods
        {
            mScreen.url = url;
            mAudio.clip = null;
            mScreen.Prepare();
        }

        public async void StartYoutubeAsync(string url, bool isRPC = false)
        {
            if (UIController.loadingIndicatorObj) 
            {
                UIController.SetLoadingIndicatorText("Processing");
                UIController.loadingIndicatorObj.SetActive(true);
            }
            var yt = await urlGrab.GetYoutubeExplode(url);

            if (yt != null)
            {
                if (yt.Contains("Timeout"))
                {
                    UIController.SetLoadingIndicatorText("Timeout/error");
                    await Task.Delay(1000);
                    // timeout/cancellation logic

                    UIController.ResetLoadingIndicatorText();
                }
                else if (yt.Contains("Error"))
                {
                    UIController.SetLoadingIndicatorText("Fatal error");

                    await Task.Delay(1000);

                    UIController.ResetLoadingIndicatorText();
                }
                else
                {

                    mScreen.url = yt;
                    mScreen.Prepare();
                    if (screenUICanvasObj && loadingCircleObj)
                    {
                        screenUICanvasObj.SetActive(true);
                        loadingCircleObj.SetActive(true);
                    }
                }
            }
            else
            {
                UIController.SetLoadingIndicatorText("Null video");

                await Task.Delay(1000);

                UIController.ResetLoadingIndicatorText();
            }
        }
        private IEnumerator YoutubeNodeQuery(bool isRPC = false)
        {
            if (youtubeURLNode == null)
            {
                Logger.LogDebug("error in yt query, youtube url null");
                yield break;
            }

            //clean url
            var url = Uri.EscapeDataString(youtubeURLNode);

            var nodeUrl = OODConfig.NodeUrl.Value;
            var authCode = OODConfig.YtAuthCode.Value;
            //begin query
            var www = UnityWebRequest.Get(nodeUrl + url + "/" + authCode);
            //Jotunn.Logger.LogDebug("url at: " + nodeUrl + url + "/" + authCode);
            www.timeout = 30;
            UIController.SetLoadingIndicatorText("Processing");
            UIController.loadingIndicatorObj.SetActive(true);


            yield return www.SendWebRequest();
            if (www.result != UnityWebRequest.Result.Success)
            {
                Debug.Log(www.error);
                UIController.loadingIndicatorObj.GetComponent<Text>().text = www.error;
                yield return new WaitForSeconds(2);
                UIController.loadingIndicatorObj.SetActive(false);
            }
            else if (www.downloadHandler.text.Contains("AUTH DENIED"))
            {
                UIController.SetLoadingIndicatorText("Invalid Auth");
                yield return new WaitForSeconds(2);
                UIController.loadingIndicatorObj.SetActive(false);
            }
            else
            {
                /*
                * this feels really messy but yt-dlp returns either seperate audio/video files or one single merged file depending on codec availability
                * maybe just remove node-js completely, but i like having the backup if needed.
                * the unfortunate thing about hacky api's like youtubeexplode is they break eventually, youtue-dlp has always been consistent in my experience 
                * nodejs server is set up to hopefully only return the merged file, but just in case it returns the split files we need to deal with that too
                */
                if (www.downloadHandler.text.Contains("\\"))
                {
                    // split files, seperate into different strings
                    var lines = www.downloadHandler.text.Split(
                        new[] { "\r\n", "\r", "\n", "\\n" },
                        StringSplitOptions.None
                    );

                    for (var i = 0; i < lines.Length; i++) //clean quotations from node return
                        lines[i] = lines[i].Replace("\"", "");

                    // clean uris

                    if (Uri.TryCreate(lines[0], UriKind.Absolute, out var cleanVideoUri))
                    {
                        //Jotunn.Logger.LogDebug("Clean URI: " + cleanVideoUri.AbsoluteUri);
                        youtubeVideoDirectUri = cleanVideoUri;
                    }
                    else
                    {
                        if (OODConfig.DebugEnabled.Value) Logger.LogDebug("Invalid URI: " + lines[0]);
                        UIController.SetLoadingIndicatorText("Invalid URI");
                        yield return new WaitForSeconds(2);
                        UIController.loadingIndicatorObj.SetActive(false);
                    }


                    if (Uri.TryCreate(lines[1], UriKind.Absolute, out var cleanSoundUri))
                    {
                        //Jotunn.Logger.LogDebug("Clean URI: " + cleanSoundUri.AbsoluteUri);
                        youtubeSoundDirectUri = cleanSoundUri;
                    }
                    else
                    {
                        if (OODConfig.DebugEnabled.Value) Logger.LogDebug("Invalid URI: " + lines[1]);
                        UIController.SetLoadingIndicatorText("Invalid URI");
                        yield return new WaitForSeconds(2);
                        UIController.loadingIndicatorObj.SetActive(false);
                    }

                    // make audio clip and play video
                    StartCoroutine(CreateYoutubeAudioAndPlay());
                }
                else
                {
                    //single file, clean url
                    var cleanUrl = www.downloadHandler.text.Replace("\"", "");
                    if (Uri.TryCreate(cleanUrl, UriKind.Absolute, out var cleanVideoUri))
                    {
                        //Jotunn.Logger.LogDebug("Clean URI: " + cleanVideoUri.AbsoluteUri);
                        youtubeVideoDirectUri = cleanVideoUri;
                    }
                    else
                    {
                        if (OODConfig.DebugEnabled.Value) Logger.LogDebug("Invalid URI: " + cleanUrl);
                        UIController.SetLoadingIndicatorText("Invalid URI");
                        yield return new WaitForSeconds(2);
                        UIController.loadingIndicatorObj.SetActive(false);
                    }
                    
                    
                    // play
                    mScreen.url = youtubeVideoDirectUri.AbsoluteUri;
                    mScreen.Prepare();
                    //m_screen.transform.Find("Plane").gameObject.SetActive(true);
                    //m_screen.Play();
                    if (screenUICanvasObj && loadingCircleObj)
                    {
                        screenUICanvasObj.SetActive(true);
                        loadingCircleObj.SetActive(true);
                    }

                    UIController.loadingIndicatorObj.SetActive(false);
                }
            }
        }

        private IEnumerator CreateYoutubeAudioAndPlay() // node js server returns split audio/video files, this function makes an audio clip from the audio file and plays the video
        {
            if (youtubeSoundDirectUri == null)
            {
                Logger.LogDebug("sound url is null, waiting");
                yield return new WaitForSeconds(1);
            }

            if (youtubeSoundDirectUri == null)
            {
                Logger.LogDebug("sound url is null still, exiting");
                yield break;
            }

            var dh = new DownloadHandlerAudioClip(youtubeSoundDirectUri, AudioType.UNKNOWN)
            {
                compressed = true // This
            };
            using var wr = new UnityWebRequest(youtubeSoundDirectUri, "GET", dh, null);
            yield return wr.SendWebRequest();

            if (wr.result == UnityWebRequest.Result.ProtocolError ||
                wr.result == UnityWebRequest.Result.ConnectionError)
            {
                Logger.LogDebug(wr.error);
                UIController.loadingIndicatorObj.SetActive(false);
            }
            else
            {
                mAudio.clip = dh.audioClip;
                
                if (youtubeVideoDirectUri != null)
                {
                    mScreen.url = youtubeVideoDirectUri.AbsoluteUri;
                    mScreen.Prepare();
                    if (screenUICanvasObj && loadingCircleObj)
                    {
                        screenUICanvasObj.SetActive(true);
                        loadingCircleObj.SetActive(true);
                    }

                    UIController.loadingIndicatorObj.SetActive(false);
                }
                else
                {
                    Logger.LogDebug("yt url is null");
                    UIController.loadingIndicatorObj.SetActive(false);
                }
            }
        }

        public void SetLock(bool locked) //Set player lock state
        {
            PlayerSettings.IsLocked = locked;
            if (UIController.selectionPanelObj)
            {
                if (PlayerSettings.IsLocked)
                {
                    UIController.lockedIconObj.SetActive(true);
                    UIController.unlockedIconObj.SetActive(false);
                }
                else
                {
                    UIController.lockedIconObj.SetActive(false);
                    UIController.unlockedIconObj.SetActive(true);
                }
            }
        }
        
        private void OnTriggerEnter(Collider other) //Show screen plane when player is in range
        {
            if (mScreen.isPlaying)
                if (screenPlaneObj && other.gameObject.layer == 9)
                    screenPlaneObj.SetActive(true);
        }

        private void OnTriggerExit(Collider other) //Hide screen plane when player is out of range
        {
            if (mScreen.isPlaying)
                if (screenPlaneObj && other.gameObject.layer == 9)
                    screenPlaneObj.SetActive(false);
        }

        private void OnTriggerStay(Collider other) //Show screen plane when player is in range
        {
            if (mScreen.isPlaying)
                if (screenPlaneObj && other.gameObject.layer == 9)
                    screenPlaneObj.SetActive(true);
        }

        public string GetHoverName()
        {
            return mName;
        }

        public string GetHoverText() 
        {
            if (PlayerSettings.AdminOnly && !SynchronizationManager.Instance.PlayerIsAdmin) return "";
            //TODO: control volume with no access
            if (!PrivateArea.CheckAccess(transform.position, 0f, false) && PlayerSettings.IsLocked)
                return Localization.instance.Localize(mName + "\n$piece_noaccess");
            if (PlayerSettings.IsLinkedToParent)
            {
                return Localization.instance.Localize(string.Concat("[<color=yellow><b>$KEY_Use</b></color>] $piece_use (Linked)" ));
            }
            return Localization.instance.Localize(string.Concat("[<color=yellow><b>$KEY_Use</b></color>] $piece_use "));
        }

        public bool Interact(Humanoid user, bool hold, bool alt) //Open screen UI
        {
            if ((!PrivateArea.CheckAccess(transform.position) && PlayerSettings.IsLocked) ||
                (PlayerSettings.AdminOnly && !SynchronizationManager.Instance.PlayerIsAdmin)) return false;


            UIController.ToggleMainPanel();

            return true;
        }

        public bool UseItem(Humanoid user, ItemDrop.ItemData item)
        {
            return false;
        }
        
        public async void RPC_UpdateZDO() //Update ZDO with a delay
        {
            await Task.Delay(350);
            UpdateZDO();
        }

        public void ClaimOwnership(ZDO zdo)
        {
            if (zdo.IsOwner())
                return;
            zdo.SetOwner(ZDOMan.instance.GetMyID());
        }

    }
}