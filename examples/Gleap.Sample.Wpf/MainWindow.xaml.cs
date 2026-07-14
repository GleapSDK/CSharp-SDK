using System;
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
    }
}
