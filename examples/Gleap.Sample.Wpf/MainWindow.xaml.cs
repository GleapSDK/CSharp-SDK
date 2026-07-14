using System;
using System.Collections.Generic;
using System.Windows;

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

        // Once Gleap is ready, record some sample activity + custom data. These ride along on any
        // bug report as the activity log (customEventLog) / custom data — real apps call these as
        // things happen in the app.
        Messenger.Ready += (_, _) =>
        {
            Gleap.TrackPage("Acme Dashboard");
            Gleap.TrackEvent("deployment-succeeded", new Dictionary<string, object> { ["id"] = 482 });
            Gleap.TrackEvent("nightly-backup-completed");
            Gleap.SetCustomData("plan", "pro");
        };
    }
}
