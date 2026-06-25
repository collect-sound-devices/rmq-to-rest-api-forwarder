using NLog;
using NLog.Extensions.Logging;
using RmqToRestApiForwarder;
using RmqToRestApiForwarder.Utils;
using LogLevel = Microsoft.Extensions.Logging.LogLevel;

var builder = Host.CreateDefaultBuilder(args)
    .ConfigureLogging((context, logging) =>
    {
        logging.ClearProviders();
        logging.SetMinimumLevel(LogLevel.Trace);

        LogManager.Setup()
            .LoadConfigurationFromSection(context.Configuration.GetSection("NLog"));

        logging.AddNLog();
    })
    .ConfigureServices((context, services) =>
    {
        var config = context.Configuration;

        services.Configure<RabbitMqServerSettings>(config.GetSection("RabbitMQ:Service"));
        services.Configure<RabbitMqMessageDeliverySettings>(config.GetSection("RabbitMQ:MessageDelivery"));
        services.Configure<ApiBaseUrlSettings>(config.GetSection("ApiBaseUrl"));
        services.Configure<GitHubCodespaceSettings>(config.GetSection("GitHubCodespace"));

        services.AddSingleton<IVersionProvider, VersionProvider>();
        services.AddHostedService<VersionStartupLogger>();

        services.AddHttpClient();
        services.AddSingleton<GitHubCodespaceAwaker>();
        services.AddHostedService<RabbitMqConsumerService>();
    });

await builder.Build().RunAsync();
