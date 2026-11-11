// tests/Gordian.Core.Tests/Network/Retail/RetailPmlTests.cs
using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Gordian.Core.Network.Retail;
using Xunit;

namespace Gordian.Core.Tests.Network.Retail
{
    /// <summary>The PML stage (spec v2.1.19 sections 4.4 and 5.7) and the retail stage order's install lookups.</summary>
    public class RetailPmlTests
    {
        private static readonly CancellationToken Ct = new CancellationTokenSource(TimeSpan.FromSeconds(20)).Token;

        [Fact]
        public void WantHello_IsTheTimeTwiceInPolBase64()
        {
            // Spec section 4.4: 1ULayDsBNfi = E6 1B F3 6A E6 1B F3 6A, t = 0x6AF31BE6.
            Assert.Equal("1ULayDsBNfi", PolPmlClient.WantHello(0x6AF31BE6));
        }

        [Fact]
        public void Request_HasRetailsHeadersInOrder()
        {
            string first = PolPmlClient.BuildRequest(PolPmlClient.MainPage, "1.18.15f", "1ULayDsBNfi", PolPmlClient.FirstIfModifiedSince, null);
            Assert.Equal(
                "GET /pml/main/index.pml HTTP/1.1\r\n" +
                "Host: wh000.pol.com\r\n" +
                "User-Agent: PlayOnline-PML-Viewer/1.00 [en] (Windows XP)\r\n" +
                "X-POL-VIEWER-VERSION: Ver.1.18.15f\r\n" +
                "Accept: text/x-playonline-pml, image/x-playonline-ang, image/png, image/jpeg, */*\r\n" +
                "Accept-Language: en-US\r\n" +
                "Connection: Keep-Alive\r\n" +
                "If-Modified-Since: Thu, 16 Sep 2014 02:19:19 GMT\r\n" +
                "X-PlayOnline-Want-Hello: 1ULayDsBNfi\r\n" +
                "\r\n", first);

            string auth = PolPmlClient.Authorization("dXNlcg", "POL", "abc", PolPmlClient.FfxiPage, "0123");
            Assert.Equal("Digest userName=\"dXNlcg\", realm=\"POL\", nonce=\"abc\", uri=\"/pml/game/ff11/index.pml?SC=0&PF=WIN\", response=\"0123\", algorithm=\"x-MD5-pol\"", auth);
            string later = PolPmlClient.BuildRequest(PolPmlClient.FfxiPage, "1.18.15f", "1ULayDsBNfi", null, auth);
            Assert.DoesNotContain("If-Modified-Since", later);
            Assert.EndsWith($"X-PlayOnline-Want-Hello: 1ULayDsBNfi\r\nAuthorization: {auth}\r\n\r\n", later);
        }

        [Theory]
        [InlineData("<define name=\"$playbutton_enable\" value=\"0\"><define name=\"$status\" value=\"The game is unavailable for play during maintenance.\">", false, "The game is unavailable for play during maintenance.")]
        [InlineData("$playbutton_enable = \"1\"\n$status = \"\"", true, "")]
        [InlineData("<pml>no flags</pml>", null, null)]
        public void LaunchStatus_ReadsThePlayFlagAndStatus(string page, bool? play, string? status)
        {
            var parsed = PolPmlClient.ParseLaunchStatus(page);
            Assert.Equal(play, parsed.PlayEnabled);
            Assert.Equal(status, parsed.Status);
        }

        [Fact]
        public void ViewerFolder_DefaultsToTheSiblingOfTheFfxiFolder()
        {
            string squareEnix = Path.Combine(Path.GetTempPath(), "PlayOnline", "SquareEnix");
            var credentials = new RetailCredentials("ABCD1234", "pw", "se", "sepw", "", Path.Combine(squareEnix, "FINAL FANTASY XI"));
            Assert.Equal(Path.Combine(squareEnix, "PlayOnlineViewer"), RetailLoginClient.ResolveViewerDirectory(credentials));
            Assert.Equal("X", RetailLoginClient.ResolveViewerDirectory(credentials with { ViewerDirectory = "X" }));
        }

        [Fact]
        public void ViewerVersion_IsVersionDatTrimmed()
        {
            string dir = Directory.CreateTempSubdirectory().FullName;
            try
            {
                Assert.Null(PolPmlClient.ReadViewerVersion(dir));
                File.WriteAllText(Path.Combine(dir, PolPmlClient.VersionFileName), "1.18.15f");
                Assert.Equal("1.18.15f", PolPmlClient.ReadViewerVersion(dir));
            }
            finally
            {
                Directory.Delete(dir, true);
            }
        }

        [Fact]
        public async Task Visit_AnswersTheChallengeAndReadsTheFfxiPage()
        {
            byte[] routing = [0x50, 0xC3, 10, 2, 0, 192]; // 192.0.2.10:50000
            byte[] key = Convert.FromHexString("311DD37445716B05");
            using var listener = new TcpListener(IPAddress.Loopback, 0);
            listener.Start();
            int port = ((IPEndPoint)listener.LocalEndpoint).Port;
            var requests = new List<string>();

            var server = Task.Run(async () =>
            {
                using var c = await listener.AcceptTcpClientAsync(Ct);
                var s = c.GetStream();
                async Task<string> ReadRequest()
                {
                    var sb = new StringBuilder();
                    var one = new byte[1];
                    while (!sb.ToString().EndsWith("\r\n\r\n", StringComparison.Ordinal))
                    {
                        if (await s.ReadAsync(one, Ct) == 0) throw new EndOfStreamException();
                        sb.Append((char)one[0]);
                    }
                    return sb.ToString();
                }
                async Task Reply(string text) => await s.WriteAsync(Encoding.ASCII.GetBytes(text), Ct);

                requests.Add(await ReadRequest());
                await Reply("HTTP/1.1 401 Unauthorized\r\nWWW-Authenticate: Digest realm=\"POL\", nonce=\"n0nce\", algorithm=\"x-MD5-pol\", domain=\"/pml/\", qop=\"auth\"\r\nContent-Length: 6\r\n\r\ndenied");
                requests.Add(await ReadRequest());
                await Reply("HTTP/1.1 200 OK\r\nContent-Length: 4\r\n\r\nmain");
                requests.Add(await ReadRequest());
                await Reply("HTTP/1.1 200 OK\r\nTransfer-Encoding: chunked\r\n\r\n10\r\n$playbutton_enab\r\n6\r\nle=\"1\"\r\n0\r\n\r\n");
            });

            var status = await PolPmlClient.VisitAsync(routing, key, 0x6AF31BE6, "1.18.15f", Ct, "127.0.0.1", port);
            await server;

            Assert.True(status.PlayEnabled);
            Assert.Equal(3, requests.Count);
            Assert.Contains("If-Modified-Since:", requests[0]);
            Assert.DoesNotContain("Authorization:", requests[0]);
            string user = PolHashes.PmlUserName(routing);
            Assert.Contains($"response=\"{PolHashes.PmlDigestResponse(user, "POL", key, PolPmlClient.MainPage, "n0nce")}\"", requests[1]);
            Assert.StartsWith("GET /pml/game/ff11/index.pml?SC=0&PF=WIN HTTP/1.1\r\n", requests[2]);
            Assert.Contains($"response=\"{PolHashes.PmlDigestResponse(user, "POL", key, PolPmlClient.FfxiPage, "n0nce")}\"", requests[2]);
            Assert.All(requests, r => Assert.Contains("X-PlayOnline-Want-Hello: 1ULayDsBNfi\r\n", r));
        }
    }
}
