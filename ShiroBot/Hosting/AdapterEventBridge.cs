using System.Threading.Channels;
using ShiroBot.SDK.Adapter;
using ShiroBot.SDK.Models;
using CH = ShiroBot.Core.ConsoleHelper;

namespace ShiroBot.Hosting;

internal sealed class AdapterEventBridge(HostEventDispatcher eventDispatcher)
{
    private const int DefaultQueueCapacity = 256;

    public IAsyncDisposable Bridge(
        string platform,
        IEventService eventService,
        Func<MessageEvent, Task> directMessageHandler)
    {
        var subscription = new Subscription(
            platform,
            eventDispatcher,
            directMessageHandler,
            DefaultQueueCapacity);
        Func<BotEvent, Task> handler = subscription.EnqueueAsync;
        eventService.EventReceived += handler;
        subscription.SetUnsubscribe(() => eventService.EventReceived -= handler);
        return subscription;
    }

    private sealed class Subscription : IAsyncDisposable
    {
        private readonly string _platform;
        private readonly HostEventDispatcher _eventDispatcher;
        private readonly Func<MessageEvent, Task> _directMessageHandler;
        private readonly System.Threading.Channels.Channel<BotEvent> _events;
        private readonly Task _consumer;
        private readonly Lock _sync = new();
        private readonly TaskCompletionSource _producersDrained = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private Action? _unsubscribe;
        private Task? _disposeTask;
        private int _activeProducers;
        private bool _accepting = true;

        public Subscription(
            string platform,
            HostEventDispatcher eventDispatcher,
            Func<MessageEvent, Task> directMessageHandler,
            int capacity)
        {
            _platform = platform;
            _eventDispatcher = eventDispatcher;
            _directMessageHandler = directMessageHandler;
            _events = System.Threading.Channels.Channel.CreateBounded<BotEvent>(new BoundedChannelOptions(capacity)
            {
                FullMode = BoundedChannelFullMode.Wait,
                SingleReader = true,
                SingleWriter = false,
                AllowSynchronousContinuations = false
            });
            _consumer = ConsumeAsync();
        }

        public void SetUnsubscribe(Action unsubscribe) => _unsubscribe = unsubscribe;

        public Task EnqueueAsync(BotEvent botEvent)
        {
            lock (_sync)
            {
                if (!_accepting) return Task.CompletedTask;
                _activeProducers++;
            }

            return EnqueueCoreAsync(botEvent);
        }

        public ValueTask DisposeAsync()
        {
            Action? unsubscribe;
            Task disposeTask;
            lock (_sync)
            {
                if (_disposeTask is not null) return new ValueTask(_disposeTask);

                _accepting = false;
                unsubscribe = _unsubscribe;
                _unsubscribe = null;
                if (_activeProducers == 0) _producersDrained.TrySetResult();
                _disposeTask = DrainAndStopAsync();
                disposeTask = _disposeTask;
            }

            unsubscribe?.Invoke();
            return new ValueTask(disposeTask);
        }

        private async Task EnqueueCoreAsync(BotEvent botEvent)
        {
            try
            {
                await _events.Writer.WriteAsync(botEvent).ConfigureAwait(false);
            }
            finally
            {
                lock (_sync)
                {
                    _activeProducers--;
                    if (!_accepting && _activeProducers == 0) _producersDrained.TrySetResult();
                }
            }
        }

        private async Task ConsumeAsync()
        {
            await foreach (var botEvent in _events.Reader.ReadAllAsync().ConfigureAwait(false))
            {
                try
                {
                    using var _ = Context.AdapterExecutionContext.Enter(_platform);
                    await (botEvent is MessageEvent { IsDirect: true } directMessage
                        ? _directMessageHandler(directMessage)
                        : _eventDispatcher.PublishAsync(botEvent)).ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    CH.Error($"适配器事件分发失败: {botEvent.GetType().Name} - {ex.Message}");
                }
            }
        }

        private async Task DrainAndStopAsync()
        {
            await _producersDrained.Task.ConfigureAwait(false);
            _events.Writer.TryComplete();
            await _consumer.ConfigureAwait(false);
        }
    }
}
