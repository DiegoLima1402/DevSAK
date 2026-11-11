using System.Net;
using System.Security.Cryptography;
using System.Text.Json;
using DevSAK.Models;
using DevSAK.Services;

var payload = "verified installer fixture"u8.ToArray();
var digest = Convert.ToHexString(SHA256.HashData(payload));
var current = new Version(1, 3, 3, 0);
var passed = 0;

JsonElement Release(string tag = "v1.3.4", bool prerelease = false, bool draft = false,
    string? sha = null, string? url = null, string? name = null)
{
    var file = name ?? $"DevSAK_Installer_v{tag.TrimStart('v')}.exe";
    return JsonSerializer.SerializeToElement(new
    {
        tag_name = tag, draft, prerelease, body = "Release notes",
        assets = new[] { new { name = file, size = payload.Length, digest = sha ?? "sha256:" + digest,
            browser_download_url = url ?? $"https://github.com/{GitHubUpdateService.Repository}/releases/download/{tag}/{file}" } }
    });
}

void Assert(bool condition, string message)
{
    if (!condition) throw new Exception(message);
}

void Test(string name, Action body)
{
    body(); passed++; Console.WriteLine($"PASS {name}");
}

void Reject(Action body)
{
    try { body(); } catch (InvalidDataException) { return; }
    throw new Exception("Expected invalid Release to be rejected");
}

Test("same version with assembly revision is not an update", () => Assert(GitHubUpdateService.ParseRelease(Release("v1.3.3"), current) == null, "same version"));
Test("older release cannot downgrade", () => Assert(GitHubUpdateService.ParseRelease(Release("v1.3.2"), current) == null, "downgrade"));
Test("numeric version comparison", () => Assert(GitHubUpdateService.ParseRelease(Release("v1.3.10"), current)?.Version == new Version(1, 3, 10), "numeric version"));
Test("tag without v is accepted", () => Assert(GitHubUpdateService.ParseRelease(Release("1.3.4"), current) != null, "plain tag"));
Test("draft and prerelease ignored", () =>
{
    Assert(GitHubUpdateService.ParseRelease(Release(draft: true), current) == null, "draft");
    Assert(GitHubUpdateService.ParseRelease(Release(prerelease: true), current) == null, "prerelease");
    Assert(GitHubUpdateService.ParseRelease(Release("v1.3.4-beta"), current) == null, "beta tag");
});
Test("external, insecure and wrong repository URLs rejected", () =>
{
    Reject(() => GitHubUpdateService.ParseRelease(Release(url: "https://example.com/setup.exe"), current));
    Reject(() => GitHubUpdateService.ParseRelease(Release(url: "http://github.com/DiegoLima1402/DevSAK/releases/download/v1.3.4/DevSAK_Installer_v1.3.4.exe"), current));
    Reject(() => GitHubUpdateService.ParseRelease(Release(url: "https://github.com/other/DevSAK/releases/download/v1.3.4/DevSAK_Installer_v1.3.4.exe"), current));
});
Test("missing installer or malformed digest rejected", () =>
{
    Reject(() => GitHubUpdateService.ParseRelease(Release(name: "source.zip"), current));
    Reject(() => GitHubUpdateService.ParseRelease(Release(sha: ""), current));
    Reject(() => GitHubUpdateService.ParseRelease(Release(sha: "sha256:" + new string('z', 64)), current));
});
Test("activity survives navigation and concurrent operations", () =>
{
    var firstPage = new object(); var secondPage = new object();
    OperationActivity.SetBusy(firstPage, true);
    OperationActivity.SetBusy(secondPage, true);
    OperationActivity.SetBusy(firstPage, false);
    Assert(OperationActivity.IsBusy, "second operation still running");
    OperationActivity.SetBusy(secondPage, false);
    Assert(!OperationActivity.IsBusy, "all operations finished");
});

var root = Path.Combine(Path.GetTempPath(), "DevSAK-update-tests-" + Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(root);
try
{
    foreach (var status in new[] { HttpStatusCode.NotFound, HttpStatusCode.Forbidden, HttpStatusCode.TooManyRequests })
    {
        using var http = new HttpClient(new StubHandler(() => new HttpResponseMessage(status)));
        var service = new GitHubUpdateService(http, root);
        if (status == HttpStatusCode.NotFound) Assert(await service.CheckAsync(current, default) == null, "no releases");
        else
        {
            var rejected = false;
            try { await service.CheckAsync(current, default); } catch (HttpRequestException) { rejected = true; }
            Assert(rejected, "rate limit");
        }
        passed++; Console.WriteLine($"PASS HTTP {status}");
    }

    var update = GitHubUpdateService.ParseRelease(Release(), current)!;
    foreach (var scenario in new[] { "valid", "corrupt", "truncated", "oversized", "cancelled" })
    {
        var bytes = scenario switch
        {
            "corrupt" => new byte[payload.Length], "truncated" => payload[..^1],
            "oversized" => payload.Concat(new byte[] { 0 }).ToArray(), _ => payload
        };
        using var http = new HttpClient(new StubHandler(() => new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(bytes) }));
        var service = new GitHubUpdateService(http, root);
        using var cancellation = new CancellationTokenSource();
        if (scenario == "cancelled") cancellation.Cancel();
        string? file = null;
        Exception? error = null;
        try { file = await service.DownloadAsync(update, null, cancellation.Token); }
        catch (Exception ex) { error = ex; }
        if (scenario == "valid")
        {
            Assert(error == null && file != null && File.ReadAllBytes(file).SequenceEqual(payload), "verified download");
            File.Delete(file!); Directory.Delete(Path.GetDirectoryName(file!)!);
        }
        else
        {
            Assert(scenario == "cancelled" ? error is OperationCanceledException : error is InvalidDataException, "invalid download must fail");
            Assert(!Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories).Any(), "partial installer must be deleted");
        }
        passed++; Console.WriteLine($"PASS download {scenario}");
    }
}
finally { Directory.Delete(root, true); }
Console.WriteLine($"{passed} update checks passed.");

sealed class StubHandler(Func<HttpResponseMessage> response) : HttpMessageHandler
{
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(response());
    }
}
