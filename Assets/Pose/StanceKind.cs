namespace Mikey.Pose
{
    /// <summary>
    /// The karate stances level 1 teaches, plus <see cref="None"/> — "no stance
    /// recognized here". A technique gated on a stance reads <see cref="None"/> as
    /// "do not score this rep at all", which is why it is a value and not a null.
    /// </summary>
    public enum StanceKind
    {
        None,
        Fudo,
        Zenkutsu,
    }
}
