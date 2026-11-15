// tests/Gordian.Core.Tests/Resources/EntityModelEvictionRealDataTests.cs
using System;
using System.Collections.Generic;
using System.IO;
using Gordian.Core.Resources;
using Gordian.Core.Resources.Models;
using Gordian.Core.World;
using Xunit;

namespace Gordian.Core.Tests.Resources
{
    /// <summary>
    /// The entity model cache drops models no entity uses (#572): on retail every passing player's gear set added one for the
    /// life of the session. Skipped without the game install.
    /// </summary>
    public class EntityModelEvictionRealDataTests
    {
        private const string GameDirectory = @"G:\Program Files (x86)\PlayOnline\SquareEnix\FINAL FANTASY XI";

        /// <summary>A Hume Male (race 1, face 1) in the given body and legs.</summary>
        private static WorldEntity Player(uint serverId, ushort body, ushort legs)
        {
            var entity = new WorldEntity(serverId, (ushort)serverId, EntityType.Player);
            entity.Appearance.CopyFrom([0x0101, 0, body, 8, legs, 8, 0, 0, 0]);
            return entity;
        }

        [Fact]
        public void IdleModels_AreDropped_WhileModelsOfLiveEntitiesStay()
        {
            if (!Directory.Exists(GameDirectory)) return;
            var rm = new ResourceManager(GameDirectory);
            rm.InitializeFileTable();

            var stays = Player(1, 8, 8);
            var leaves = Player(2, 9, 9);
            Assert.True(rm.TryLoadEntityModel(stays, out var kept));
            Assert.True(rm.TryLoadEntityModel(leaves, out var dropped));
            Assert.NotSame(kept, dropped);
            Assert.Equal(2, rm.CachedEntityModelCount);

            // Idle time 0: everything not worn by a live entity goes, at once.
            var live = new List<EntityModel>();
            Assert.Equal(1, rm.EvictIdleEntityModels([stays], idleSeconds: 0, live));
            Assert.Equal(1, rm.CachedEntityModelCount);
            Assert.Same(kept, Assert.Single(live));

            Assert.True(rm.TryLoadEntityModel(stays, out var again));
            Assert.Same(kept, again);
            Assert.True(rm.TryLoadEntityModel(leaves, out var rebuilt));
            Assert.NotSame(dropped, rebuilt);
        }

        [Fact]
        public void RecentlyUsedModels_StayWithinTheIdleTime()
        {
            if (!Directory.Exists(GameDirectory)) return;
            var rm = new ResourceManager(GameDirectory);
            rm.InitializeFileTable();

            Assert.True(rm.TryLoadEntityModel(Player(1, 8, 8), out _));
            Assert.Equal(0, rm.EvictIdleEntityModels(Array.Empty<WorldEntity>()));
            Assert.Equal(1, rm.CachedEntityModelCount);
        }
    }
}
