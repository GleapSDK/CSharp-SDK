using System.Threading;
using System.Threading.Tasks;

namespace GleapSDK;

/// <summary>
/// The swappable engine behind the <see cref="Gleap"/> facade.
/// SP-0 provides <c>ManagedBackend</c>; platform packages later add native-bridge backends.
/// Only the surface needed for SP-0 Part 1 is declared; it grows in later plans.
/// </summary>
public interface IGleapBackend
{
    Task InitializeAsync(string token, CancellationToken ct);
    void Open();
    void Close();
    void StartConversation(bool showBackButton);
    void StartBot(string botId, bool showBackButton);
    void OpenConversation(string shareToken);
    void OpenHelpCenter(bool showBackButton);
    void OpenNews(bool showBackButton);
    void ShowSurvey(string surveyId, SurveyFormat format);
}
