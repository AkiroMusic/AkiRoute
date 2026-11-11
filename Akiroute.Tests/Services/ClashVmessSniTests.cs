using System.Text.Json.Nodes;
using Akiroute.Models;
using Akiroute.Services;

namespace Akiroute.Tests.Services;

/// <summary>
/// End-to-end SNI mapping for Clash VMess imports: YAML → ClashConfigParser →
/// XrayConfigBuilder. The TLS SNI and the WS Host are deliberately different
/// values so a mix-up (or a fallback to the transport host / server address)
/// cannot pass — this is the mapping the engine-variant matrix cannot see,
/// because that matrix builds nodes directly via WithParams.
/// </summary>
public class ClashVmessSniTests
{
    private const string Uuid = "11111111-1111-1111-1111-111111111111";

    [Fact]
    public void Servername_FlowsToTlsSettingsServerName()
    {
        // Arrange: vmess with TLS, a dedicated servername, and a WS Host that
        // differs from both the SNI and the server address.
        var yaml = $"""
            proxies:
              - name: vmess-sni
                type: vmess
                server: edge.example.com
                port: 443
                uuid: {Uuid}
                alterId: 0
                cipher: auto
                tls: true
                servername: origin.example.com
                network: ws
                ws-opts:
                  path: /ws
                  headers:
                    Host: cdn.example.com
            """;

        // Act: full import → build chain.
        var node = Assert.Single(ClashConfigParser.Parse(yaml));
        var config = XrayConfigBuilder.Build(node, Array.Empty<ProcessRule>(), 3333, ProxyMode.Global);

        // Assert: the vmess outbound's TLS serverName is exactly the YAML
        // servername — not the WS Host, not the server address.
        var outbound = SingleOutbound(config, "vmess");
        var stream = outbound["streamSettings"]!;
        Assert.Equal("tls", stream["security"]!.GetValue<string>());
        Assert.Equal("origin.example.com", stream["tlsSettings"]!["serverName"]!.GetValue<string>());
        Assert.Equal("cdn.example.com", stream["wsSettings"]!["headers"]!["Host"]!.GetValue<string>());
    }

    [Fact]
    public void PlainSni_FlowsToTlsSettingsServerName()
    {
        // The mihomo-dialect alias keeps working when servername is absent.
        var yaml = $"""
            proxies:
              - name: vmess-sni-alias
                type: vmess
                server: edge.example.com
                port: 443
                uuid: {Uuid}
                cipher: auto
                tls: true
                sni: alias.example.com
            """;

        var node = Assert.Single(ClashConfigParser.Parse(yaml));
        var config = XrayConfigBuilder.Build(node, Array.Empty<ProcessRule>(), 3333, ProxyMode.Global);

        var stream = SingleOutbound(config, "vmess")["streamSettings"]!;
        Assert.Equal("tls", stream["security"]!.GetValue<string>());
        Assert.Equal("alias.example.com", stream["tlsSettings"]!["serverName"]!.GetValue<string>());
    }

    [Fact]
    public void ServernameWins_WhenBothKeysPresent()
    {
        var yaml = $"""
            proxies:
              - name: vmess-both
                type: vmess
                server: edge.example.com
                port: 443
                uuid: {Uuid}
                cipher: auto
                tls: true
                sni: sni.example.com
                servername: documented.example.com
            """;

        var node = Assert.Single(ClashConfigParser.Parse(yaml));
        var config = XrayConfigBuilder.Build(node, Array.Empty<ProcessRule>(), 3333, ProxyMode.Global);

        var stream = SingleOutbound(config, "vmess")["streamSettings"]!;
        Assert.Equal("documented.example.com", stream["tlsSettings"]!["serverName"]!.GetValue<string>());
    }

    [Fact]
    public void TlsFalse_DoesNotEnableTls()
    {
        // A servername alone must never turn TLS on; an explicit tls: false
        // means no tlsSettings and security "none".
        var yaml = $"""
            proxies:
              - name: vmess-plain
                type: vmess
                server: edge.example.com
                port: 80
                uuid: {Uuid}
                cipher: auto
                tls: false
                servername: origin.example.com
            """;

        var node = Assert.Single(ClashConfigParser.Parse(yaml));
        var config = XrayConfigBuilder.Build(node, Array.Empty<ProcessRule>(), 3333, ProxyMode.Global);

        var stream = SingleOutbound(config, "vmess")["streamSettings"]!;
        Assert.Equal("none", stream["security"]!.GetValue<string>());
        Assert.Null(stream["tlsSettings"]);
    }

    /// <summary>Returns the single outbound with the given protocol tag.</summary>
    private static JsonNode SingleOutbound(JsonObject config, string protocol)
    {
        return Assert.Single(
            config["outbounds"]!.AsArray(),
            o => o is not null && o["protocol"]!.GetValue<string>() == protocol)!;
    }
}
