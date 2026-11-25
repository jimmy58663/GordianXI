// tests/Gordian.Core.Tests/World/EventMotionStopTests.cs
using System.Diagnostics;
using Gordian.Core.Animation;
using Gordian.Core.World;
using Xunit;

namespace Gordian.Core.Tests.World
{
    /// <summary>
    /// #583: on retail Balasiel stayed in his idle pose from the neck down through Southern San d'Oria event 597. A
    /// still-standing NPC can carry MotStopFlag; the renderer froze its motion, so the event's gestures never started while
    /// the head (which follows the event look through the flag) turned. An event actor plays its event gestures through it.
    /// </summary>
    public sealed class EventMotionStopTests
    {
        private static ActionRequest Gesture(string routine) => new()
        {
            Motion = ActionMotion.EventMotion,
            Routine = routine,
            ReceivedTimestamp = Stopwatch.GetTimestamp(),
        };

        [Fact]
        public void MotionStoppedEntity_StaysFrozenOutsideAnEventAndWithoutAGesture()
        {
            var entity = new WorldEntity(0x010E6040, 64, EntityType.Npc) { IsMotionStopped = true };
            Assert.True(entity.IsMotionFrozen);

            entity.IsInEvent = true;
            Assert.True(entity.IsMotionFrozen);            // in the event, idle between gestures stays frozen

            entity.IsInEvent = false;
            entity.Animation.EnqueueAction(Gesture("tlk0"));
            Assert.True(entity.IsMotionFrozen);            // a gesture outside an event does not lift the flag
        }

        [Fact]
        public void EventActor_PlaysItsEventGestureThroughMotStopFlag()
        {
            var entity = new WorldEntity(0x010E6040, 64, EntityType.Npc) { IsMotionStopped = true, IsInEvent = true };
            entity.Animation.EnqueueAction(Gesture("tlk0"));

            Assert.True(entity.Animation.HasEventGesture);
            Assert.False(entity.IsMotionFrozen);
        }

        [Fact]
        public void EntityWithoutTheFlag_IsNeverFrozen()
        {
            var entity = new WorldEntity(1, 1, EntityType.Npc);
            Assert.False(entity.IsMotionFrozen);
        }
    }
}
