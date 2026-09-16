// tests/Gordian.Core.Tests/Network/CombatLogFormatterTests.cs
using System.Collections.Generic;
using Gordian.Core.Network.Packets;
using Xunit;

namespace Gordian.Core.Tests.Network
{
    public sealed class CombatLogFormatterTests
    {
        private static string? MockEntityResolver(uint id) => id switch
        {
            1001 => "Cybin",
            2001 => "Wild Rabbit",
            3001 => "Goblin Fisher",
            _ => null
        };

        [Fact]
        public void FormatBattleMessage_DefeatsTarget_FormatsCorrectly()
        {
            var msg = new CombatMessageRecord
            {
                CasterId = 1001,
                TargetId = 2001,
                MessageId = 6,
                Param = 0,
                Value = 0
            };

            string text = CombatLogFormatter.FormatBattleMessage(msg, MockEntityResolver);
            Assert.Equal("Cybin defeats Wild Rabbit.", text);
        }

        [Fact]
        public void FormatBattleMessage_ExperiencePoints_FormatsCorrectly()
        {
            var msg = new CombatMessageRecord
            {
                CasterId = 1001,
                TargetId = 1001,
                MessageId = 8,
                Param = 120,
                Value = 0
            };

            string text = CombatLogFormatter.FormatBattleMessage(msg, MockEntityResolver);
            Assert.Equal("Cybin gains 120 experience points.", text);
        }

        [Fact]
        public void FormatBattleMessage_AttackHits_FormatsCorrectly()
        {
            var msg = new CombatMessageRecord
            {
                CasterId = 1001,
                TargetId = 2001,
                MessageId = 1,
                Param = 45,
                Value = 0
            };

            string text = CombatLogFormatter.FormatBattleMessage(msg, MockEntityResolver);
            Assert.Equal("Cybin hits Wild Rabbit for 45 points of damage.", text);
        }

        [Fact]
        public void FormatBattleMessage_CriticalHit_FormatsCorrectly()
        {
            var msg = new CombatMessageRecord
            {
                CasterId = 1001,
                TargetId = 2001,
                MessageId = 67,
                Param = 89,
                Value = 0
            };

            string text = CombatLogFormatter.FormatBattleMessage(msg, MockEntityResolver);
            Assert.Equal("Cybin scores a critical hit! Wild Rabbit takes 89 points of damage.", text);
        }

        [Fact]
        public void FormatBattleMessage_MagicDamage_FormatsCorrectly()
        {
            var msg = new CombatMessageRecord
            {
                CasterId = 1001,
                TargetId = 3001,
                MessageId = 2,
                Param = 144, // Fire
                Value = 65
            };

            string text = CombatLogFormatter.FormatBattleMessage(msg, MockEntityResolver);
            Assert.Equal("Cybin casts Fire. Goblin Fisher takes 65 points of damage.", text);
        }

        [Fact]
        public void FormatAction_BasicAttack_Hit_FormatsCorrectly()
        {
            var action = new CombatActionRecord
            {
                ActorId = 1001,
                Category = ActionCategory.BasicAttack,
                ActionId = 0,
                Targets = new List<CombatActionTargetRecord>
                {
                    new()
                    {
                        TargetId = 2001,
                        Results = new List<CombatActionResult>
                        {
                            new()
                            {
                                Resolution = ActionResolution.Hit,
                                Param = 32,
                                MessageId = 1
                            }
                        }
                    }
                }
            };

            var lines = CombatLogFormatter.FormatAction(action, MockEntityResolver);
            Assert.Single(lines);
            Assert.Equal("Cybin hits Wild Rabbit for 32 points of damage.", lines[0]);
        }

        [Fact]
        public void FormatAction_BasicAttack_WithProcAndReaction_FormatsAllLines()
        {
            var action = new CombatActionRecord
            {
                ActorId = 1001,
                Category = ActionCategory.BasicAttack,
                ActionId = 0,
                Targets = new List<CombatActionTargetRecord>
                {
                    new()
                    {
                        TargetId = 3001,
                        Results = new List<CombatActionResult>
                        {
                            new()
                            {
                                Resolution = ActionResolution.Hit,
                                Param = 50,
                                MessageId = 1,
                                HasProc = true,
                                ProcKind = ActionProcAddEffect.FireDamage,
                                ProcParam = 10,
                                HasReaction = true,
                                ReactionKind = ActionReactKind.BlazeSpikes,
                                ReactionParam = 4
                            }
                        }
                    }
                }
            };

            var lines = CombatLogFormatter.FormatAction(action, MockEntityResolver);
            Assert.Equal(3, lines.Count);
            Assert.Equal("Cybin hits Goblin Fisher for 50 points of damage.", lines[0]);
            Assert.Equal("Additional effect: Goblin Fisher takes 10 points of FireDamage damage.", lines[1]);
            Assert.Equal("Goblin Fisher's BlazeSpikes deals 4 damage to Cybin.", lines[2]);
        }

        [Fact]
        public void FormatAction_MagicFinish_Cure_FormatsHeal()
        {
            var action = new CombatActionRecord
            {
                ActorId = 1001,
                Category = ActionCategory.MagicFinish,
                ActionId = 1, // Cure
                Targets = new List<CombatActionTargetRecord>
                {
                    new()
                    {
                        TargetId = 1001,
                        Results = new List<CombatActionResult>
                        {
                            new()
                            {
                                Resolution = ActionResolution.Hit,
                                Param = 30,
                                MessageId = 7
                            }
                        }
                    }
                }
            };

            var lines = CombatLogFormatter.FormatAction(action, MockEntityResolver);
            Assert.Single(lines);
            Assert.Equal("Cybin casts Cure. Cybin recovers 30 HP.", lines[0]);
        }

        [Fact]
        public void FormatAction_SkillFinish_FastBlade_FormatsDamage()
        {
            var action = new CombatActionRecord
            {
                ActorId = 1001,
                Category = ActionCategory.SkillFinish,
                ActionId = 32, // Fast Blade
                Targets = new List<CombatActionTargetRecord>
                {
                    new()
                    {
                        TargetId = 2001,
                        Results = new List<CombatActionResult>
                        {
                            new()
                            {
                                Resolution = ActionResolution.Hit,
                                Param = 78,
                                MessageId = 185
                            }
                        }
                    }
                }
            };

            var lines = CombatLogFormatter.FormatAction(action, MockEntityResolver);
            Assert.Single(lines);
            Assert.Equal("Cybin uses Fast Blade. Wild Rabbit takes 78 points of damage.", lines[0]);
        }

        [Fact]
        public void FormatAction_SkillFinish_Miss_FormatsMiss()
        {
            var action = new CombatActionRecord
            {
                ActorId = 1001,
                Category = ActionCategory.SkillFinish,
                ActionId = 32, // Fast Blade
                Targets = new List<CombatActionTargetRecord>
                {
                    new()
                    {
                        TargetId = 2001,
                        Results = new List<CombatActionResult>
                        {
                            new()
                            {
                                Resolution = ActionResolution.Miss,
                                Param = 0,
                                MessageId = 188
                            }
                        }
                    }
                }
            };

            var lines = CombatLogFormatter.FormatAction(action, MockEntityResolver);
            Assert.Single(lines);
            Assert.Equal("Cybin uses Fast Blade, but misses Wild Rabbit.", lines[0]);
        }
    }
}
