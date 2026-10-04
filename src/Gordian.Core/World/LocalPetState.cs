// src/Gordian.Core/World/LocalPetState.cs
using System;

namespace Gordian.Core.World
{
    /// <summary>A snapshot of the local player's pet (see <see cref="LocalPetState"/>).</summary>
    public sealed record LocalPet(
        uint ServerId,
        ushort TargetIndex,
        ushort OwnerIndex,
        byte Hpp,
        byte Mpp,
        uint Tp,
        uint TargetServerId,
        string Name);

    /// <summary>
    /// The local player's personal pet (a Beastmaster's charmed monster or jug pet, a Summoner's avatar, a Dragoon's
    /// wyvern, a Puppetmaster's automaton), kept up to date by S2C 0x068 mode 4 (see <see cref="Network.Packets.EntitySyncPacket"/>).
    /// Read by the render and UI threads while the network thread writes, so every access takes the lock.
    /// Trusts and fellows are not pets in this sense.
    /// </summary>
    public sealed class LocalPetState
    {
        private readonly object _sync = new();
        private LocalPet? _pet;

        /// <summary>Raised after the pet appeared, changed or went away, on the network thread.</summary>
        public event Action? Changed;

        /// <summary>The pet, or null when the player has none.</summary>
        public LocalPet? Current
        {
            get { lock (_sync) return _pet; }
        }

        public bool HasPet => Current != null;

        /// <summary>
        /// Replaces the pet with <paramref name="pet"/>, or removes it when null. Raises <see cref="Changed"/> when the
        /// contents differ from the last one.
        /// </summary>
        public void Set(LocalPet? pet)
        {
            lock (_sync)
            {
                if (Equals(_pet, pet)) return;
                _pet = pet;
            }
            Changed?.Invoke();
        }

        /// <summary>Forgets the pet (a zone change or logout).</summary>
        public void Clear() => Set(null);
    }
}
