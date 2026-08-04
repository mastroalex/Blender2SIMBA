using System;
using System.IO;
using UnityEngine;

namespace M2M.SIMBA
{
    public static class SIMBAUtilities
    {
        /// <summary>Captures a PNG screenshot at the end of the current frame.</summary>
        public static string CaptureScreenshot(string directory = null, string fileName = null, int superSize = 1)
        {
            directory ??= Path.Combine(Application.persistentDataPath, "SIMBA Screenshots");
            Directory.CreateDirectory(directory);
            fileName ??= $"SIMBA_{DateTime.Now:yyyyMMdd_HHmmss}.png";
            if (!fileName.EndsWith(".png", StringComparison.OrdinalIgnoreCase)) fileName += ".png";
            string path = Path.Combine(directory, fileName);
            ScreenCapture.CaptureScreenshot(path, Mathf.Max(1, superSize));
            return path;
        }

        public static SIMBAPlayer FindPlayer(GameObject gameObject)
        {
            return gameObject != null ? gameObject.GetComponent<SIMBAPlayer>() : null;
        }
    }
}
