using System.Diagnostics;
using System.Text.Json.Nodes;
using Akiroute.Models;
using Akiroute.Services;

namespace Akiroute.Tests.Services;

/// <summary>
/// A [Fact] that SKIPS (visibly, in the runner result) when the bundled xray
/// engine asset is absent — the canonical xUnit 2.x conditional-skip pattern.
/// CI downloads the pinned engine before running tests, so a Skip there means
/// the setup step failed and is visible as a non-passing result.
/// </summary>
public sealed class EngineFactAttribute : FactAttribute
{
    public EngineFactAttribute()
    {
        if (XrayConfigValidationTests.ResolvedEngine is null)
        {
            Skip = "bundled xray.exe not available — run tools/fetch-assets.ps1";
        }
    }
}

/// <summary>The [Theory] counterpart of <see cref="EngineFactAttribute"/>.</summary>
public sealed class EngineTheoryAttribute : TheoryAttribute
{
    public EngineTheoryAttribute()
    {
        if (XrayConfigValidationTests.ResolvedEngine is null)
        {
            Skip = "bundled xray.exe not available — run tools/fetch-assets.ps1";
        }
    }
}

/// <summary>
/// Engine-driven config validation: every protocol and key transport variant
/// the app claims to support is built via <see cref="XrayConfigBuilder"/> and
/// fed to the REAL bundled engine with <c>xray run -test</c>. JSON-shape
/// assertions alone cannot prove the engine accepts a config — protocol names
/// and schema drift are only caught here (this is how the ss→shadowsocks and
/// hysteria2→hysteria mappings were verified).
///
/// The engine asset is gitignored; CI downloads the pinned version via
/// tools/fetch-assets.ps1 BEFORE running tests, so a missing engine means the
/// setup itself failed and the test reports a visible Skipped result via the
/// <see cref="EngineFactAttribute"/>/<see cref="EngineTheoryAttribute"/> Skip
/// property — never a fake pass.
/// </summary>
public class XrayConfigValidationTests
{
    private const string Uuid = "b831381d-6324-4d53-ad4f-8cda48b30811";

    private static string? SourceXrayExe =>
        OperatingSystem.IsWindows()
            ? Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "Akiroute", "Assets", "engine", "xray.exe")
            : null;

    internal static string? ResolvedEngine =>
        SourceXrayExe is not null && File.Exists(SourceXrayExe) ? Path.GetFullPath(SourceXrayExe) : null;

    public static IEnumerable<object[]> ProtocolMatrix()
    {
        yield return new object[] { "vless-tls", VlessNode() };
        yield return new object[] { "vless-reality", WithParams(VlessNode(), ("security", "reality"), ("publicKey", "AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA"), ("shortId", "ab12"), ("spiderX", "/sp")) };
        yield return new object[] { "vless-ws", WithParams(VlessNode(), ("network", "ws"), ("host", "cdn.example.com"), ("path", "/ws")) };
        yield return new object[] { "vless-grpc", WithParams(VlessNode(), ("network", "grpc"), ("serviceName", "svc")) };
        yield return new object[] { "vmess-tls", VmessNode() };
        yield return new object[] { "vmess-clash-tls-bool", WithParams(VmessNode(), ("security", "auto"), ("tls", "true"), ("sni", "example.com"), ("fingerprint", "chrome"), ("alpn", "h2,h3")) };
        yield return new object[] { "vmess-ws", WithParams(VmessNode(), ("network", "ws"), ("host", "cdn.example.com"), ("path", "/ws")) };
        yield return new object[] { "ss", SsNode() };
        yield return new object[] { "trojan-ws", WithParams(TrojanNode(), ("network", "ws"), ("host", "cdn.example.com"), ("path", "/ws")) };
        yield return new object[] { "trojan-grpc", WithParams(TrojanNode(), ("network", "grpc"), ("serviceName", "svc")) };
        yield return new object[] { "hysteria2", Hysteria2Node() };
        yield return new object[] { "hysteria2-obfs-alpn", WithParams(Hysteria2Node(), ("obfs", "salamander"), ("obfsPassword", "obfspw"), ("alpn", "h3")) };
    }

    [EngineTheory]
    [MemberData(nameof(ProtocolMatrix))]
    public async Task ProtocolVariants_PassRealEngineValidation(string label, ProxyNode node)
    {
        var engine = ResolvedEngine!;
        var path = Path.Combine(Path.GetTempPath(), $"akiroute-validate-{label}-{Guid.NewGuid():N}.json");
        File.WriteAllText(path, XrayConfigBuilder.Build(node, Array.Empty<ProcessRule>(), 3333, ProxyMode.Global).ToJsonString());
        try
        {
            var (exitCode, output) = await RunEngineTestAsync(engine!, path);
            if (exitCode != 0)
            {
                Assert.Fail($"{label} rejected by the bundled engine (exit {exitCode}): {FirstErrorLine(output)}");
            }
        }
        finally
        {
            File.Delete(path);
        }
    }

    [EngineFact]
    public void TuicNode_IsRejectedByBuilder()
    {
        // TUIC has no outbound in the bundled engine; the builder must fail
        // loudly instead of emitting a config the engine cannot load.
        Assert.Throws<ArgumentException>(() => BuildFor(TuicNode()));
    }

    // ── helpers ──────────────────────────────────────────────────────────

    private static JsonObject BuildFor(ProxyNode node) =>
        XrayConfigBuilder.Build(node, Array.Empty<ProcessRule>(), 3333, ProxyMode.Global);

    private static ProxyNode WithParams(ProxyNode node, params (string Key, string Value)[] pairs)
    {
        foreach (var (key, value) in pairs)
        {
            node.ExtraParams ??= new Dictionary<string, JsonNode>(StringComparer.Ordinal);
            node.ExtraParams[key] = System.Text.Json.Nodes.JsonValue.Create(value);
        }
        return node;
    }

    private static ProxyNode VlessNode() => new()
    {
        Type = "vless",
        Address = "203.0.113.10",
        Port = 443,
        ExtraParams = Params(("uuid", Uuid), ("security", "tls"), ("sni", "example.com")),
    };

    private static ProxyNode VmessNode() => new()
    {
        Type = "vmess",
        Address = "203.0.113.10",
        Port = 443,
        ExtraParams = Params(("uuid", Uuid), ("security", "tls")),
    };

    private static ProxyNode SsNode() => new()
    {
        Type = "ss",
        Address = "203.0.113.10",
        Port = 8388,
        ExtraParams = Params(("method", "aes-256-gcm"), ("password", "testpass123")),
    };

    private static ProxyNode TrojanNode() => new()
    {
        Type = "trojan",
        Address = "203.0.113.10",
        Port = 443,
        ExtraParams = Params(("password", "testpass123"), ("sni", "example.com")),
    };

    private static ProxyNode Hysteria2Node() => new()
    {
        Type = "hysteria2",
        Address = "203.0.113.10",
        Port = 8443,
        ExtraParams = Params(("password", "authpass"), ("sni", "example.com")),
    };

    private static ProxyNode TuicNode() => new()
    {
        Type = "tuic",
        Address = "203.0.113.10",
        Port = 443,
        ExtraParams = Params(("uuid", Uuid), ("password", "testpass123")),
    };

    private static Dictionary<string, JsonNode> Params(params (string Key, string Value)[] pairs)
    {
        var dict = new Dictionary<string, JsonNode>(StringComparer.Ordinal);
        foreach (var (key, value) in pairs)
        {
            dict[key] = System.Text.Json.Nodes.JsonValue.Create(value);
        }
        return dict;
    }

    /// <summary>
    /// Runs the engine with <c>run -test</c>. Both streams are drained
    /// CONCURRENTLY (sequential ReadToEnd calls can deadlock when the child
    /// fills the stderr pipe while the parent blocks on stdout), the wait is
    /// bounded, and a hung engine is killed — a timeout is a FAILED
    /// validation, never a pass.
    /// </summary>
    private static async Task<(int ExitCode, string Output)> RunEngineTestAsync(string engine, string configPath)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = engine,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        startInfo.ArgumentList.Add("run");
        startInfo.ArgumentList.Add("-test");
        startInfo.ArgumentList.Add("-config");
        startInfo.ArgumentList.Add(configPath);

        using var process = Process.Start(startInfo)!;
        var stdoutTask = process.StandardOutput.ReadToEndAsync();
        var stderrTask = process.StandardError.ReadToEndAsync();

        var waitTask = process.WaitForExitAsync(CancellationToken.None);
        var completed = await Task.WhenAny(waitTask, Task.Delay(TimeSpan.FromSeconds(20))).ConfigureAwait(false);

        if (completed != waitTask)
        {
            // Engine hung past the validation timeout: kill the tree and fail.
            try
            {
                process.Kill(entireProcessTree: true);
                await process.WaitForExitAsync(CancellationToken.None).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is InvalidOperationException or NotSupportedException)
            {
            }

            Assert.Fail($"xray run -test timed out after 20 s for config {Path.GetFileName(configPath)}");
        }

        var output = (await stdoutTask.ConfigureAwait(false)) + (await stderrTask.ConfigureAwait(false));
        return (process.ExitCode, output);
    }

    private static string FirstErrorLine(string output)
    {
        foreach (var line in output.Split('\n'))
        {
            var trimmed = line.Trim();
            if (trimmed.Length > 0 && trimmed.StartsWith("Failed", StringComparison.OrdinalIgnoreCase))
            {
                return trimmed.Length > 300 ? trimmed[..300] : trimmed;
            }
        }

        return output.Length > 300 ? output[..300] : output;
    }
}
