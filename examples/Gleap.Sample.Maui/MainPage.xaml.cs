using GleapSDK.Maui;
// The project namespace is "Gleap.Sample.Maui", so a bare "Gleap" resolves to that namespace and
// shadows the facade type — alias it explicitly.
using GleapSdk = GleapSDK.Gleap;

namespace Gleap.Sample.Maui;

public partial class MainPage : ContentPage
{
    // Replace with your project SDK key (Gleap's own public demo key here so it runs out of the box).
    private const string DemoSdkKey = "ogWhNhuiZcGWrva5nlDS8l7a78OfaLlV";

    private bool _attached;

    public MainPage()
    {
        InitializeComponent();
    }

    private async void OnAttach(object sender, EventArgs e)
    {
        try
        {
            Status.Text = "Attaching…";
            var channel = new MauiWebViewChannel(Web);
            await GleapMaui.AttachAsync(channel, DemoSdkKey);
            _attached = true;
            Status.Text = "Attached. Use the buttons to drive the messenger.";
        }
        catch (Exception ex)
        {
            Status.Text = "Attach failed: " + ex.Message;
        }
    }

    private void OnOpen(object sender, EventArgs e) => Guarded(() => GleapSdk.Open());
    private void OnStartBot(object sender, EventArgs e) => Guarded(() => GleapSdk.StartBot(""));
    private void OnHelpCenter(object sender, EventArgs e) => Guarded(() => GleapSdk.OpenHelpCenter());
    private void OnNews(object sender, EventArgs e) => Guarded(() => GleapSdk.OpenNews());
    private void OnTrackEvent(object sender, EventArgs e) => Guarded(() => GleapSdk.TrackEvent("maui-sample-clicked", null));

    private void Guarded(Action action)
    {
        if (!_attached)
        {
            Status.Text = "Attach first.";
            return;
        }
        try
        {
            action();
        }
        catch (Exception ex)
        {
            Status.Text = "Error: " + ex.Message;
        }
    }
}
