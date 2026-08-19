using System;
using System.Collections.Generic;

namespace Mikey.Pose
{
    /// <summary>One selectable exercise: its id, display name, framing hint, and a factory
    /// that builds a fresh analyzer for the requested <see cref="ScoringProfile"/>.</summary>
    public sealed class ExerciseDescriptor
    {
        public string Id { get; }
        public string DisplayName { get; }

        /// <summary>Framing/setup hint shown above the live HUD ("" when none needed).</summary>
        public string Hint { get; }

        public Func<ScoringProfile, IExerciseAnalyzer> Create { get; }

        public ExerciseDescriptor(string id, string displayName, Func<ScoringProfile, IExerciseAnalyzer> create,
            string hint = "")
        {
            Id = id;
            DisplayName = displayName;
            Create = create;
            Hint = hint ?? string.Empty;
        }
    }

    /// <summary>
    /// Registry of exercises available for selection. Selection UI (dev sandbox, and later the
    /// in-game picker) reads <see cref="All"/>; adding an exercise is one <see cref="Register"/>
    /// call — no changes to the controller, HUD, or sandbox.
    ///
    /// Level 0 (assessment) entries are scored leniently; level 1 (teaching) entries are the
    /// same registry with strict scoring — the sandbox's teaching toggle picks the profile, so
    /// one entry serves both levels rather than the catalog carrying two copies of everything.
    /// </summary>
    public static class ExerciseCatalog
    {
        private const string SideOn = "Боком к камере, всё тело в кадр";

        private static readonly List<ExerciseDescriptor> Items = new List<ExerciseDescriptor>
        {
            new ExerciseDescriptor("pushup", "Push-ups", p => new PushUpAnalyzer(profile: p)),
            new ExerciseDescriptor("squat", "Squats", p => new SquatAnalyzer(profile: p)),
            new ExerciseDescriptor("wallsit", "Wall-sit (сек)", p => new WallSitAnalyzer(profile: p),
                "Спиной к стене, бёдра параллельно полу"),
            new ExerciseDescriptor("yokogeri-gedan", "Yoko geri gedan", p => new YokoGeriAnalyzer(KickZone.Gedan, profile: p),
                "Лицом к камере, можно держаться за стену"),
            new ExerciseDescriptor("yokogeri-chudan", "Yoko geri chudan", p => new YokoGeriAnalyzer(KickZone.Chudan, profile: p),
                "Лицом к камере, можно держаться за стену"),
            new ExerciseDescriptor("yokogeri-jodan", "Yoko geri jodan", p => new YokoGeriAnalyzer(KickZone.Jodan, profile: p),
                "Лицом к камере, можно держаться за стену"),

            // Уровень 1. Профиль игнорируется намеренно: это техники обучения, судить их
            // мягко нечем — «почти правильная стойка» стойкой не является.
            new ExerciseDescriptor("stance-fudo", "Fudo dachi", _ => new StanceAnalyzer(StanceKind.Fudo), SideOn),
            new ExerciseDescriptor("stance-zenkutsu", "Zenkutsu dachi", _ => new StanceAnalyzer(StanceKind.Zenkutsu), SideOn),
            new ExerciseDescriptor("kizamizuki-jodan", "Kizami zuki jodan", _ => new KizamiZukiAnalyzer(), SideOn),
            new ExerciseDescriptor("maegeri-chudan-stance", "Mae geri chudan (из стойки)",
                _ => new MaeGeriAnalyzer(KickZone.Chudan, profile: ScoringProfile.Strict), SideOn),
            new ExerciseDescriptor("ghoststep-forward", "Ghost step вперёд",
                _ => new GhostStepAnalyzer(forward: true), SideOn),
            new ExerciseDescriptor("ghoststep-back", "Ghost step назад",
                _ => new GhostStepAnalyzer(forward: false), SideOn),
        };

        public static IReadOnlyList<ExerciseDescriptor> All => Items;

        /// <summary>Registers an exercise (idempotent by id). Handy for tests or plugins.</summary>
        public static void Register(ExerciseDescriptor descriptor)
        {
            if (descriptor == null)
                throw new ArgumentNullException(nameof(descriptor));
            Items.RemoveAll(d => d.Id == descriptor.Id);
            Items.Add(descriptor);
        }

        /// <summary>Creates a fresh analyzer for the given id, or null if unknown.</summary>
        public static IExerciseAnalyzer Create(string id, ScoringProfile profile = ScoringProfile.Lenient)
        {
            foreach (ExerciseDescriptor d in Items)
                if (d.Id == id)
                    return d.Create(profile);
            return null;
        }
    }
}
