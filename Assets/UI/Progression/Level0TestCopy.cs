namespace Mikey.UI.Progression
{
    /// <summary>
    /// Display copy for each Level 0 Combine test, sourced verbatim from the
    /// locked design reference. The one place this copy lives — the Combine
    /// screen and Camera Test HUD read from here rather than hardcoding text.
    /// </summary>
    public static class Level0TestCopy
    {
        public static string TitleFor(Level0Test test)
        {
            switch (test)
            {
                case Level0Test.CameraTest: return "Camera Test";
                case Level0Test.PushUps: return "Max Push-Ups";
                case Level0Test.Squats: return "Max Squats";
                case Level0Test.WallSit: return "Wall Sit";
                case Level0Test.YokoGeri: return "Slow Yoko-Geri";
                default: return string.Empty;
            }
        }

        /// <summary>Non-empty only for Yoko-Geri: the Gedan/Chudan/Jodan sequence line.</summary>
        public static string SecondaryFor(Level0Test test) =>
            test == Level0Test.YokoGeri ? "GEDAN → CHUDAN → JODAN" : string.Empty;

        public static string DescriptionFor(Level0Test test)
        {
            switch (test)
            {
                case Level0Test.CameraTest: return "Calibrate camera tracking before the assessment.";
                case Level0Test.PushUps: return "Complete as many strict repetitions as possible.";
                case Level0Test.Squats: return "Complete as many strict repetitions as possible.";
                case Level0Test.WallSit: return "Hold the wall-sit position as long as possible.";
                case Level0Test.YokoGeri: return "Perform the slow side-kick sequence continuously from low to middle to high.";
                default: return string.Empty;
            }
        }

        /// <summary>Stat/category line. Empty for Camera Test — calibration, not a graded stat.</summary>
        public static string StatFor(Level0Test test)
        {
            switch (test)
            {
                case Level0Test.PushUps: return "POWER / ENDURANCE";
                case Level0Test.Squats: return "LOWER-BODY POWER / ENDURANCE";
                case Level0Test.WallSit: return "ENDURANCE";
                case Level0Test.YokoGeri: return "CONTROL / FLEXIBILITY / BALANCE";
                default: return string.Empty;
            }
        }

        /// <summary>Filename, under Media/Images/combine/, of this test's checklist/panel illustration.</summary>
        public static string IllustrationFileName(Level0Test test)
        {
            switch (test)
            {
                case Level0Test.CameraTest: return "combine_camera.png";
                case Level0Test.PushUps: return "combine_pushups.png";
                case Level0Test.Squats: return "combine_squats.png";
                case Level0Test.WallSit: return "combine_wallsit.png";
                case Level0Test.YokoGeri: return "combine_yokogeri.png";
                default: return string.Empty;
            }
        }
    }
}
