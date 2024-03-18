using System;
using OdinOnDemand.Utils.Net;
using UnityEngine;
using Logger = Jotunn.Logger;


namespace OdinOnDemand.Components
{
    public class SpeakerComponent : MonoBehaviour, Hoverable, Interactable
    {
        public Piece mPiece { get; set; }
        public string mName;
        public string mGUID = "";
        public ZNetView ZNetView;

        private void Awake()
        {
            mPiece = GetComponentInChildren<Piece>();
            mName = mPiece.m_name;
            ZNetView = GetComponentInChildren<ZNetView>();
            
            ComponentLists.SpeakerComponentList.Add(this);
        }
        
        public void OnEnable()
        {
            if (mPiece.IsPlacedByPlayer())
            {
                LoadZDO(); // If the player is placed by a player, load the zdo data to init}
                if (string.IsNullOrEmpty(mGUID))
                {
                    mGUID = System.IO.Path.GetRandomFileName().Replace(".", "") + "-" + DateTime.Now.Ticks;
                    SaveZDO();
                }
            }
        }
        
        private void OnDestroy()
        {
            ComponentLists.SpeakerComponentList.Remove(this);
        }
        
        public void SaveZDO()
        {
            ZDO zdo = ZNetView.GetZDO();
            if (!zdo.IsOwner())
                zdo.SetOwner(ZDOMan.GetSessionID());
            zdo.Set("guid", mGUID);
        }
        
        public void LoadZDO()
        {
            ZDO zdo = ZNetView.GetZDO();
            mGUID = zdo.GetString("guid", mGUID);
        }

        public string GetHoverName()
        {
            return mName;
        }

        public string GetHoverText()
        {
            return Localization.instance.Localize(string.Concat(mName));
            
        }

        public bool Interact(Humanoid user, bool hold, bool alt)
        {
            return false;
        }

        public bool UseItem(Humanoid user, ItemDrop.ItemData item)
        {
            return false;
        }

    }
}