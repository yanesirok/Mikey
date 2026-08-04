using System;
using System.Collections.Generic;

namespace Mikey.Pose
{
    /// <summary>One selectable exercise: its id, display name, and a factory for a fresh analyzer.</summary>
    public sealed class ExerciseDescriptor
    {
        public string Id { get; }
        public string DisplayName { get; }
        public Func<IExerciseAnalyzer> Create { get; }

        public ExerciseDescriptor(string id, string displayName, Func<IExerciseAnalyzer> create)
        {
            Id = id;
            DisplayName = displayName;
            Create = create;
        }
    }

    /// <summary>
    /// Registry of exercises available for selection. Selection UI (dev sandbox, and later the
    /// in-game picker) reads <see cref="All"/>; adding an exercise is one <see cref="Register"/>
    /// call — no changes to the controller, HUD, or sandbox.
    /// </summary>
    public static class ExerciseCatalog
    {
        private static readonly List<ExerciseDescriptor> Items = new List<ExerciseDescriptor>
        {
            new ExerciseDescriptor("pushup", "Push-ups", () => new PushUpAnalyzer()),
            new ExerciseDescriptor("squat", "Squats", () => new SquatAnalyzer()),
            new ExerciseDescriptor("wallsit", "Wall-sit (сек)", () => new WallSitAnalyzer()),
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
        public static IExerciseAnalyzer Create(string id)
        {
            foreach (ExerciseDescriptor d in Items)
                if (d.Id == id)
                    return d.Create();
            return null;
        }
    }
}
