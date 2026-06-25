using System.Diagnostics.CodeAnalysis;
using Microsoft.Extensions.Options;
using System.Text;
using System.Text.Json;

namespace RmqToRestApiForwarder;

[SuppressMessage("Performance", "CA1873:Avoid potentially expensive logging")]
public class GitHubCodespaceAwaker(
    IOptions<GitHubCodespaceSettings> codespaceSettings,
    IHttpClientFactory httpClientFactory,
    ILogger<GitHubCodespaceAwaker> logger)
{
    private enum RequestState
    {
        Idle,
        Requested,
        InProgress
    }

    private readonly int _timeoutSeconds = codespaceSettings.Value.TimeoutSeconds;

    private readonly Lock _stateLock = new();
    private Timer? _resetTimer;

    private RequestState State
    {
        get { lock (_stateLock) return field; }
        set { lock (_stateLock) field = value; }
    } = RequestState.Idle;

    public async Task Awake(CancellationToken cancellationToken)
    {
        if (State != RequestState.Idle)
        {
            logger.LogInformation("Awake() call ignored because current state is {State}", State);
            return;
        }
        State = RequestState.Requested;

        logger.LogInformation("Codespace awake sequence started. State -> Requested");

        var codespaceName = codespaceSettings.Value.CodespaceName;
        var startUrl = codespaceSettings.Value.StartUrl;
        if (string.IsNullOrWhiteSpace(codespaceName) || string.IsNullOrWhiteSpace(startUrl))
        {
            logger.LogWarning("Codespace awake request skipped because GitHubCodespace settings are incomplete.");
            State = RequestState.Idle;
            return;
        }

        try
        {
            using var httpClient = httpClientFactory.CreateClient();
            var payload = JsonSerializer.Serialize(new { key = "abc", query = codespaceName });
            using var jsonContent = new StringContent(payload, Encoding.UTF8, "application/json");

            logger.LogInformation("Sending Codespace start request for '{CodespaceName}'", codespaceName);
            var response = await httpClient.PostAsync(startUrl, jsonContent, cancellationToken);

            if (!response.IsSuccessStatusCode)
            {
                // Log full response body for diagnostics
                var body = string.Empty;
                try
                {
                    body = await response.Content.ReadAsStringAsync(cancellationToken);
                }
                catch (Exception readEx)
                {
                    logger.LogInformation(readEx, "Failed reading error response body");
                }
                var reason = $"HTTP {(int)response.StatusCode} {response.ReasonPhrase}";
                logger.LogWarning("Codespace start request failed: {Reason}. Body: {Body}", reason, body);
                throw new Exception(reason);
            }
            logger.LogInformation("Codespace start request accepted by server (HTTP {Status}).", (int)response.StatusCode);
        }
        catch (Exception ex)
        {
            logger.LogWarning("Codespace start request encountered {ExceptionType}: {Message}", ex.GetType().Name, ex.Message);
        }
        finally
        {
            // Transition to InProgress and schedule reset
            State = RequestState.InProgress;
            logger.LogInformation("Codespace awaker state -> InProgress. Will reset to Idle in {Timeout}s", _timeoutSeconds);

            Timer? oldTimer;
            lock (_stateLock)
            {
                oldTimer = _resetTimer;
                _resetTimer = new Timer(_ =>
                {
                    State = RequestState.Idle;
                    logger.LogInformation("Codespace awaker state reset to Idle after timeout of {Timeout}s", _timeoutSeconds);
                }, null, TimeSpan.FromSeconds(_timeoutSeconds), Timeout.InfiniteTimeSpan);
            }
            // ReSharper disable once MethodHasAsyncOverload
            oldTimer?.Dispose();
        }
    }
}
