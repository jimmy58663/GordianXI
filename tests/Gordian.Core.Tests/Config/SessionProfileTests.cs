// tests/Gordian.Core.Tests/Config/SessionProfileTests.cs
using System;
using System.Linq;
using System.Reflection;
using Gordian.Core.Config;
using Gordian.Core.Network.Packets;
using Xunit;

namespace Gordian.Core.Tests.Config
{
    /// <summary>The server's feature restrictions are write-once per bit (AGENTS.md kill-switch policy).</summary>
    public sealed class SessionProfileTests
    {
        [Fact]
        public void FeatureRestrictionsHaveNoSetter()
        {
            var property = typeof(SessionProfile).GetProperty(nameof(SessionProfile.FeatureRestrictions))!;
            Assert.Null(property.SetMethod);
        }

        [Fact]
        public void TheOnlyWriterTakesADecodedServerPacketAndIsInternal()
        {
            // Any method that takes a flag value or the packet and returns nothing is a writer; IsRestricted is a query.
            var writers = typeof(SessionProfile)
                .GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly)
                .Where(m => m.ReturnType == typeof(void) && m.GetParameters().Any(p => p.ParameterType.GetElementType() == typeof(S2C_0x0EE_FeatureRestrictions)
                    || p.ParameterType == typeof(FeatureRestrictions)))
                .ToList();
            var writer = Assert.Single(writers);
            Assert.True(writer.IsAssembly);
            Assert.Equal(typeof(S2C_0x0EE_FeatureRestrictions).MakeByRefType(), writer.GetParameters().Single().ParameterType);
        }

        [Fact]
        public void RestrictionsAddUpAndNeverClear()
        {
            var profile = new SessionProfile();
            Assert.Equal(FeatureRestrictions.None, profile.FeatureRestrictions);

            profile.Restrict(FeatureRestrictions.Combat);
            profile.Restrict(FeatureRestrictions.None);
            Assert.Equal(FeatureRestrictions.Combat, profile.FeatureRestrictions);

            profile.Restrict(FeatureRestrictions.Movement);
            Assert.Equal(FeatureRestrictions.Combat | FeatureRestrictions.Movement, profile.FeatureRestrictions);
            Assert.True(profile.IsRestricted(FeatureRestrictions.Movement));
        }

        [Fact]
        public void AShortPacketChangesNothing()
        {
            var profile = new SessionProfile().Restrict(FeatureRestrictions.Addons);
            profile.ApplyServerRestrictions(new S2C_0x0EE_FeatureRestrictions(new byte[4]));
            Assert.Equal(FeatureRestrictions.Addons, profile.FeatureRestrictions);
        }
    }
}
