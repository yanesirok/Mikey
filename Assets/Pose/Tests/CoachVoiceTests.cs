using System.Collections.Generic;
using NUnit.Framework;

namespace Mikey.Pose.Tests
{
    public class CoachVoiceTests
    {
        /// <summary>Records what would have been spoken, so the anti-spam rule is testable off-device.</summary>
        private sealed class FakeVoice : IVoice
        {
            public readonly List<string> Said = new List<string>();

            public void Speak(string text) => Said.Add(text);
        }

        [Test]
        public void FirstCueIsSpoken()
        {
            var voice = new FakeVoice();
            var coach = new CoachVoice(voice);
            coach.Observe("Ниже", ExerciseFormState.BadForm, 0.0);
            CollectionAssert.AreEqual(new[] { "Ниже" }, voice.Said);
        }

        [Test]
        public void SameCueWithinRepeatWindowStaysSilent()
        {
            var voice = new FakeVoice();
            var coach = new CoachVoice(voice, repeatAfterSeconds: 2.0);
            coach.Observe("Ниже", ExerciseFormState.BadForm, 0.0);
            // Тот же огрех держится десятки кадров подряд — вслух он звучит один раз.
            for (double t = 0.1; t <= 1.9; t += 0.1)
                coach.Observe("Ниже", ExerciseFormState.BadForm, t);
            CollectionAssert.AreEqual(new[] { "Ниже" }, voice.Said);
        }

        [Test]
        public void SameCueAfterRepeatWindowIsSpokenAgain()
        {
            var voice = new FakeVoice();
            var coach = new CoachVoice(voice, repeatAfterSeconds: 2.0);
            coach.Observe("Ниже", ExerciseFormState.BadForm, 0.0);
            coach.Observe("Ниже", ExerciseFormState.BadForm, 2.5);
            CollectionAssert.AreEqual(new[] { "Ниже", "Ниже" }, voice.Said);
        }

        [Test]
        public void ChangedCueIsSpokenOnceTheGapHasPassed()
        {
            var voice = new FakeVoice();
            var coach = new CoachVoice(voice, repeatAfterSeconds: 2.0, minGapSeconds: 1.2);
            coach.Observe("Ниже", ExerciseFormState.BadForm, 0.0);
            coach.Observe("Корпус прямо", ExerciseFormState.BadForm, 1.5);
            CollectionAssert.AreEqual(new[] { "Ниже", "Корпус прямо" }, voice.Said);
        }

        [Test]
        public void NewCueDoesNotCutOffThePreviousOne()
        {
            var voice = new FakeVoice();
            var coach = new CoachVoice(voice, repeatAfterSeconds: 2.0, minGapSeconds: 1.2);
            // Вердикт повтора живёт один кадр позы; без паузы живая подсказка стойки
            // затирала бы его через 0.1 с — нативный TTS говорит через QUEUE_FLUSH.
            coach.Observe("Выше", ExerciseFormState.BadForm, 0.0);
            coach.Observe("Шире шаг", ExerciseFormState.BadForm, 0.1);
            coach.Observe("Шире шаг", ExerciseFormState.BadForm, 0.9);
            CollectionAssert.AreEqual(new[] { "Выше" }, voice.Said);
        }

        [Test]
        public void RepeatWindowIsMeasuredFromTheLastTimeThePhraseWasSaid()
        {
            var voice = new FakeVoice();
            var coach = new CoachVoice(voice, repeatAfterSeconds: 2.0);
            coach.Observe("Ниже", ExerciseFormState.BadForm, 0.0);
            coach.Observe("Ниже", ExerciseFormState.BadForm, 1.5);   // молчит
            coach.Observe("Ниже", ExerciseFormState.BadForm, 2.5);   // 2.5 с от t=0 — снова вслух
            CollectionAssert.AreEqual(new[] { "Ниже", "Ниже" }, voice.Said);
        }

        [Test]
        public void EmptyCueIsNeverSpoken()
        {
            var voice = new FakeVoice();
            var coach = new CoachVoice(voice);
            coach.Observe(string.Empty, ExerciseFormState.GoodForm, 0.0);
            coach.Observe(null, ExerciseFormState.BadForm, 1.0);
            CollectionAssert.IsEmpty(voice.Said);
        }

        [Test]
        public void NotVisibleIsNeverSpoken()
        {
            var voice = new FakeVoice();
            var coach = new CoachVoice(voice);
            for (double t = 0.0; t <= 10.0; t += 0.5)
                coach.Observe("В кадр", ExerciseFormState.NotVisible, t);
            CollectionAssert.IsEmpty(voice.Said);
        }

        [Test]
        public void NotVisibleDoesNotSuppressTheNextRealCue()
        {
            var voice = new FakeVoice();
            var coach = new CoachVoice(voice);
            coach.Observe("В кадр", ExerciseFormState.NotVisible, 0.0);
            coach.Observe("В кадр", ExerciseFormState.BadForm, 0.2);   // та же строка, но уже судимый кадр
            CollectionAssert.AreEqual(new[] { "В кадр" }, voice.Said);
        }

        [Test]
        public void ResetForgetsTheLastPhrase()
        {
            var voice = new FakeVoice();
            var coach = new CoachVoice(voice, repeatAfterSeconds: 2.0);
            coach.Observe("Ниже", ExerciseFormState.BadForm, 0.0);
            coach.Reset();
            coach.Observe("Ниже", ExerciseFormState.BadForm, 0.2);   // новый подход — фраза звучит заново
            CollectionAssert.AreEqual(new[] { "Ниже", "Ниже" }, voice.Said);
        }
    }
}
