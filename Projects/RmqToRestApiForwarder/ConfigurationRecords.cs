namespace RmqToRestApiForwarder;

// Bound options classes must public read-write properties
// ReSharper disable AutoPropertyCanBeMadeGetOnly.Global
public record RabbitMqServerSettings
{
    public string HostName { get; init; } = "localhost";
    public string QueueName { get; init; } = "sdr_queue";
    public string UserName { get; init; } = "guest";
    public string Password { get; init; } = "guest";
    public int Port { get; init; } = 5671;
    public int NetworkRecoveryIntervalInSeconds { get; init; } = 3;
}

public record RabbitMqMessageDeliverySettings
{
    public int MaxRetryAttempts { get; init; } = 5;
    public int RetryDelayInSeconds { get; init; } = 10;
    // Debouncing interval for VolumeRenderChanged/VolumeCaptureChanged events (milliseconds)
    public int VolumeChangeEventDebouncingWindowInMilliseconds { get; init; } = 400;
}

public record ApiBaseUrlSettings
{
    public string Target { get; init; } = "Azure";
    public string Codespace { get; init; } = string.Empty;
    public string Azure { get; init; } = string.Empty;
    public string Local { get; init; } = string.Empty;
}

public record GitHubCodespaceSettings
{
    public string StartUrl { get; init; } = string.Empty;
    public string CodespaceName { get; init; } = string.Empty;
    public string Token { get; init; } = string.Empty;
    public int TimeoutSeconds { get; init; } = 30;
}
// ReSharper restore AutoPropertyCanBeMadeGetOnly.Global
