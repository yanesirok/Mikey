using System;
using System.Collections.Generic;

namespace Mikey.UI.Combine
{
    /// <summary>
    /// Pure, frontend-only state holder for the Combine screen. Owns the current
    /// <see cref="CombineState"/> and the mock "selected items" shown in the ready
    /// state, and raises <see cref="Changed"/> when either is replaced.
    ///
    /// Kept free of UnityEngine/UI Toolkit types so it can be exercised in EditMode
    /// tests without a live panel (mirrors how <c>SafeAreaInsets</c> is split out
    /// from <c>SafeAreaController</c>). All data is local/mock — no backend,
    /// networking, camera, or pose input.
    /// </summary>
    public sealed class CombineViewModel
    {
        private static readonly IReadOnlyList<CombineItem> NoItems = Array.Empty<CombineItem>();

        /// <summary>Raised after <see cref="State"/> and/or <see cref="Items"/> change.</summary>
        public event Action Changed;

        /// <summary>The current display state. Starts at <see cref="CombineState.Loading"/>.</summary>
        public CombineState State { get; private set; } = CombineState.Loading;

        /// <summary>
        /// The selected items to render. Non-empty only in
        /// <see cref="CombineState.Ready"/>; empty in every other state.
        /// </summary>
        public IReadOnlyList<CombineItem> Items { get; private set; } = NoItems;

        /// <summary>
        /// Switches to <paramref name="state"/>, populating <see cref="Items"/> with
        /// the mock baseline in <see cref="CombineState.Ready"/> and clearing it
        /// otherwise. Always raises <see cref="Changed"/> so the bound view can
        /// re-render even on a same-state refresh.
        /// </summary>
        public void SetState(CombineState state)
        {
            State = state;
            Items = state == CombineState.Ready ? CreateMockItems() : NoItems;
            Changed?.Invoke();
        }

        /// <summary>
        /// Advances to the next state in a fixed dev cycle
        /// (Loading → Empty → Ready → Error → Loading). Drives the single
        /// "cycle" affordance on the development-only state switcher.
        /// </summary>
        public void CycleState()
        {
            SetState(NextState(State));
        }

        /// <summary>Pure next-state lookup for the dev cycle. Wraps Error → Loading.</summary>
        public static CombineState NextState(CombineState state)
        {
            switch (state)
            {
                case CombineState.Loading: return CombineState.Empty;
                case CombineState.Empty: return CombineState.Ready;
                case CombineState.Ready: return CombineState.Error;
                case CombineState.Error: return CombineState.Loading;
                default: return CombineState.Loading;
            }
        }

        /// <summary>
        /// The mock selected items — the four LVL0 "Combine" baseline measurements
        /// from the design reference. Local placeholder data only.
        /// </summary>
        public static IReadOnlyList<CombineItem> CreateMockItems()
        {
            return new[]
            {
                new CombineItem("Push-ups", "腕", "24", "REPS", "Power · upper"),
                new CombineItem("Sit-ups", "腹", "31", "REPS", "Core"),
                new CombineItem("Flexibility & Control", "柔", "7.5", "SCORE", "Control"),
                new CombineItem("Durability", "耐", "48", "SECONDS", "Endurance"),
            };
        }
    }
}
