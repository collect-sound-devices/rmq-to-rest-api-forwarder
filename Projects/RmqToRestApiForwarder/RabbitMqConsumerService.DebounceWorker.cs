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

        private async Task RunAsync()
        {
            var reader = _queue.Reader;
            PendingMessage? carry = null;

            while (!_stopToken.IsCancellationRequested)
            {
                PendingMessage head;
                if (carry != null)
                {
                    head = carry;
                    carry = null;
                }
                else
                {
                    try
                    {
                        head = await reader.ReadAsync(_stopToken);
                    }
                    catch (OperationCanceledException)
                    {
                        break;
                    }
                }

                var last = head;

                while (reader.TryRead(out var next))
                {
                    if ((next.UpdateDate - last.UpdateDate) <= _window) // within the time window?
                    {
                        await _ignoreMessageAsync(last, _stopToken); // ignore previous last
                        last = next; // keep the most recent within the window
                        continue;
                    }

                    // Next is outside the window; keep it for next iteration
                    carry = next;
                    break;
                }

                // Process the chosen last message
                try
                {
                    await _processMessageAsync(last, _stopToken);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "[{Name}] Error while processing debounced message.", _name);
                    try
                    {
                        await _ignoreMessageAsync(last, _stopToken);
                    }
                    catch
                    {
                        /* ignored */
                    }
                }
            }
        }
    }
}