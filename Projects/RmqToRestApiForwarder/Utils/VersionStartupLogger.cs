namespace RmqToRestApiForwarder.Utils;

internal sealed class VersionStartupLogger : IHostedService
{
    public VersionStartupLogger(IVersionProvider versionProvider,
        ILogger<VersionStartupLogger> logger)
    {
        logger.LogInformation(
            """
            "{AppName}" application starting up:
              Runtime: {Runtime}
              CodeVersion: {CodeVersion}
              LastCommitDate: {LastCommitDate}
            """,
            versionProvider.AppName,
            versionProvider.Runtime,
            versionProvider.CodeVersion,
            versionProvider.LastCommitDate);

    }

    public Task StartAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
