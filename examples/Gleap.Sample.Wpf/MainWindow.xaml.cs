using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Windows;
using GleapSDK.Extensions.Logging;
using Microsoft.Extensions.Logging;

namespace GleapSDK.Sample.Wpf;

public partial class MainWindow : Window
{
    // Replace with your own project SDK key. Kept here (not in a text box) so the demo
    // initializes Gleap purely in code, exactly like Gleap.initialize("KEY") in the JS SDK.
    private const string DemoSdkKey = "ogWhNhuiZcGWrva5nlDS8l7a78OfaLlV";

    public MainWindow()
    {
        InitializeComponent();
        Messenger.SdkKey = Environment.GetEnvironmentVariable("gleap.sdkkey") ?? DemoSdkKey;

        // Once Gleap is ready, record some sample activity so a submitted ticket shows the full
        // picture a real app would attach automatically.
        Messenger.Ready += async (_, _) =>
        {
            // Activity log (customEventLog) + custom data — real apps call these as things happen.
            Gleap.TrackPage("Acme Dashboard");
            Gleap.TrackEvent("deployment-succeeded", new Dictionary<string, object> { ["id"] = 482 });
            Gleap.TrackEvent("nightly-backup-completed");
            Gleap.SetCustomData("plan", "pro");

            // Console log: route the app's ILogger through Gleap and log a couple of lines — they
            // land in consoleLog on the next ticket. A real app keeps one long-lived ILoggerFactory
            // (from the generic host / DI); here we build a throwaway one just for the demo. In MAUI
            // it's a one-liner: builder.Logging.AddGleap().
            using (var loggerFactory = LoggerFactory.Create(b => b.AddGleap()))
            {
                var logger = loggerFactory.CreateLogger("Acme.Dashboard");
                // CA1848/CA1873: hot-path library code would use LoggerMessage source-gen delegates,
                // but a demo should show the plain ILogger calls a typical app actually writes.
#pragma warning disable CA1848, CA1873
                logger.LogInformation("User {User} opened the dashboard", "acme-admin");
                logger.LogWarning("Cache miss for key {Key}", "user:482");
#pragma warning restore CA1848, CA1873
            }

            // Network log: any HttpClient routed through the Gleap handler is captured into
            // networkLogs. (.NET has no global HTTP interception, so app traffic opts in this way.)
            using var http = new HttpClient(Messenger.Backend!.CreateNetworkLoggingHandler());
            try
            {
                await http.GetAsync("https://gleap.io/").ConfigureAwait(true);
            }
            catch (HttpRequestException)
            {
                // Best-effort demo request — ignore connectivity failures.
            }
        };
    }
}
