namespace Mikey.UI.Profile
{
    /// <summary>
    /// Pure validation for <see cref="ProfileUserData"/>'s numeric/choice fields.
    /// Display Name reuses <see cref="ProfileDisplayNameStorage.Validate"/>
    /// instead of duplicating that rule. No UnityEngine dependency, so directly
    /// unit-testable — mirrors <c>Mikey.UI.Map.MapPanZoomMath</c>'s pure-static-
    /// class pattern.
    /// </summary>
    public static class ProfileUserDataValidation
    {
        public const int MinAge = 10;
        public const int MaxAge = 100;
        public const float MinWeightKg = 30f;
        public const float MaxWeightKg = 300f;
        public const int MinHeightCm = 100;
        public const int MaxHeightCm = 250;

        public static bool IsValidAge(int age) => age >= MinAge && age <= MaxAge;

        public static bool IsValidWeightKg(float weightKg) => weightKg >= MinWeightKg && weightKg <= MaxWeightKg;

        public static bool IsValidHeightCm(int heightCm) => heightCm >= MinHeightCm && heightCm <= MaxHeightCm;

        public static bool IsValidGender(string gender) =>
            gender == ProfileUserData.GenderMale ||
            gender == ProfileUserData.GenderFemale ||
            gender == ProfileUserData.GenderOther ||
            gender == ProfileUserData.GenderPreferNotToSay;
    }
}
