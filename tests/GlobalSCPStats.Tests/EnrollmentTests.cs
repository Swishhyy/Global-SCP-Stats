using System;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using GlobalSCPStats.Core.Transport;

namespace GlobalSCPStats.Tests;

internal static partial class Program
{
    private static async Task TestEnrollment()
    {
        await RunAsync("enrollment reuses its pending token after a lost response and stores credentials encrypted", async () =>
        {
            using var temp = new TestDirectory();
            byte[] key = NewKey();
            string setupCode = "enroll_" + new string('A', 43);
            const string serverId = "server-5232fb73-d366-489b-a9a1-0a4afafb298a";
            string? pendingClaim = null;
            using var dropped = new HttpClient(new FakeHandler(async request =>
            {
                Equal("https://api.globalscpstats.com/v1/enrollment/redeem", request.RequestUri!.AbsoluteUri);
                True(request.Headers.Authorization == null);
                True(File.Exists(Path.Combine(temp.Path, "enrollment.state")));
                pendingClaim = await request.Content!.ReadAsStringAsync();
                throw new HttpRequestException("simulated lost registration response");
            }));
            try
            {
                await new ConnectorEnrollment(temp.Path, key).GetCredentialsAsync(setupCode, dropped, CancellationToken.None);
                throw new InvalidOperationException("Expected the first request to fail.");
            }
            catch (EnrollmentException) { }

            using var accepted = new HttpClient(new FakeHandler(async request =>
            {
                Equal(pendingClaim, await request.Content!.ReadAsStringAsync());
                return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent($"{{\"server_id\":\"{serverId}\"}}") };
            }));
            UploadOptions result = await new ConnectorEnrollment(temp.Path, key).GetCredentialsAsync(setupCode, accepted, CancellationToken.None);
            Equal(serverId, result.ServerId);
            using var unexpected = new HttpClient(new FakeHandler(_ => throw new InvalidOperationException("No request is expected after restart.")));
            UploadOptions saved = await new ConnectorEnrollment(temp.Path, key).GetCredentialsAsync(null, unexpected, CancellationToken.None);
            Equal(result.ApiToken, saved.ApiToken);
            using var sha = SHA256.Create();
            string tokenHash = Convert.ToBase64String(sha.ComputeHash(Encoding.UTF8.GetBytes(saved.ApiToken)))
                .TrimEnd('=').Replace('+', '-').Replace('/', '_');
            True(pendingClaim!.Contains(tokenHash));
            True(!pendingClaim.Contains(saved.ApiToken));
            string envelope = Encoding.UTF8.GetString(File.ReadAllBytes(Path.Combine(temp.Path, "enrollment.state")));
            True(!envelope.Contains(saved.ApiToken));
            True(!envelope.Contains(setupCode));
        });
    }
}
