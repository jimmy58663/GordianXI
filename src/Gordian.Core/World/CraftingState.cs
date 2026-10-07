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

        internal void SetOutcome(SynthesisOutcome outcome)
        {
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
