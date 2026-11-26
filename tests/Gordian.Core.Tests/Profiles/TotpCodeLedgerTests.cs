// tests/Gordian.Core.Tests/Profiles/TotpCodeLedgerTests.cs
using System;
using System.Threading.Tasks;
using Gordian.Core.Profiles;
using Xunit;

namespace Gordian.Core.Tests.Profiles
{
    /// <summary>
    /// The two-factor code ledger: LandSandBoat refuses a TOTP step it already accepted for the account (result 2), so
    /// back-to-back logins must each send a newer step inside the server's one-step window.
    /// </summary>
    public class TotpCodeLedgerTests
    {
        private const string Secret = "JBSWY3DPEHPK3PXPJBSWY3DPEHPK3PXP";
        private static readonly string Account = TotpCodeLedger.AccountKey("play.example", 54231, "knot");

        // 10 seconds into a step, so the step boundary is 20 s away.
        private static readonly DateTimeOffset Now = TotpEngine.StepStart(59_000_000) + TimeSpan.FromSeconds(10);

        [Fact]
        public void TryTake_SendsTheCurrentStepThenTheNextThenWaits()
        {
            var ledger = new TotpCodeLedger();
            long step = TotpEngine.StepAt(Now);

            Assert.Equal(TotpEngine.GenerateCode(Secret, step), ledger.TryTake(Account, Secret, Now, out _));
            // A second login in the same 30 s: the next step's code, which the server's window accepts.
            Assert.Equal(TotpEngine.GenerateCode(Secret, step + 1), ledger.TryTake(Account, Secret, Now.AddSeconds(5), out _));
            // A third: nothing usable until the next step begins (20 s, then 10 s later; plus a second's margin).
            Assert.Null(ledger.TryTake(Account, Secret, Now.AddSeconds(10), out var wait));
            Assert.Equal(TimeSpan.FromSeconds(11), wait);

            // Once the next step began, the one after it is inside the window.
            Assert.Equal(TotpEngine.GenerateCode(Secret, step + 2), ledger.TryTake(Account, Secret, TotpEngine.StepStart(step + 1).AddSeconds(1), out _));
        }

        [Fact]
        public void TryTake_KeepsAccountsApartAndIgnoresCase()
        {
            var ledger = new TotpCodeLedger();
            long step = TotpEngine.StepAt(Now);
            ledger.TryTake(Account, Secret, Now, out _);

            Assert.Equal(TotpEngine.GenerateCode(Secret, step), ledger.TryTake(TotpCodeLedger.AccountKey("play.example", 54231, "other"), Secret, Now, out _));
            Assert.Equal(TotpEngine.GenerateCode(Secret, step), ledger.TryTake(TotpCodeLedger.AccountKey("play.example", 50000, "knot"), Secret, Now, out _));
            Assert.Equal(Account, TotpCodeLedger.AccountKey(" PLAY.example ", 54231, "KNOT"));
            Assert.Equal(TotpEngine.GenerateCode(Secret, step + 1), ledger.TryTake(TotpCodeLedger.AccountKey("Play.Example", 54231, "Knot"), Secret, Now, out _));
        }

        [Fact]
        public void TryTake_StartsAfreshOnceTheUsedStepsArePast()
        {
            var ledger = new TotpCodeLedger();
            ledger.TryTake(Account, Secret, Now, out _);
            var later = Now.AddMinutes(5);
            Assert.Equal(TotpEngine.GenerateCode(Secret, TotpEngine.StepAt(later)), ledger.TryTake(Account, Secret, later, out _));
        }

        [Fact]
        public void NoteSent_RecordsATypedCodeOfTheKey()
        {
            var ledger = new TotpCodeLedger();
            long step = TotpEngine.StepAt(Now);
            // The two-factor setup's verify sent the current code (typed from a phone app).
            ledger.NoteSent(Account, Secret, TotpEngine.GenerateCode(Secret, step), Now);
            Assert.Equal(TotpEngine.GenerateCode(Secret, step + 1), ledger.TryTake(Account, Secret, Now, out _));
        }

        [Fact]
        public void NoteSent_IgnoresACodeOutsideTheWindow()
        {
            var ledger = new TotpCodeLedger();
            long step = TotpEngine.StepAt(Now);
            ledger.NoteSent(Account, Secret, "000000" == TotpEngine.GenerateCode(Secret, step) ? "111111" : "000000", Now);
            ledger.NoteSent(Account, Secret, TotpEngine.GenerateCode(Secret, step - 5), Now);
            Assert.Equal(TotpEngine.GenerateCode(Secret, step), ledger.TryTake(Account, Secret, Now, out _));
        }

        [Fact]
        public async Task TakeAsync_ReturnsAtOnceWhileACodeIsFree()
        {
            var ledger = new TotpCodeLedger();
            bool waited = false;
            string code = await ledger.TakeAsync(Account, Secret, _ => waited = true, ct: TestContext.Current.CancellationToken);
            Assert.Equal(6, code.Length);
            Assert.False(waited);
        }

        [Fact]
        public void GenerateCurrentCode_IsTheCodeOfTheCurrentStep()
        {
            // RFC 6238 appendix B, SHA-1, T = 59 s: 94287082 (8 digits), 287082 in 6.
            Assert.Equal("287082", TotpEngine.GenerateCode("GEZDGNBVGY3TQOJQGEZDGNBVGY3TQOJQ", 59 / 30));
        }
    }
}
