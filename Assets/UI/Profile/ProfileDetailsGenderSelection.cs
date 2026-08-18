namespace Mikey.UI.Profile
{
    /// <summary>
    /// Pure selection-state logic for the Gender chip group: given the full
    /// ordered set of options and the chosen value, which single option (if any)
    /// should read as selected. Kept separate from
    /// <see cref="ProfileDetailsController"/> so the "exactly one selected, or
    /// none until a choice is made" invariant is directly unit-testable without a
    /// live UI Toolkit panel — mirrors <c>ProfileRadarMath</c>'s pure-geometry
    /// pattern. The controller calls this rather than reimplementing the
    /// comparison loop, so the tested logic is the actual logic running.
    /// </summary>
    public static class ProfileDetailsGenderSelection
    {
        /// <summary>flags[i] is true iff options[i] == selectedValue. At most one entry is ever true.</summary>
        public static bool[] ComputeSelectedFlags(string[] options, string selectedValue)
        {
            var flags = new bool[options.Length];
            for (int i = 0; i < options.Length; i++)
                flags[i] = options[i] == selectedValue;
            return flags;
        }
    }
}
