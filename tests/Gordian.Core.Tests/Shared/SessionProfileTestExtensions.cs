// tests/Gordian.Core.Tests/Shared/SessionProfileTestExtensions.cs
using System;
using System.Buffers.Binary;
using Gordian.Core.Config;
using Gordian.Core.Network.Packets;

namespace Gordian.Tests.Shared
{
    /// <summary>
    /// Puts a session under server restrictions the way the server does: through a decoded S2C 0x0EE, which is the only way
    /// to write <see cref="SessionProfile.FeatureRestrictions"/>. Compiled into both test projects.
    /// </summary>
    internal static class SessionProfileTestExtensions
    {
        /// <summary>Adds <paramref name="restrictions"/> as if the server had sent them; returns the profile.</summary>
        public static SessionProfile Restrict(this SessionProfile profile, FeatureRestrictions restrictions)
        {
            Span<byte> payload = stackalloc byte[8];
            BinaryPrimitives.WriteUInt64LittleEndian(payload, (ulong)restrictions);
            profile.ApplyServerRestrictions(new S2C_0x0EE_FeatureRestrictions(payload));
            return profile;
        }

        /// <summary>A new profile under <paramref name="restrictions"/>.</summary>
        public static SessionProfile Restricted(FeatureRestrictions restrictions) => new SessionProfile().Restrict(restrictions);
    }
}
