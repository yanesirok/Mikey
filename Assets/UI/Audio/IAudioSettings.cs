using System;

namespace Mikey.UI.Audio
{
    /// <summary>
    /// Shared read/write access to the three MVP audio volumes (Music, SFX, Trainer
    /// Voice), each a normalized 0..1 value persisted locally. Lives in this
    /// assembly (autoReferenced) so per-screen assemblies (e.g. Home, for its
    /// Settings modal sliders) can each reach the single instance living on the
    /// shared "UI" GameObject via <c>GetComponent&lt;IAudioSettings&gt;()</c> —
    /// mirrors how <see cref="Mikey.UI.Progression.ITutorialProgress"/> is shared.
    /// Trainer Voice is settings-only for now: nothing plays it back yet.
    /// </summary>
    public interface IAudioSettings
    {
        /// <summary>Raised whenever any volume actually changes (including via persistence load).</summary>
        event Action Changed;

        /// <summary>Menu/soundtrack music volume, 0..1. Safe default is 0.70.</summary>
        float MusicVolume { get; set; }

        /// <summary>UI sound-effect volume, 0..1. Safe default is 1.00.</summary>
        float SfxVolume { get; set; }

        /// <summary>Trainer voice-line volume, 0..1. Safe default is 1.00. Not yet used for playback.</summary>
        float TrainerVoiceVolume { get; set; }
    }
}
