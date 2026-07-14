using System;
using System.Windows;

namespace GleapSDK.Sample.Wpf;

public partial class MainWindow : Window
{
    private bool _initialized;

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
            StatusText.Text = "Initializing…";
            Messenger.SdkKey = sdkKey;
            await Messenger.InitializeAsync();
            _initialized = true;
            StatusText.Text = "Ready. Click the launcher (bottom-right) or Open to show the messenger.";
        }
        catch (Exception ex)
        {
            StatusText.Text = "Init failed: " + ex.Message;
            MessageBox.Show(ex.ToString(), "Gleap init failed");
        }
    }

    // Open/Close drive the overlay; the launcher button and the widget's own ✕ do the same via the control.
    private void OnOpen(object sender, RoutedEventArgs e) => Guarded(() => Messenger.ShowMessenger());

    private void OnClose(object sender, RoutedEventArgs e) => Guarded(() =>
    {
        Gleap.Close();
        Messenger.HideMessenger();
    });

    // Navigation implies opening the messenger, matching the JS SDK.
    private void OnStartConversation(object sender, RoutedEventArgs e) => GuardedNav(() => Gleap.StartConversation());
    private void OnStartBot(object sender, RoutedEventArgs e) => GuardedNav(() => Gleap.StartBot(""));
    private void OnOpenHelpCenter(object sender, RoutedEventArgs e) => GuardedNav(() => Gleap.OpenHelpCenter());
    private void OnOpenNews(object sender, RoutedEventArgs e) => GuardedNav(() => Gleap.OpenNews());

    private void OnTrackEvent(object sender, RoutedEventArgs e) => Guarded(() => Gleap.TrackEvent("sample-button-clicked", null));
    private void OnSetCustomData(object sender, RoutedEventArgs e) => Guarded(() => Gleap.SetCustomData("plan", "pro"));

    private void GuardedNav(Action navigate) => Guarded(() =>
    {
        Messenger.ShowMessenger();
        navigate();
    });

    private void Guarded(Action action)
    {
        if (!_initialized)
        {
            StatusText.Text = "Initialize first.";
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
