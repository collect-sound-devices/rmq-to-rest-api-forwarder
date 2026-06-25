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
    ) : IHasUpdateDate;

    private void InitializeDebouncers((string render, string capture) names, CancellationToken cancellationToken)
    { 
        _renderDebouncer = CreateDebouncer(names.render, cancellationToken);
        _captureDebouncer = CreateDebouncer(names.capture, cancellationToken);
    }


    private DebounceWorker<PendingMessage> CreateDebouncer(string eventName, CancellationToken cancellationToken)
    {
        return new DebounceWorker<PendingMessage>(
            _volumeDebounceWindow,
            (msg, ct) => ProcessDebouncedMessageAsync(eventName, msg, ct),
            (msg, ct) => IgnoreDebouncedMessageAsync(eventName, msg, ct),
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

    private async Task IgnoreDebouncedMessageAsync(string eventName, PendingMessage message, CancellationToken ct)
    {
        _logger.LogInformation(
            "Debouncing chosen {EventName} message at {UpdateDate:o} to be IGNORED",
            eventName,
            message.UpdateDate);
        await ConsumerChannel.BasicAckAsync(message.DeliveryTag, false, ct);
    }

    private sealed class DebounceWorker<TMessage> where TMessage : class, IHasUpdateDate
    {
        private readonly TimeSpan _debounceWindow;
        private readonly Func<TMessage, CancellationToken, Task> _forwardMessageAsync;
        private readonly Func<TMessage, CancellationToken, Task> _ignoreMessageAsync;

        private readonly Channel<TMessage> _queue =
            Channel.CreateUnbounded<TMessage>(new UnboundedChannelOptions
            { SingleReader = true, SingleWriter = false });

        private readonly CancellationToken _stopToken;
        private readonly Task _workerTask;

        public DebounceWorker(TimeSpan window,
            Func<TMessage, CancellationToken, Task> forwardMessageAsync,
            Func<TMessage, CancellationToken, Task> ignoreMessageAsync,
            CancellationToken stopToken)
        {
            _debounceWindow = window;
            _forwardMessageAsync = forwardMessageAsync;
            _ignoreMessageAsync = ignoreMessageAsync;
            _stopToken = stopToken;
            _workerTask = Task.Run(RunAsync, stopToken);
        }

        public void WaitForStop()
        {
            _workerTask.Wait(CancellationToken.None);
        }

        public ValueTask EnqueueAsync(TMessage message)
        {
            return _queue.Writer.WriteAsync(message, _stopToken);
        }

        private async Task<(TMessage messageToForward, TMessage? firstMessageAfterWindow)> ChooseMessageToForwardAsync(
            TMessage firstMessageInWindow,
            ChannelReader<TMessage> reader)
        {
            var messageToForward = firstMessageInWindow;
            TMessage? firstMessageAfterWindow = null;
            while (reader.TryRead(out var candidateMessage))
            {
                if ((candidateMessage.UpdateDate - messageToForward.UpdateDate) <= _debounceWindow) // within the time window?
                {
                    try
                    {
                        await _ignoreMessageAsync(messageToForward, _stopToken);
                    }
                    catch
                    {
                        // Ignored
                    }

                    messageToForward = candidateMessage; // keep the most recent within the window
                    continue;
                }

                // The candidateMessage is now outside the window; keep it for next iteration
                firstMessageAfterWindow = candidateMessage;
                break;
            }

            return (messageToForward, firstMessageAfterWindow);
        }

        // ReSharper disable CognitiveComplexity
        private async Task RunAsync()
        {
            var reader = _queue.Reader;
            TMessage? firstMessageAfterWindow = null;
            try
            {
                while (!_stopToken.IsCancellationRequested)
                {
                    TMessage firstMessageInWindow;
                    if (firstMessageAfterWindow != null)
                    {
                        firstMessageInWindow = firstMessageAfterWindow;
                    }
                    else
                    {
                        try
                        {
                            firstMessageInWindow = await reader.ReadAsync(_stopToken);
                        }
                        catch (OperationCanceledException)
                        {
                            break; // Exit the loop if cancellation is requested
                        }
                    }

                    (var messageToForward, firstMessageAfterWindow) =
                        await ChooseMessageToForwardAsync(firstMessageInWindow, reader);

                    // Process the chosen latest message
                    try
                    {
                        await _forwardMessageAsync(messageToForward, _stopToken);
                    }
                    catch
                    {
                        // Ignored
                    }
                }
            }
            catch (OperationCanceledException)
            {
                // Just exit on cancellation
            }
        }
        // ReSharper restore CognitiveComplexity
    }


}