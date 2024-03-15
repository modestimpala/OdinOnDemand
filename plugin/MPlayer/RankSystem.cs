using System.Collections;
using Jotunn.Managers;
using OdinOnDemand.Utils.Config;
using UnityEngine;
using UnityEngine.Serialization;
using UnityEngine.UI;
using Logger = Jotunn.Logger;

namespace OdinOnDemand.Utils
{
    public class RankSystem : MonoBehaviour
    {
        private static GameObject _blockedText;
        private static CoroutineManager _coroutineManager;
        private static Coroutine _blockMenuCoroutine;

        public static PlayerRank GetRank(string steam)
        {
            if(ZNet.instance.ListContainsId(ZNet.instance.m_adminList, steam))
            {
                return PlayerRank.Admin;
            } else if (OODConfig.VipList.Value.Contains(steam))
            {
                return PlayerRank.Vip;
            }
            return PlayerRank.Player;
        }

        private static void CreateBlockMenu()
        {
            if (!_blockedText)
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
                
                _blockedText = GUIManager.Instance.CreateText(
                    OODConfig.VipMessage.Value,
                    GUIManager.CustomGUIFront.transform,
                    new Vector2(0.5f, 0.5f),
                    new Vector2(0.5f, 0.5f),
                    new Vector2(0f, -150f),
                    GUIManager.Instance.AveriaSerifBold,
                    22,
                    GUIManager.Instance.ValheimOrange,
                    true,
                    Color.black,
                    450f,
                    40f,
                    false);
                _blockedText.SetActive(false);
            }
        }

        public static void DisplayBlockMenu()
        {
            if (_coroutineManager == null)
            {
                GameObject go = new GameObject("CoroutineManager");
                _coroutineManager = go.AddComponent<CoroutineManager>();
                DontDestroyOnLoad(go);
            }

            if (!_blockedText)
            {
                CreateBlockMenu();
            }

            // If coroutine is running, stop it
            if (_blockMenuCoroutine != null)
            {
                _coroutineManager.StopCoroutine(_blockMenuCoroutine);
            }

            // Start the coroutine
            _blockMenuCoroutine = _coroutineManager.StartCoroutine(ShowBlockMenu());
        }

        private static IEnumerator ShowBlockMenu()
        {
            if (_blockedText.activeSelf)
            {
                // If blockedText is already active, deactivate it and end the coroutine
                _blockedText.SetActive(false);
                yield break;
            }

            // Show blockedText
            _blockedText.SetActive(true);
            yield return new WaitForSeconds(3);
            _blockedText.SetActive(false);
        }

        public enum PlayerRank
        {
            Player,
            Vip,
            Admin
        }
        private class CoroutineManager : MonoBehaviour { }
    }
}