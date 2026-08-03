using UnityEngine;

namespace M2M.SIMBA
{
    public enum SIMBAColorMap
    {
        Turbo, Viridis, Plasma, Inferno, Magma, Cividis, Jet,
        Coolwarm, Hot, Gray, Rainbow, Spring, Summer, Autumn, Winter,
        Custom
    }

    public static class SIMBAColorMaps
    {
        public static Texture2D Load(SIMBAColorMap preset)
        {
            if (preset == SIMBAColorMap.Custom) return null;
            Texture2D texture = Resources.Load<Texture2D>($"SIMBA/Colormaps/{preset}");
            if (texture != null)
            {
                texture.wrapMode = TextureWrapMode.Clamp;
                texture.filterMode = FilterMode.Bilinear;
            }
            return texture;
        }
    }
}
