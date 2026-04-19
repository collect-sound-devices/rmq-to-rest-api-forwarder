using System.Diagnostics.CodeAnalysis;
using Microsoft.Extensions.Options;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;

namespace RmqToRestApiForwarder;

public class GitHubCodespaceAwaker(
    IOptions<GitHubCodespaceSettings> codespaceSettings,
    CryptService cryptService,
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

    // Thread-safe state property
    private RequestState State
    {
        get { lock (_stateLock) return field; }
        set { lock (_stateLock) field = value; }
    } = RequestState.Idle;

    // Atomic compare-and-set helper

    [SuppressMessage("Performance", "CA1873:Avoid potentially expensive logging")]
    public async Task Awake(CancellationToken cancellationToken)
    {
        if (State != RequestState.Idle)
        {
            logger.LogInformation("Awake() call ignored because current state is {State}", State);
            return;
        }
        State = RequestState.Requested;

        logger.LogInformation("Codespace awake sequence started. State -> Requested");

        const string shortestPassword = "my.shortest.password";

        var codespaceName = cryptService.TryDecryptOrReturnOriginal(codespaceSettings.Value.CodespaceName, shortestPassword);
        var urlExpanded = codespaceSettings.Value.StartUrl.Replace("{codespace}", codespaceName, StringComparison.OrdinalIgnoreCase);
        var authorizationValue = "Bearer " + cryptService.TryDecryptOrReturnOriginal(codespaceSettings.Value.Token, shortestPassword);

        try
        {
            using var httpClient = httpClientFactory.CreateClient();
            httpClient.DefaultRequestHeaders.Add("Authorization", authorizationValue);
            httpClient.DefaultRequestHeaders.Add("Accept", "application/vnd.github.v3+json");
            httpClient.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("rmq-to-rest-api-forwarder", "1.0"));

            var payload = JsonSerializer.Serialize(new { codespace_name = codespaceName });
            using var jsonContent = new StringContent(payload, Encoding.UTF8, "application/json");

            logger.LogInformation("Sending GitHub Codespace start request for '{CodespaceName}'", codespaceName);
            var response = await httpClient.PostAsync(urlExpanded, jsonContent, cancellationToken);

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
                logger.LogWarning("GitHub Codespace start request failed: {Reason}. Body: {Body}", reason, body);
                throw new Exception(reason);
            }
            logger.LogInformation("GitHub Codespace start request accepted by server (HTTP {Status}).", (int)response.StatusCode);
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
