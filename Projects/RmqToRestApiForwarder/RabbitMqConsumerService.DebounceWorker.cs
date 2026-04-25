using System.Threading.Channels;

namespace RmqToRestApiForwarder;

public partial class RabbitMqConsumerService
{
    private sealed record PendingMessage(
        ulong DeliveryTag,
        byte[] Body,
        int Attempt,
        string? HttpMethod,
        string? UrlSuffix,
        DateTime UpdateDate
    );

    private void InitializeDebouncers((string render, string capture) names, CancellationToken cancellationToken)
    { 
        _renderDebouncer = CreateDebouncer(names.render, cancellationToken);
        _captureDebouncer = CreateDebouncer(names.capture, cancellationToken);
    }


    private DebounceWorker CreateDebouncer(string eventName, CancellationToken cancellationToken)
    {
        return new DebounceWorker(
            eventName,
            _volumeDebounceWindow,
            (msg, ct) => ProcessDebouncedMessageAsync(eventName, msg, ct),
            (msg, ct) => IgnoreDebouncedMessageAsync(eventName, msg, ct),
            _logger,
            cancellationToken);
    }

    private async Task ProcessDebouncedMessageAsync(string eventName, PendingMessage message, CancellationToken ct)
    {
        _logger.LogInformation(
            "Debouncing chosen {message.} message at {UpdateDate:o} to be PROCESSED",
            eventName,
            message.UpdateDate);
        await ProcessMessageAsync(message, ct);
    }

    private ValueTask IgnoreDebouncedMessageAsync(string eventName, PendingMessage message, CancellationToken ct)
    {
        _logger.LogInformation(
            "Debouncing chosen {EventName} message at {UpdateDate:o} to be IGNORED",
            eventName,
            message.UpdateDate);
        return ConsumerChannel.BasicAckAsync(message.DeliveryTag, false, ct);
    }
    private sealed class DebounceWorker
    {
        private readonly string _name;
        private readonly TimeSpan _window;
        private readonly Func<PendingMessage, CancellationToken, Task> _processMessageAsync;
        private readonly Func<PendingMessage, CancellationToken, ValueTask> _ignoreMessageAsync;
        private readonly ILogger _logger;

        private readonly Channel<PendingMessage> _queue =
            Channel.CreateUnbounded<PendingMessage>(new UnboundedChannelOptions
                { SingleReader = true, SingleWriter = false });

        private readonly CancellationToken _stopToken;

        public DebounceWorker(string name, TimeSpan window,
            Func<PendingMessage, CancellationToken, Task> processMessageAsync,
            Func<PendingMessage, CancellationToken, ValueTask> ignoreMessageAsync,
            ILogger logger,
            CancellationToken stopToken)
        {
            _name = name;
            _window = window;
            _processMessageAsync = processMessageAsync;
            _ignoreMessageAsync = ignoreMessageAsync;
            _logger = logger;
            _stopToken = stopToken;
            _ = Task.Run(RunAsync, stopToken);
        }

        public ValueTask EnqueueAsync(PendingMessage message)
        {
            return _queue.Writer.WriteAsync(message, _stopToken);
        }
        private static async Task<PendingMessage?> GetNextMessageAsync(
            PendingMessage? nextMessage,
            ChannelReader<PendingMessage> reader,
            CancellationToken cancellationToken)
        {
            if (nextMessage != null)
            {
                return nextMessage;
            }
            try
            {
                return await reader.ReadAsync(cancellationToken);
            }
            catch (OperationCanceledException)
            {
                return null; // Signal to break the loop
            }
        }

        private async Task<PendingMessage?> ProcessDebounceWindowAsync(
            PendingMessage currentMessage,
            ChannelReader<PendingMessage> reader)
        {
            var latestMessage = currentMessage;
            PendingMessage? nextMessage = null;
            while (reader.TryRead(out var next))
            {
                if ((next.UpdateDate - latestMessage.UpdateDate) <= _window) // within the time window?
                {
                    try
                    {
                        await _ignoreMessageAsync(latestMessage, _stopToken);
                    }
                    catch
                    {
                        // Ignored
                    }
                    
                    latestMessage = next; // keep the most recent within the window
                    continue;
                }
                // Next is outside the window; keep it for next iteration
                nextMessage = next;
                break;
            }
            return nextMessage;
        }
        private async Task ProcessMessageSafelyAsync(PendingMessage message)
        {
            try
            {
                await _processMessageAsync(message, _stopToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[{Name}] Error or stop while processing debounced message.", _name);
                try
                {
                    await _ignoreMessageAsync(message, _stopToken);
                }
                catch
                {
                    // Ignored
                }
            }
        }
        private async Task RunAsync()
        {
            var reader = _queue.Reader;
            PendingMessage? nextMessage = null;
            try
            {
                while (!_stopToken.IsCancellationRequested)
                {
                    var currentMessage = await GetNextMessageAsync(nextMessage, reader, _stopToken);
                    if (currentMessage == null)
                    {
                        break; // Exit the loop if cancellation is requested
                    }
                    // Process messages within the debounce window
                    nextMessage = await ProcessDebounceWindowAsync(currentMessage, reader);
                    // Process the chosen latest message
                    await ProcessMessageSafelyAsync(currentMessage);
                }
            }
            catch (OperationCanceledException)
            {
                // Gracefully exit on cancellation
            }
        }
    }
}