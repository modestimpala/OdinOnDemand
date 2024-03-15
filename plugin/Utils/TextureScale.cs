using UnityEngine;

namespace OdinOnDemand.Utils
{
    public class TextureScale
    {
        public static void Point(Texture2D tex, int newWidth, int newHeight)
        {
            Texture2D result = new Texture2D(newWidth, newHeight, tex.format, false);
            for (int i = 0; i < result.height; ++i)
            {
                for (int j = 0; j < result.width; ++j)
                {
                    Color newColor = tex.GetPixelBilinear((float)j / result.width, (float)i / result.height);
                    result.SetPixel(j, i, newColor);
                }
            }
            result.Apply();
            // Replace old texture with resized one
            tex.Resize(newWidth, newHeight);
            tex.SetPixels(result.GetPixels());
            tex.Apply();
        }
    }
}