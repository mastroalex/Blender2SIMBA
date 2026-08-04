using System;

namespace M2M.SIMBA
{
    [Serializable]
    public sealed class AnimatedField
    {
        public string Name = string.Empty;
        public string Units = string.Empty;
        public float GlobalMin;
        public float GlobalMax;
        public float[] FrameMin = Array.Empty<float>();
        public float[] FrameMax = Array.Empty<float>();
        public float[][] Values = Array.Empty<float[]>();
    }
}
