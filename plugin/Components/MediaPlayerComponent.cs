using Jotunn.Managers;
using OdinOnDemand.MPlayer;
using OdinOnDemand.Utils;
using OdinOnDemand.Utils.Config;
using OdinOnDemand.Utils.Net;
using OdinOnDemand.Utils.UI;
using UnityEngine;

namespace OdinOnDemand.Components
{
    public class MediaPlayerComponent : BasePlayer, Hoverable, Interactable
    {
        private RenderTexture RenderTexture { get; set; }
        

        public new void Awake()
        {
            base.Awake();
            //init components
            mPiece = gameObject.GetComponentInChildren<Piece>();
            mName = mPiece.m_name;
            
            // Identify and set the type of player
            if (mName.Contains("boombox") || mName.Contains("radio")) //TODO separate classes
            {
                PlayerSettings.PlayerType = CinemaPackage.MediaPlayers.Radio;
            }
            else if (mName.Contains("gramophone")) // If it's a gramophone, we need to set the Animator 
            {
                PlayerSettings.PlayerType = CinemaPackage.MediaPlayers.Radio;
                Animator = gameObject.GetComponentInChildren<Animator>();
                Animator.SetBool(PlayerSettings.Playing, false);
            }
            else
            {
                PlayerSettings.PlayerType = CinemaPackage.MediaPlayers.CinemaScreen;
            }
            if(transform.Find("screenUICanvas"))
            {
                ScreenUICanvasObj = transform.Find("screenUICanvas").gameObject;
                RadioPanelObj = ScreenUICanvasObj.transform.Find("mainCanvas/radioPanel").gameObject;
            }
            SetupScreen(); // TODO consolidate 
            SetupRadioPanel();
            InvokeRepeating(nameof(DropoffUpdate), 0.05f, 0.05f);
        }

        public void Start()
        {
            // add trigger collider if we need it for screen disable or audio fade type is toggle
            if (OODConfig.ScreenDisableOutOfRange.Value)
            {
                triggerCollider = gameObject.AddComponent<SphereCollider>();
                triggerCollider.isTrigger = true;
                triggerCollider.radius = OODConfig.DefaultDistance.Value;
            }
        }

        public void OnEnable()
        {
            if (mPiece.IsPlacedByPlayer())
            {
                LoadZDO(); // If the player is placed by a player, load the zdo data to init}
            }
        }

        public void OnDisable()
        { 
            SaveZDO();
        }

        private new void OnDestroy()
        {
            base.OnDestroy();
            //cLean up
            if (RenderTexture) RenderTexture.Release();
            if (PlayerSettings
                .IsGuiActive) // If the GUI is active, close it so it's not stuck open. Solves edge case where player is destroyed while GUI is open
            {
                PlayerSettings.IsGuiActive = false;
                if (UIController.URLPanelObj) UIController.URLPanelObj.SetActive(false);
                GUIManager.BlockInput(PlayerSettings.IsGuiActive);
            }

            if (UIController.URLPanelObj) Destroy(UIController.URLPanelObj);
        }

        public override void LoadZDO()
        {
            base.LoadZDO();
            var zdo = ZNetView.GetZDO();
            if (zdo == null) return;
            PlayerSettings.VerticalDistanceDropoff = zdo.GetFloat("verticalDropoff");
            if (zdo.GetFloat("dropoffPower") != 0f)
            {
                PlayerSettings.DropoffPower = zdo.GetFloat("dropoffPower");
            }
        }

        public override void UpdateZDO() // For periodic updates to the ZDO, usually from RPC when a player changes something
        {
            base.UpdateZDO();
            var zdo = ZNetView.GetZDO();
            if (zdo != null)
            {
                PlayerSettings.VerticalDistanceDropoff = zdo.GetFloat("verticalDropoff");
            }
        }
        
        public override void SaveZDO(bool saveTime = true) // Save all our ZDO settings
        {
            base.SaveZDO(saveTime);
            var zdo = ZNetView.GetZDO();
            if (zdo == null || mAudio == null) return;
            zdo.Set("verticalDropoff", PlayerSettings.VerticalDistanceDropoff);
            zdo.Set("dropoffPower", PlayerSettings.DropoffPower);
        }

        private void DropoffUpdate()
        {
            if (PlayerSettings.IsPaused || !PlayerSettings.IsPlaying || UnparsedURL == null) return;
            //vertical distance dropoff
            if (PlayerSettings.VerticalDistanceDropoff != 0f)
            {
                // Get the player's position
                var mLocalPlayer = Player.m_localPlayer;
                if (mLocalPlayer == null) return;
                Vector3 playerPosition = mLocalPlayer.transform.position;

                // Calculate the absolute difference in height
                float heightDifference = Mathf.Abs(playerPosition.y - this.transform.position.y);

                // If the height difference is greater than the dropoff distance
                if (heightDifference > PlayerSettings.VerticalDistanceDropoff)
                {
                    // Decrease the volume based on the square of the height difference
                    var volumeDecrease = 1 - 1 / Mathf.Pow(heightDifference + 1, PlayerSettings.DropoffPower);
                    mAudio.volume = Mathf.Max(0, PlayerSettings.Volume - volumeDecrease);
                }
                else
                {
                    // If the player is within the dropoff distance, set the volume to the original volume
                    mAudio.volume = PlayerSettings.Volume;
                }
            }
        }

        private void SetupScreen() //Setup the screen, called on awake
        {
            SetupCinemaScreen();
            
            mScreen.url = "";
            mScreen.Pause();
        }

        private void SetupCinemaScreen()
        {
            if(PlayerSettings.PlayerType != CinemaPackage.MediaPlayers.CinemaScreen) return;
            ScreenPlaneObj = mScreen.transform.Find("Plane").gameObject;
            //Depending on config value, we have two different types of materials for our screen. One is affected by in-game light, the other is not.
            if (OODConfig.VideoBacklight.Value)
            {
                //Render texture with Unlit shader
                RenderTexture = new RenderTexture(1920, 1080, 0, RenderTextureFormat.ARGB32);
                RenderTexture.Create();
                RenderTexture.Release();
                TargetTexMat = new Material(OdinOnDemandPlugin.MainScreenMat.shader)
                {
                    mainTexture = RenderTexture
                };
                //set to our screen
                ScreenPlaneObj.GetComponent<MeshRenderer>().material = TargetTexMat;
                mScreen.targetTexture = RenderTexture;
            
                LoadingCircleObj = ScreenUICanvasObj.transform.Find("mainCanvas/Loading Circle").gameObject;
            
                if(LoadingCircleObj)
                {
                    //Loading progress circle for screen display
                    var progressCircle = LoadingCircleObj.transform.Find("Progress").gameObject;
                    progressCircle.AddComponent<LoadingCircle>();
                }
                ScreenPlaneObj.SetActive(false);
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
                TargetTexMat = new Material(shader);
                TargetTexMat.EnableKeyword("_EMISSION");
                TargetTexMat.SetTexture(PlayerSettings.MainTex, RenderTexture);
                TargetTexMat.SetTexture(PlayerSettings.EmissiveColorMap, RenderTexture);
                //set to our screen
                ScreenPlaneObj.GetComponent<MeshRenderer>().material = TargetTexMat;
            }
        }

        private void OnTriggerEnter(Collider other) //Show screen plane when player is in range
        {
            if (mScreen.isPlaying && OODConfig.ScreenDisableOutOfRange.Value)
                if (ScreenPlaneObj && other.gameObject.layer == 9)
                    ScreenPlaneObj.SetActive(true);
        }

        private void OnTriggerExit(Collider other) //Hide screen plane when player is out of range
        {
            if (mScreen.isPlaying && OODConfig.ScreenDisableOutOfRange.Value)
                if (ScreenPlaneObj && other.gameObject.layer == 9)
                    ScreenPlaneObj.SetActive(false);
        }

        private void OnTriggerStay(Collider other) //Show screen plane when player is in range
        {
            if (mScreen.isPlaying && OODConfig.ScreenDisableOutOfRange.Value)
                if (ScreenPlaneObj && other.gameObject.layer == 9)
                    ScreenPlaneObj.SetActive(true);
        }

        public string GetHoverName()
        {
            return mName;
        }

        public string GetHoverText()
        {
            if (PlayerSettings.AdminOnly && !SynchronizationManager.Instance.PlayerIsAdmin) return "";
            //TODO: control volume with no access
            if (OODConfig.VipMode.Value)
            {
                var rank = RankSystem.GetRank(Steamworks.SteamUser.GetSteamID().ToString());
                if (rank != RankSystem.PlayerRank.Admin && rank != RankSystem.PlayerRank.Vip)
                {
                    return Localization.instance.Localize(mName + "\n$piece_noaccess");
                }
            }

            if (!PrivateArea.CheckAccess(transform.position, 0f, false) && PlayerSettings.IsLocked)
                return Localization.instance.Localize(mName + "\n$piece_noaccess");
            if (PlayerSettings.IsLinkedToParent)
            {
                return Localization.instance.Localize(
                    string.Concat("[<color=yellow><b>$KEY_Use</b></color>] $piece_use (Linked)"));
            }

            return Localization.instance.Localize(string.Concat("[<color=yellow><b>$KEY_Use</b></color>] $piece_use "));
        }

        public bool Interact(Humanoid user, bool hold, bool alt) //Open screen UI
        {
            if (OODConfig.VipMode.Value)
            {
                var rank = RankSystem.GetRank(Steamworks.SteamUser.GetSteamID().ToString());
                if (rank != RankSystem.PlayerRank.Admin && rank != RankSystem.PlayerRank.Vip)
                {
                    return false;
                }
            }

            if ((!PrivateArea.CheckAccess(transform.position) && PlayerSettings.IsLocked) ||
                (PlayerSettings.AdminOnly && !SynchronizationManager.Instance.PlayerIsAdmin)) return false;

            UIController.ToggleMainPanel();

            return true;
        }

        public bool UseItem(Humanoid user, ItemDrop.ItemData item)
        {
            return false;
        }
        
    }
}