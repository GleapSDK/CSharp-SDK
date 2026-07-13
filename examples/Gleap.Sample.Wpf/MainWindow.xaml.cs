using System;
using System.Windows;
using GleapSDK;
using GleapSDK.WebView2;

namespace GleapSDK.Sample.Wpf;

public partial class MainWindow : Window
{
    private bool _attached;

    public MainWindow()
    {
        InitializeComponent();
        SdkKeyBox.Text = Environment.GetEnvironmentVariable("gleap.sdkkey") ?? "";
    }

    private async void OnAttach(object sender, RoutedEventArgs e)
    {
        var sdkKey = SdkKeyBox.Text.Trim();
        if (string.IsNullOrEmpty(sdkKey))
        {
            StatusText.Text = "Enter an SDK key first.";
            return;
        }

        try
        {
            StatusText.Text = "Attaching…";
            await GleapWebView2Host.AttachAsync(WebView, sdkKey);
            _attached = true;
            StatusText.Text = "Attached. Click Open to show the widget.";
        }
        catch (Exception ex)
        {
            StatusText.Text = "Attach failed: " + ex.Message;
            MessageBox.Show(ex.ToString(), "Gleap attach failed");
        }
    }

    private void OnOpen(object sender, RoutedEventArgs e) => Guarded(() => Gleap.Open());
    private void OnClose(object sender, RoutedEventArgs e) => Guarded(() => Gleap.Close());
    private void OnStartConversation(object sender, RoutedEventArgs e) => Guarded(() => Gleap.StartConversation());
    private void OnStartBot(object sender, RoutedEventArgs e) => Guarded(() => Gleap.StartBot(""));
    private void OnOpenHelpCenter(object sender, RoutedEventArgs e) => Guarded(() => Gleap.OpenHelpCenter());
    private void OnOpenNews(object sender, RoutedEventArgs e) => Guarded(() => Gleap.OpenNews());
    private void OnTrackEvent(object sender, RoutedEventArgs e) => Guarded(() => Gleap.TrackEvent("sample-button-clicked", null));
    private void OnSetCustomData(object sender, RoutedEventArgs e) => Guarded(() => Gleap.SetCustomData("plan", "pro"));

    private void Guarded(Action action)
    {
        if (!_attached)
        {
            StatusText.Text = "Attach first.";
            return;
        }
        try
        {
            action();
        }
        catch (Exception ex)
        {
            StatusText.Text = "Error: " + ex.Message;
        }
    }
}
