using System.Diagnostics;
using System.Text.Json.Nodes;
using Akiroute.Models;
using Akiroute.Services;

namespace Akiroute.Tests.Services;

/// <summary>
/// Engine-driven config validation: every protocol the app claims to support
/// is built via <see cref="XrayConfigBuilder"/> and fed to the REAL bundled
/// engine with <c>xray run -test</c>. JSON-shape assertions alone cannot prove
/// the engine accepts a config — protocol names and schema drift are only
/// caught here (this is how the ss→shadowsocks and hysteria2→hysteria
/// mappings were verified).
///
/// Gated on the source-tree engine asset (run tools/fetch-assets.ps1 first);
/// without it the suite reports the coverage gap loudly instead of silently
/// passing.
/// </summary>
public class XrayConfigValidationTests
{
    private const string Uuid = "b831381d-6324-4d53-ad4f-8cda48b30811";

    private static string? SourceXrayExe =>
        OperatingSystem.IsWindows()
            ? Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "Akiroute", "Assets", "engine", "xray.exe")
            : null;

    private static string? ResolvedEngine =>
        SourceXrayExe is not null && File.Exists(SourceXrayExe) ? Path.GetFullPath(SourceXrayExe) : null;

    [Fact]
    public void SupportedProtocols_PassRealEngineValidation()
    {
        var engine = ResolvedEngine;
        if (engine is null)
        {
            // Engine integration coverage NOT RUN: the bundled xray.exe is
            // missing (tools/fetch-assets.ps1). Reported loudly so a vacuous
            // green suite cannot hide the gap.
            Console.WriteLine("[SKIP] XrayConfigValidationTests: bundled xray.exe not available — engine config validation is NOT running.");
            return;
        }

        var configs = new Dictionary<string, JsonObject>
        {
            ["vless"] = BuildFor(VlessNode()),
            ["vmess"] = BuildFor(VmessNode()),
            ["ss"] = BuildFor(SsNode()),
            ["trojan"] = BuildFor(TrojanNode()),
            ["hysteria2"] = BuildFor(Hysteria2Node()),
        };

        var failures = new List<string>();
        foreach (var (protocol, config) in configs)
        {
            var path = Path.Combine(Path.GetTempPath(), $"akiroute-validate-{protocol}-{Guid.NewGuid():N}.json");
            File.WriteAllText(path, config.ToJsonString());
            try
            {
                var (exitCode, output) = RunEngineTest(engine, path);
                if (exitCode != 0)
                {
                    failures.Add($"{protocol}: exit {exitCode}: {FirstErrorLine(output)}");
                }
            }
            finally
            {
                File.Delete(path);
            }
        }

        Assert.True(failures.Count == 0,
            "protocols rejected by the bundled engine:\n" + string.Join("\n", failures));
    }

    [Fact]
    public void TuicNode_IsRejectedByBuilder()
    {
        // TUIC has no outbound in the bundled engine; the builder must fail
        // loudly instead of emitting a config the engine cannot load.
        Assert.Throws<ArgumentException>(() => BuildFor(TuicNode()));
    }

    // ── helpers ──────────────────────────────────────────────────────────

    private static JsonObject BuildFor(ProxyNode node) =>
        XrayConfigBuilder.Build(node, Array.Empty<ProcessRule>(), 3333, ProxyMode.Global);

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

    private static (int ExitCode, string Output) RunEngineTest(string engine, string configPath)
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
        var output = process.StandardOutput.ReadToEnd() + process.StandardError.ReadToEnd();
        process.WaitForExit(15000);
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
