using Akiroute.Services;
using Microsoft.Win32;

namespace Akiroute.Tests.Services;

/// <summary>
/// StartupHelper tests. The registry access is redirected through the
/// <see cref="StartupHelper.RootKeyFactoryForTests"/> seam to a scratch key
/// under HKCU\Software\AkirouteTests — the real HKCU Run entry is never touched.
/// </summary>
public class StartupHelperTests : IDisposable
{
    private readonly string _scratchPath;
    private readonly RegistryKey _scratchRoot;

    public StartupHelperTests()
    {
        _scratchPath = $@"Software\AkirouteTests\{Guid.NewGuid():N}";
        _scratchRoot = Registry.CurrentUser.CreateSubKey(_scratchPath, writable: true);
        // Production assumes the HKCU Run key always exists (Windows creates
        // it); the scratch hive must mirror that or write access would fail.
        Registry.CurrentUser.CreateSubKey(ScratchRunKey, writable: true);
        // Each invocation returns a FRESH key instance — StartupHelper disposes
        // the key it receives, so handing out the shared _scratchRoot would
        // break every subsequent access in the test.
        StartupHelper.RootKeyFactoryForTests = () =>
            Registry.CurrentUser.CreateSubKey(_scratchPath, writable: true);
    }

    public void Dispose()
    {
        StartupHelper.RootKeyFactoryForTests = null;
        _scratchRoot.Dispose();
        try
        {
            Registry.CurrentUser.DeleteSubKeyTree(_scratchPath, throwOnMissingSubKey: false);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
        {
            // Best-effort scratch cleanup; a leaked test key is harmless.
        }
    }

    private string ScratchRunKey => $@"{_scratchPath}\Software\Microsoft\Windows\CurrentVersion\Run";

    [Fact]
    public void SetLaunchOnStartup_True_WritesExePath()
    {
        using (var root = Registry.CurrentUser.CreateSubKey(ScratchRunKey, writable: true))
        {
            var ok = StartupHelper.SetLaunchOnStartup(true);
            Assert.True(ok);
            // The Run value is a command line: the path must be quoted so
            // install locations with spaces survive.
            var value = root.GetValue("Akiroute") as string;
            Assert.False(string.IsNullOrEmpty(value));
            Assert.StartsWith("\"", value);
            Assert.EndsWith("\"", value);
        }
    }

    [Fact]
    public void SetLaunchOnStartup_False_RemovesEntry()
    {
        using (var root = Registry.CurrentUser.CreateSubKey(ScratchRunKey, writable: true))
        {
            root.SetValue("Akiroute", "C:\\old\\Akiroute.exe");
        }

        var ok = StartupHelper.SetLaunchOnStartup(false);
        Assert.True(ok);

        using var root2 = Registry.CurrentUser.OpenSubKey(ScratchRunKey);
        Assert.Null(root2?.GetValue("Akiroute"));
    }

    [Fact]
    public void GetRegisteredExePath_ReturnsNullWhenAbsent()
    {
        Assert.Null(StartupHelper.GetRegisteredExePath());
        Assert.False(StartupHelper.IsLaunchOnStartupEnabled());
    }

    [Fact]
    public void Reconcile_EnabledWithMissingEntry_Rewrites()
    {
        // Desired ON, registry empty → registration is written.
        StartupHelper.ReconcileLaunchOnStartup(desired: true);

        using var root = Registry.CurrentUser.OpenSubKey(ScratchRunKey);
        Assert.NotNull(root?.GetValue("Akiroute"));
    }

    [Fact]
    public void Reconcile_DisabledWithStaleEntry_Removes()
    {
        using (var root = Registry.CurrentUser.CreateSubKey(ScratchRunKey, writable: true))
        {
            root.SetValue("Akiroute", "C:\\stale\\path\\Akiroute.exe");
        }

        StartupHelper.ReconcileLaunchOnStartup(desired: false);

        using var root2 = Registry.CurrentUser.OpenSubKey(ScratchRunKey);
        Assert.Null(root2?.GetValue("Akiroute"));
    }

    [Fact]
    public void Reconcile_EnabledWithCurrentPath_KeepsEntry()
    {
        // Quote-free comparison: an existing quoted entry pointing at the
        // current exe must NOT be rewritten or treated as stale.
        var exePath = Environment.ProcessPath ?? "C:\\current\\Akiroute.exe";
        using (var root = Registry.CurrentUser.CreateSubKey(ScratchRunKey, writable: true))
        {
            root.SetValue("Akiroute", "\"" + exePath + "\"");
        }

        StartupHelper.ReconcileLaunchOnStartup(desired: true);

        using var root2 = Registry.CurrentUser.OpenSubKey(ScratchRunKey);
        Assert.Equal("\"" + exePath + "\"", root2?.GetValue("Akiroute") as string);
    }

    [Fact]
    public void GetRegisteredExePath_StripsQuotes()
    {
        using (var root = Registry.CurrentUser.CreateSubKey(ScratchRunKey, writable: true))
        {
            root.SetValue("Akiroute", "\"C:\\Program Files\\Akiroute\\Akiroute.exe\"");
        }

        Assert.Equal("C:\\Program Files\\Akiroute\\Akiroute.exe", StartupHelper.GetRegisteredExePath());
        Assert.True(StartupHelper.IsLaunchOnStartupEnabled());
    }

    [Fact]
    public void Reconcile_EnabledWithStalePath_Corrects()
    {
        using (var root = Registry.CurrentUser.CreateSubKey(ScratchRunKey, writable: true))
        {
            root.SetValue("Akiroute", "C:\\old-location\\Akiroute.exe");
        }

        StartupHelper.ReconcileLaunchOnStartup(desired: true);

        using var root2 = Registry.CurrentUser.OpenSubKey(ScratchRunKey);
        var value = root2?.GetValue("Akiroute") as string;
        Assert.NotEqual("C:\\old-location\\Akiroute.exe", value);
        Assert.False(string.IsNullOrEmpty(value));
    }
}
