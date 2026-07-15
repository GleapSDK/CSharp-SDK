using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using GleapSDK.Metadata;

namespace GleapSDK.WebView2;

/// <summary>
/// Windows device metadata layered over <see cref="DefaultMetadataProvider"/>'s cross-platform subset,
/// filling in the fields the native SDKs report (app/build identity, screen geometry, disk, power).
/// </summary>
/// <remarks>
/// <c>Collect()</c> is called while assembling a report, which can run on a ThreadPool continuation, so
/// every value here comes from a thread-safe source (Win32 calls, <see cref="DriveInfo"/>, reflection) —
/// deliberately not WPF's <c>SystemParameters</c>, which is UI-thread-affine.
/// <para><c>deviceIdentifier</c> is intentionally NOT reported: iOS uses an app-scoped
/// <c>identifierForVendor</c>, and the Windows equivalents (registry MachineGuid, hardware serials) are
/// hardware-derived identifiers with materially worse privacy properties. A persisted per-install GUID
/// would be the right shape if this is ever wanted.</para>
/// </remarks>
public sealed class WindowsMetadataProvider : IMetadataProvider
{
    private readonly DefaultMetadataProvider _baseProvider;
    private readonly DateTimeOffset _startedUtc = DateTimeOffset.UtcNow;

    public WindowsMetadataProvider(string sdkVersion) =>
        _baseProvider = new DefaultMetadataProvider("NET/Windows", sdkVersion);

    public IReadOnlyDictionary<string, object?> Collect()
    {
        var meta = new Dictionary<string, object?>();
        foreach (var kv in _baseProvider.Collect())
        {
            meta[kv.Key] = kv.Value;
        }

        var entry = Assembly.GetEntryAssembly();

        meta["systemName"] = "Windows";
        meta["systemVersion"] = RuntimeInformation.OSDescription;
        meta["releaseVersionNumber"] = Environment.OSVersion.Version.ToString();
        meta["deviceName"] = Environment.MachineName;
        meta["deviceModel"] = Environment.MachineName;
        meta["buildVersionNumber"] = entry?.GetName().Version?.ToString() ?? "1.0.0";

        // App identity + build flavour (iOS: bundleID / buildMode).
        meta["bundleID"] = entry?.GetName().Name ?? "";
        meta["buildMode"] = entry?.GetCustomAttribute<AssemblyConfigurationAttribute>()?.Configuration ?? "Release";

        // Session duration in seconds — this provider is constructed at attach, i.e. session start.
        meta["sessionDuration"] = Math.Round((DateTimeOffset.UtcNow - _startedUtc).TotalSeconds, 2);

        AddScreen(meta);
        AddDisk(meta);
        AddPower(meta);

        return meta;
    }

    private static void AddScreen(Dictionary<string, object?> meta)
    {
        try
        {
            meta["screenWidth"] = GetSystemMetrics(SM_CXSCREEN);
            meta["screenHeight"] = GetSystemMetrics(SM_CYSCREEN);
            meta["devicePixelRatio"] = Math.Round(GetDpiForSystem() / 96.0, 2);
        }
        catch (EntryPointNotFoundException)
        {
            // GetDpiForSystem needs Windows 10 1607+; screen size alone is still useful.
        }
        catch (DllNotFoundException)
        {
            // Non-Windows host (shouldn't happen for this TFM) — skip rather than fail the report.
        }
    }

    private static void AddDisk(Dictionary<string, object?> meta)
    {
        try
        {
            var root = Path.GetPathRoot(Environment.SystemDirectory);
            if (string.IsNullOrEmpty(root))
            {
                return;
            }
            var drive = new DriveInfo(root!);
            if (!drive.IsReady)
            {
                return;
            }
            // Megabytes, matching the native SDKs' disk reporting.
            meta["totalDiskSpace"] = drive.TotalSize / (1024 * 1024);
            meta["freeDiskSpace"] = drive.AvailableFreeSpace / (1024 * 1024);
        }
        catch (IOException) { /* disk unavailable — omit rather than fail */ }
        catch (UnauthorizedAccessException) { /* ditto */ }
    }

    private static void AddPower(Dictionary<string, object?> meta)
    {
        try
        {
            if (!GetSystemPowerStatus(out var status))
            {
                return;
            }
            if (status.BatteryLifePercent != BatteryPercentUnknown)
            {
                meta["batteryLevel"] = Math.Round(status.BatteryLifePercent / 100.0, 2);   // 0..1, like iOS
            }
            if (status.ACLineStatus != AcLineUnknown)
            {
                meta["phoneChargingStatus"] = status.ACLineStatus == 1;
            }
            meta["batterySaveMode"] = status.SystemStatusFlag == 1;
        }
        catch (DllNotFoundException) { /* not on Windows — omit */ }
        catch (EntryPointNotFoundException) { /* ditto */ }
    }

    private const int SM_CXSCREEN = 0;
    private const int SM_CYSCREEN = 1;
    private const byte BatteryPercentUnknown = 255;
    private const byte AcLineUnknown = 255;

    [DllImport("user32.dll")]
    private static extern int GetSystemMetrics(int nIndex);

    [DllImport("user32.dll")]
    private static extern uint GetDpiForSystem();

    [DllImport("kernel32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetSystemPowerStatus(out SystemPowerStatus status);

    [StructLayout(LayoutKind.Sequential)]
    private struct SystemPowerStatus
    {
        public byte ACLineStatus;
        public byte BatteryFlag;
        public byte BatteryLifePercent;
        public byte SystemStatusFlag;
        public int BatteryLifeTime;
        public int BatteryFullLifeTime;
    }
}
