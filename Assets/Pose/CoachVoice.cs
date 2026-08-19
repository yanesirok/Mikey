using System;

namespace Mikey.Pose
{
    /// <summary>
    /// Anything that can say a short coaching phrase out loud. Exists so
    /// <see cref="CoachVoice"/> is EditMode-testable without a device: production wires
    /// <see cref="AndroidVoiceAdapter"/>, tests wire a recorder.
    /// </summary>
    public interface IVoice
    {
        /// <summary>Says the phrase now, cancelling whatever is still being said.</summary>
        void Speak(string text);
    }

    /// <summary>
    /// Adapts <see cref="AndroidVoice"/> (native TTS on device, <c>Debug.Log</c> in the
    /// Editor) to <see cref="IVoice"/>. Owns the wrapped voice — disposing this disposes it.
    /// </summary>
    public sealed class AndroidVoiceAdapter : IVoice, IDisposable
    {
        private readonly AndroidVoice _voice = new AndroidVoice();

        public void Speak(string text) => _voice.Speak(text);

        public void Dispose() => _voice.Dispose();
    }

    /// <summary>
    /// Speaks an analyzer's <c>Cue</c> as it changes, with the anti-spam rule that makes
    /// live coaching bearable: a phrase is said when it is new, and repeated only after
    /// <c>repeatAfterSeconds</c> — otherwise every frame of a held fault would fire the
    /// same words. Framing prompts (<see cref="ExerciseFormState.NotVisible"/>) are never
    /// spoken: the player already sees them, and in a loop they are unbearable.
    /// Time is passed in, never read from the engine, so the rule is testable.
    /// Engine-free: the only UnityEngine dependency in this file is the adapter above.
    /// </summary>
    public sealed class CoachVoice
    {
        private readonly IVoice _voice;
        private readonly double _repeatAfterSeconds;
        private readonly double _minGapSeconds;

        private string _lastSpoken = string.Empty;
        private double _lastSpokenAt;

        public CoachVoice(IVoice voice, double repeatAfterSeconds = 2.0, double minGapSeconds = 1.2)
        {
            _voice = voice ?? throw new ArgumentNullException(nameof(voice));
            _repeatAfterSeconds = repeatAfterSeconds;
            _minGapSeconds = minGapSeconds;
        }

        /// <summary>The phrase most recently said aloud ("" before the first one).</summary>
        public string LastSpoken => _lastSpoken;

        /// <summary>
        /// Feeds the analyzer's current cue and state. Says the cue aloud when it is
        /// non-empty, the state is judgeable, and the phrase is either different from the
        /// last one spoken or older than the repeat window.
        /// </summary>
        public void Observe(string cue, ExerciseFormState state, double nowSeconds)
        {
            if (state == ExerciseFormState.NotVisible || string.IsNullOrEmpty(cue))
                return;

            bool sameAsLast = string.Equals(cue, _lastSpoken, StringComparison.Ordinal);
            if (sameAsLast && nowSeconds - _lastSpokenAt <= _repeatAfterSeconds)
                return;

            // Новая фраза раньше minGapSeconds обрывает предыдущую на полуслове: нативный
            // Speaker говорит через QUEUE_FLUSH. Вердикт повтора живёт один кадр позы, и
            // без этой паузы его немедленно затирает живая подсказка стойки. Держать паузу
            // здесь дешевле, чем растягивать вердикт в каждом анализаторе.
            if (!sameAsLast && nowSeconds - _lastSpokenAt < _minGapSeconds && _lastSpoken.Length > 0)
                return;

            _lastSpoken = cue;
            _lastSpokenAt = nowSeconds;
            _voice.Speak(cue);
        }

        /// <summary>Forgets the last phrase, so a fresh set starts coaching from scratch.</summary>
        public void Reset()
        {
            _lastSpoken = string.Empty;
            _lastSpokenAt = 0;
        }
    }
}
