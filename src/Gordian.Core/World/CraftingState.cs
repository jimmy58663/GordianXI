// src/Gordian.Core/World/CraftingState.cs
using System;
using Gordian.Core.Network.Packets;

namespace Gordian.Core.World
{
    /// <summary>
    /// What the server has told the client about crafting: the character's last synthesis result (S2C 0x06F),
    /// the last result seen from another character (0x070), and the last recipe or recipe list a guild NPC sent (0x031).
    /// A passive data holder; UI and the message log subscribe to the events.
    /// </summary>
    public sealed class CraftingState
    {
        private readonly object _gate = new();
        private SynthesisOutcome? _lastOutcome;
        private OtherSynthesisOutcome? _lastOtherOutcome;
        private RecipeDetail? _lastRecipe;
        private RecipeListPage? _lastRecipeList;

        /// <summary>Raised when the character's own synthesis ends (S2C 0x06F).</summary>
        public event Action<SynthesisOutcome>? SynthesisCompleted;

        /// <summary>Raised when a nearby character's synthesis ends (S2C 0x070).</summary>
        public event Action<OtherSynthesisOutcome>? OtherSynthesisCompleted;

        /// <summary>Raised when a guild NPC sends one recipe (S2C 0x031 kind 1 or 3).</summary>
        public event Action<RecipeDetail>? RecipeReceived;

        /// <summary>Raised when a guild NPC sends a page of the recipe list (S2C 0x031 kind 2).</summary>
        public event Action<RecipeListPage>? RecipeListReceived;

        public SynthesisOutcome? LastOutcome { get { lock (_gate) return _lastOutcome; } }
        public OtherSynthesisOutcome? LastOtherOutcome { get { lock (_gate) return _lastOtherOutcome; } }
        public RecipeDetail? LastRecipe { get { lock (_gate) return _lastRecipe; } }
        public RecipeListPage? LastRecipeList { get { lock (_gate) return _lastRecipeList; } }

        // The synthesis lock: retail refuses commands while the character synthesizes ("You cannot use that command
        // during synthesis."). It starts when C2S 0x096 goes out, holds a short grace for the server's S2C 0x030 to confirm
        // it, and then lasts until S2C 0x06F ends the synthesis (or a safety timeout, should that packet never come).
        private const double UnconfirmedSeconds = 4, ConfirmedSeconds = 60;
        private long _lockStart;
        private bool _lockConfirmed;

        /// <summary>Whether a synthesis is running: sent and not yet answered.</summary>
        public bool IsSynthesizing
        {
            get
            {
                lock (_gate)
                {
                    if (_lockStart == 0) return false;
                    double elapsed = (double)(System.Diagnostics.Stopwatch.GetTimestamp() - _lockStart) / System.Diagnostics.Stopwatch.Frequency;
                    if (elapsed < (_lockConfirmed ? ConfirmedSeconds : UnconfirmedSeconds)) return true;
                    _lockStart = 0;
                    return false;
                }
            }
        }

        /// <summary>C2S 0x096 was sent: the synthesis lock starts.</summary>
        public void BeginSynthesis()
        {
            lock (_gate)
            {
                _lockStart = System.Diagnostics.Stopwatch.GetTimestamp();
                _lockConfirmed = false;
            }
        }

        /// <summary>The server started the character's synthesis animation (S2C 0x030): the lock holds until the result.</summary>
        public void ConfirmSynthesis()
        {
            lock (_gate)
            {
                if (_lockStart == 0) _lockStart = System.Diagnostics.Stopwatch.GetTimestamp();
                _lockConfirmed = true;
            }
        }

        /// <summary>Drops the synthesis lock (the result came, or a zone change).</summary>
        public void EndSynthesis()
        {
            lock (_gate)
            {
                _lockStart = 0;
                _lockConfirmed = false;
            }
        }

        internal void SetOutcome(SynthesisOutcome outcome)
        {
            EndSynthesis();
            lock (_gate) _lastOutcome = outcome;
            SynthesisCompleted?.Invoke(outcome);
        }

        internal void SetOtherOutcome(OtherSynthesisOutcome outcome)
        {
            lock (_gate) _lastOtherOutcome = outcome;
            OtherSynthesisCompleted?.Invoke(outcome);
        }

        internal void SetRecipe(RecipeDetail recipe)
        {
            lock (_gate) _lastRecipe = recipe;
            RecipeReceived?.Invoke(recipe);
        }

        internal void SetRecipeList(RecipeListPage page)
        {
            lock (_gate) _lastRecipeList = page;
            RecipeListReceived?.Invoke(page);
        }
    }
}
