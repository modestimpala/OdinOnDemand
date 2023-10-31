using UnityEngine;

namespace OdinOnDemand.Components
{
    public class SpeakerComponent : MonoBehaviour, Hoverable, Interactable
    {
        public Piece mPiece { get; set; }
        public string mName;

        private void Awake()
        {
            mPiece = gameObject.GetComponentInChildren<Piece>();
            mName = mPiece.m_name;
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