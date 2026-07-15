using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using GleapSDK.Metadata;

namespace GleapSDK.Omnis;

/// <summary>
/// Device/session metadata for the Omnis host, layered over <see cref="DefaultMetadataProvider"/>'s
/// runtime-agnostic subset (sdkType/version, locale) with the Windows fields the native SDKs report:
/// OS name/version, machine name, screen geometry, disk and power state. App identity (<see cref="AppName"/>,
/// <see cref="AppVersion"/>) is supplied by the Omnis glue rather than read from the .NET entry assembly,
/// because the running application is the Omnis library, not this COM host.
/// </summary>
/// <remarks>
/// <c>Collect()</c> runs while assembling a report, potentially on a ThreadPool thread, so every value comes
/// from a thread-safe source (Win32, <see cref="DriveInfo"/>). <c>deviceIdentifier</c> is intentionally not
/// reported (see the note on <c>WindowsMetadataProvider</c>): the Windows hardware-derived identifiers have
/// materially worse privacy properties than iOS' app-scoped id.
/// </remarks>
public sealed class OmnisMetadataProvider : IMetadataProvider
{
    private readonly DefaultMetadataProvider _baseProvider;
    private readonly DateTimeOffset _startedUtc = DateTimeOffset.UtcNow;

    /// <summary>SDK type label reported in metadata; identifies reports as coming from the Omnis SDK.</summary>
    public const string SdkType = "NET/Omnis";

    /// <param name="sdkVersion">The Gleap Omnis SDK version reported in metadata.</param>
    public OmnisMetadataProvider(string sdkVersion) =>
        _baseProvider = new DefaultMetadataProvider(SdkType, sdkVersion);

    /// <summary>The host application's display name (set by the Omnis glue). Reported as <c>bundleID</c>.</summary>
    public string AppName { get; set; } = "Omnis";

    /// <summary>The host application's version (set by the Omnis glue). Reported as <c>buildVersionNumber</c>.</summary>
    public string AppVersion { get; set; } = "1.0.0";

    /// <inheritdoc />
    public IReadOnlyDictionary<string, object?> Collect()
    {
        var meta = new Dictionary<string, object?>();
        foreach (var kv in _baseProvider.Collect())
        {
            meta[kv.Key] = kv.Value;
        }

        meta["systemName"] = "Windows";
        meta["systemVersion"] = RuntimeInformation.OSDescription;
        meta["releaseVersionNumber"] = Environment.OSVersion.Version.ToString();
        meta["deviceName"] = Environment.MachineName;
        meta["deviceModel"] = Environment.MachineName;
        meta["bundleID"] = AppName;
        meta["buildVersionNumber"] = AppVersion;
        meta["buildMode"] = "Release";
        meta["sessionDuration"] = Math.Round((DateTimeOffset.UtcNow - _startedUtc).TotalSeconds, 2);

        AddScreen(meta);
        AddDisk(meta);
        AddPower(meta);

        return meta;
    }

    private static void AddScreen(IDictionary<string, object?> meta)
    {
        try
        {
            meta["screenWidth"] = GetSystemMetrics(SmCxScreen);
            meta["screenHeight"] = GetSystemMetrics(SmCyScreen);
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

    private static void AddDisk(IDictionary<string, object?> meta)
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

    private static void AddPower(IDictionary<string, object?> meta)
    {
        try
        {
            if (!GetSystemPowerStatus(out var status))
            {
                return;
            }

            if (status.BatteryLifePercent != BatteryPercentUnknown)
            {
                meta["batteryLevel"] = Math.Round(status.BatteryLifePercent / 100.0, 2); // 0..1, like iOS
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

    private const int SmCxScreen = 0;
    private const int SmCyScreen = 1;
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
