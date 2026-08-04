using System.Collections.Generic;
using System.Globalization;
using System.IO;
using UnityEngine;
using Mikey.Pose;

namespace Mikey.Pose.Tests
{
    /// <summary>
    /// Loads a pose recording CSV (the on-device format PoseController writes:
    /// t,x0,y0,z0,v0,…,x32,y32,z32,v32) into frames, so real captured movement can be
    /// replayed through the actual analyzers as a regression corpus.
    /// </summary>
    internal static class CsvPoseFrames
    {
        public static List<PoseFrame> Load(string assetsRelativePath)
        {
            string full = Path.Combine(Application.dataPath, assetsRelativePath);
            var frames = new List<PoseFrame>();
            foreach (string line in File.ReadLines(full))
            {
                string[] parts = line.Split(',');
                if (parts.Length < 1 + PoseFrame.LandmarkCount * 4)
                    continue;
                if (!double.TryParse(parts[0], NumberStyles.Float, CultureInfo.InvariantCulture, out double t))
                    continue; // заголовок или мусор
                var lm = new PoseLandmark[PoseFrame.LandmarkCount];
                bool ok = true;
                for (int i = 0; i < PoseFrame.LandmarkCount && ok; i++)
                {
                    float x = 0, y = 0, z = 0, v = 0;
                    ok = TryF(parts[1 + i * 4], out x) && TryF(parts[2 + i * 4], out y)
                      && TryF(parts[3 + i * 4], out z) && TryF(parts[4 + i * 4], out v);
                    if (ok)
                        lm[i] = new PoseLandmark(x, y, z, v);
                }
                if (ok)
                    frames.Add(new PoseFrame(lm, t));
            }
            return frames;
        }

        private static bool TryF(string s, out float value)
        {
            return float.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out value);
        }
    }
}
