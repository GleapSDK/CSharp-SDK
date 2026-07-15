using System.Windows;

namespace GleapSDK.WebView2;

/// <summary>
/// Marks UI that must never leave the device in a screenshot or session-replay frame — password boxes,
/// payment fields, personal data. Censored elements are masked out of every capture
/// (<see cref="WindowsScreenshotProvider"/>), which is the desktop equivalent of the JS SDK's
/// <c>gleap-ignore</c> / <c>rr-mask</c> markers.
/// </summary>
/// <example>
/// In XAML:
/// <code>
/// xmlns:gleap="clr-namespace:GleapSDK.WebView2;assembly=Gleap.WebView2"
/// &lt;TextBox gleap:GleapCensor.IsCensored="True" /&gt;
/// </code>
/// Or in code: <c>GleapCensor.SetIsCensored(myTextBox, true);</c>
/// </example>
public static class GleapCensor
{
    /// <summary>When true, this element's area is masked out of Gleap screenshots and replay frames.
    /// Applying it to a container censors the whole subtree.</summary>
    public static readonly DependencyProperty IsCensoredProperty =
        DependencyProperty.RegisterAttached(
            "IsCensored",
            typeof(bool),
            typeof(GleapCensor),
            new PropertyMetadata(false));

    /// <summary>Sets whether <paramref name="element"/> is masked out of captures.</summary>
    public static void SetIsCensored(DependencyObject element, bool value) =>
        element.SetValue(IsCensoredProperty, value);

    /// <summary>Whether <paramref name="element"/> is masked out of captures.</summary>
    public static bool GetIsCensored(DependencyObject element) =>
        (bool)element.GetValue(IsCensoredProperty);
}
