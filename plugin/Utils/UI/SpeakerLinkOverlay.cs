using System.Collections.Generic;
using OdinOnDemand.Components;
using OdinOnDemand.MPlayer;
using OdinOnDemand.Utils.Net;
using UnityEngine;
using UnityEngine.Rendering;

namespace OdinOnDemand.Utils.UI
{
    /// <summary>
    ///     Debug lines from each loaded media player to its linked speakers, toggled from the
    ///     remote. Green reaches a loaded speaker, orange only a saved position (the speaker is
    ///     unloaded, or moved or rebuilt since it was linked). In center mode a violet post
    ///     marks where the sound plays from.
    /// </summary>
    internal sealed class SpeakerLinkOverlay : MonoBehaviour
    {
        private const float RefreshSeconds = 0.25f;
        private const float LineWidth = 0.04f;
        private const float CenterPostHeight = 2f;
        private static readonly Vector3 Lift = Vector3.up * 0.5f;
        private static readonly Color LoadedColor = new Color(0.3f, 1f, 0.4f);
        private static readonly Color SavedOnlyColor = new Color(1f, 0.55f, 0.1f);
        private static readonly Color CenterColor = new Color(0.8f, 0.4f, 1f);

        private static SpeakerLinkOverlay instance;
        private static Object owner;

        private readonly List<LineRenderer> lines = new List<LineRenderer>();
        private readonly HashSet<string> loadedGuids = new HashSet<string>();
        private readonly List<Vector3> loadedPositions = new List<Vector3>();
        private Material material;
        private float nextRefresh;
        private int used;

        /// <summary>Shows or hides the lines. Returns whether they are now shown.</summary>
        internal static bool Toggle(Object by)
        {
            if (!instance)
            {
                var overlayObj = new GameObject("OODSpeakerLinkOverlay");
                DontDestroyOnLoad(overlayObj);
                instance = overlayObj.AddComponent<SpeakerLinkOverlay>();
                instance.enabled = false;
            }

            instance.enabled = !instance.enabled;
            owner = by;
            return instance.enabled;
        }

        /// <summary>Hides the lines if <paramref name="by" /> turned them on.</summary>
        internal static void Hide(Object by)
        {
            if (instance && instance.enabled && ReferenceEquals(owner, by)) instance.enabled = false;
        }

        private void OnEnable()
        {
            nextRefresh = 0f;
        }

        private void OnDisable()
        {
            foreach (var line in lines)
                if (line) line.gameObject.SetActive(false);
        }

        private void Update()
        {
            if (Time.time < nextRefresh) return;
            nextRefresh = Time.time + RefreshSeconds;

            CollectLoadedSpeakers();
            used = 0;
            foreach (var list in ComponentLists.MediaComponentLists.Values)
            {
                foreach (BasePlayer player in list)
                {
                    if (!player || player.SpeakerCount == 0) continue;
                    var from = player.transform.position + Lift;
                    foreach (var link in player.SpeakerLinks)
                        Draw(from, link.Position + Lift, IsLoaded(link) ? LoadedColor : SavedOnlyColor);
                    if (player.PlayerSettings.SpeakerOutput == SpeakerMode.Center)
                    {
                        var center = player.AudioPosition;
                        Draw(center, center + Vector3.up * CenterPostHeight, CenterColor);
                    }
                }
            }

            for (int i = used; i < lines.Count; i++)
                if (lines[i]) lines[i].gameObject.SetActive(false);
        }

        private void CollectLoadedSpeakers()
        {
            loadedGuids.Clear();
            loadedPositions.Clear();
            foreach (var speaker in ComponentLists.SpeakerComponentList)
            {
                if (!speaker) continue;
                loadedGuids.Add(speaker.mGUID);
                loadedPositions.Add(speaker.transform.position);
            }
        }

        // Same matching as BasePlayer.FindSpeakerLink: the guid, else the saved position.
        private bool IsLoaded(SpeakerLink link)
        {
            if (link.Guid.Length > 0 && loadedGuids.Contains(link.Guid)) return true;
            foreach (var position in loadedPositions)
                if ((position - link.Position).sqrMagnitude < 0.01f) return true;
            return false;
        }

        private void Draw(Vector3 from, Vector3 to, Color color)
        {
            if (used == lines.Count) lines.Add(CreateLine());
            var line = lines[used++];
            line.gameObject.SetActive(true);
            line.SetPosition(0, from);
            line.SetPosition(1, to);
            line.startColor = color;
            line.endColor = color;
        }

        private LineRenderer CreateLine()
        {
            if (!material)
            {
                var shader = Shader.Find("Sprites/Default");
                if (!shader) shader = Shader.Find("UI/Default");
                material = new Material(shader);
            }

            var lineObj = new GameObject("OODSpeakerLink");
            lineObj.transform.SetParent(transform, false);
            var line = lineObj.AddComponent<LineRenderer>();
            line.sharedMaterial = material;
            line.useWorldSpace = true;
            line.positionCount = 2;
            line.widthMultiplier = LineWidth;
            line.shadowCastingMode = ShadowCastingMode.Off;
            line.receiveShadows = false;
            return line;
        }
    }
}
