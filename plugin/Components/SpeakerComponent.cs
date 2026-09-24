using OdinOnDemand.Utils.Net;
using UnityEngine;
using Logger = Jotunn.Logger;


namespace OdinOnDemand.Components
{
    public class SpeakerComponent : MonoBehaviour, Hoverable, Interactable
    {
        private const string GuidKey = "guid";

        public Piece mPiece { get; set; }
        public string mName;
        public ZNetView ZNetView;
        private string cachedGuid = "";

        /// <summary>
        ///     The id receivers link to. Speakers from older versions keep their saved guid. New ones
        ///     use their ZDO id, which every client agrees on, and the owner saves it so it survives
        ///     restarts. Clients used to invent random guids at the same time and overwrite each other.
        /// </summary>
        public string mGUID
        {
            get
            {
                var zdo = ZNetView ? ZNetView.GetZDO() : null;
                if (zdo == null) return cachedGuid;
                var guid = zdo.GetString(GuidKey);
                if (string.IsNullOrEmpty(guid))
                {
                    guid = zdo.m_uid.ToString();
                    if (zdo.IsOwner()) zdo.Set(GuidKey, guid);
                }
                cachedGuid = guid;
                return guid;
            }
        }

        private void Awake()
        {
            mPiece = GetComponentInChildren<Piece>();
            mName = mPiece.m_name;
            ZNetView = GetComponentInChildren<ZNetView>();
            
            ComponentLists.SpeakerComponentList.Add(this);
        }
        
        public void OnEnable()
        {
            if (mPiece.IsPlacedByPlayer()) _ = mGUID;
        }
        
        private void OnDestroy()
        {
            ComponentLists.SpeakerComponentList.Remove(this);
        }

        public string GetHoverName()
        {
            return mName;
        }

        public float GetHoverOffset()
        {
            return 0f;
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