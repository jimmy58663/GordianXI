// src/Gordian.Engine/Sessions/CharacterSession.Vanish.cs
using Gordian.Core.World;

namespace Gordian.Engine.Sessions
{
    public sealed partial class CharacterSession
    {
        /// <summary>The fade-out routine (ROM/0/0 <c>syst/acti/kesu</c>) as S2C 0x038 names it, its key's bytes little-endian.</summary>
        private const string VanishRoutine = "kesu";
        private const uint VanishRoutineId = 0x7573656B;

        /// <summary>
        /// The server sends the living character away (<see cref="LocalPlayerState.IsVanishing"/>: a survival guide's or home
        /// point's teleport to another zone): retail fades the character out (round 1 of the 2026-11-03 test), so it plays
        /// the fade an S2C 0x038 <c>kesu</c> would. LandSandBoat sends no scheduler for this, only the status. Retail's
        /// sound with it is not known yet.
        /// </summary>
        private void FadeOutIfVanishing()
        {
            if (!LocalPlayer.IsVanishing || !World.TryGetByServerId(LocalPlayer.ServerId, out var self) || self == null) return;
            Scheduler.PostActor(new ActorSchedulerRequest(World.CurrentZoneId, VanishRoutine, VanishRoutineId, self.ServerId, self.ServerId,
                self.TargetIndex, self.TargetIndex));
        }
    }
}
