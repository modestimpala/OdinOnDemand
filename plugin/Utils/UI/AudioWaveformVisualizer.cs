using System;
using UnityEngine;
using UnityEngine.UI;

namespace OdinOnDemand.Utils.UI
{
    public class AudioWaveformVisualizer : MonoBehaviour
    {
        public int barCount = 64; // Number of UI bars to create
        public int smoothing = 8; // Smoothing factor for the bars, lower value = more smoothing
        public RectTransform[] bars; // Bars array to hold UI bars
        public AudioSource audioSource; // AudioSource for the audio input
        public Gradient colorGradient; // Gradient for coloring the bars
        public float spacing = 10f; // Space between bars
        public float minHeight = 0.001f; // Minimum height for the bars
        public float maxHeight = 10f; // Max  height for the bars

        public bool enableGlowEffect = true; // Enable glow effect for bars
        public bool enableOutlineEffect = true; // Enable outline effect for bars
        
        private bool _initialUpdateDone = false; // Flag to ensure initial update
        [SerializeField] private int midStartIndex = 10;
        [SerializeField] private int midEndIndex = 32;
        [SerializeField] private float midScalingFactor = 5f;
        [SerializeField] private float highScalingFactor = 8.0f;

        public void Setup(AudioSource audio)
        {
            audioSource = audio;
            CreateBars();
            CreateGradient();
            Update();
        }

        private void CreateGradient()
        {
            colorGradient = new Gradient();

            // Create a gradient from blue (quiet) to yellow (medium) to red (loud)
            GradientColorKey[] colorKey = new GradientColorKey[3];
            colorKey[0].color = Color.green;
            colorKey[0].time = 0.0f; // Start of the gradient
            colorKey[1].color = Color.yellow;
            colorKey[1].time = 0.25f; // 1/4 of the gradient
            colorKey[2].color = new Color(255, 165, 0);
            colorKey[2].time = 1.0f; // End of the gradient

            // Define how the gradient's alpha transitions (not as crucial for this example, but necessary for a complete gradient definition)
            GradientAlphaKey[] alphaKey = new GradientAlphaKey[2];
            alphaKey[0].alpha = 1.0f;
            alphaKey[0].time = 0.0f;
            alphaKey[1].alpha = 1.0f;
            alphaKey[1].time = 1.0f;

            colorGradient.SetKeys(colorKey, alphaKey);
        }

        private void CreateBars()
        {
            bars = new RectTransform[barCount];

            float totalSpacing = spacing * (barCount - 1);
            float barWidth = (GetComponent<RectTransform>().rect.width - totalSpacing) / barCount;

            for (int i = 0; i < barCount; i++)
            {
                GameObject barObject = new GameObject("Bar " + i);
                barObject.transform.SetParent(transform, false);

                RectTransform barTransform = barObject.AddComponent<RectTransform>();

                // Set the bar's size
                barTransform.sizeDelta = new Vector2(barWidth, GetComponent<RectTransform>().rect.height);

                // Anchor bars to the middle horizontally and set their positions
                barTransform.anchorMin = new Vector2(0.5f, 0);
                barTransform.anchorMax = new Vector2(0.5f, 1);
                barTransform.pivot = new Vector2(0.5f, 0.5f);
                barTransform.anchoredPosition =
                    new Vector2((barWidth + spacing) * i - GetComponent<RectTransform>().rect.width / 2 + barWidth / 2,
                        0);

                // Add an Image component and set its sprite
                Image barImage = barObject.AddComponent<Image>();
                barImage.sprite = Sprite.Create(Texture2D.whiteTexture, new Rect(0, 0, 1, 1), new Vector2(0.5f, 0.5f));

                // Add optional effects to the bar
                if (enableGlowEffect)
                {
                    barImage.material = new Material(Shader.Find("UI/Default"));
                    barImage.material.EnableKeyword("_EMISSION");
                    barImage.material.SetColor("_EmissionColor", Color.white);
                }
                if (enableOutlineEffect)
                {
                    Outline outlineEffect = barObject.AddComponent<Outline>();
                    outlineEffect.effectColor = Color.black;
                }
                
                maxHeight = GetComponent<RectTransform>().rect.height;
                
                bars[i] = barTransform;
            }
        }

        private void Update()
        {
            if (!_initialUpdateDone || audioSource.isPlaying)
            {
                float[] spectrumData = new float[128];
                audioSource.GetSpectrumData(spectrumData, 0, FFTWindow.Rectangular);

                for (int i = 0; i < bars.Length; i++)
                {
                    // Determine the scaling factor based on the frequency range
                    float scalingFactor = 1.0f;
                    if (i >= midStartIndex && i <= midEndIndex) // Mid frequencies (vocals)
                        scalingFactor = midScalingFactor;
                    else if (i > midEndIndex) // High frequencies
                        scalingFactor = highScalingFactor;
                    if(i == 0)
                        scalingFactor = 0.25f; // Lowest frequencies
                    
                    float targetHeight = spectrumData[i] * scalingFactor * 10 + minHeight;
                    targetHeight = Mathf.Clamp(targetHeight, minHeight, maxHeight);

                    // Smoothly interpolate the current height towards the target height
                    float height = Mathf.Lerp(bars[i].localScale.y, targetHeight, Time.deltaTime * smoothing);
                    bars[i].localScale = new Vector3(1, height, 1);
                    
                    bars[i].GetComponent<Image>().color = colorGradient.Evaluate(spectrumData[i] * scalingFactor * 25);
                }

                _initialUpdateDone = true; // Mark that the initial update is done
            }
        }

    }
}
