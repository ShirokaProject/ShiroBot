using System.Threading.Channels;
using ShiroBot.Hosting.Context;
using ShiroBot.Hosting.Events;
using ShiroBot.SDK.Adapter;
using ShiroBot.SDK.Models;
using CH = ShiroBot.Terminal.ConsoleOutput;
using Channel = System.Threading.Channels.Channel;

namespace ShiroBot.Adapters;

internal sealed class AdapterEventBridge(HostEventDispatcher eventDispatcher)
{
    private const int DefaultQueueCapacity = 256;

    public IAsyncDisposable Bridge(
        string adapterId,
        string platform,
        IEventService eventService)
    {
        var subscription = new Subscription(
            adapterId,
            platform,
            eventDispatcher,
            DefaultQueueCapacity);
        var handler = subscription.EnqueueAsync;
        eventService.EventReceived += handler;
        subscription.SetUnsubscribe(() => eventService.EventReceived -= handler);
        return subscription;
    }

    private sealed class Subscription : IAsyncDisposable
    {
        private const int MaxConcurrentDispatches = 8;

        private readonly string _adapterId;
        private readonly Task _consumer;
        private readonly HostEventDispatcher _eventDispatcher;
        private readonly Channel<BotEvent> _events;
        private readonly string _platform;

        private readonly TaskCompletionSource _producersDrained =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        private readonly Lock _sync = new();
        private bool _accepting = true;
        private int _activeProducers;
        private Task? _disposeTask;
        private Action? _unsubscribe;

        public Subscription(
            string adapterId,
            string platform,
            HostEventDispatcher eventDispatcher,
            int capacity)
        {
            _adapterId = adapterId;
            _platform = platform;
            _eventDispatcher = eventDispatcher;
            _events = Channel.CreateBounded<BotEvent>(new BoundedChannelOptions(capacity)
            {
                FullMode = BoundedChannelFullMode.Wait,
                SingleReader = true,
                SingleWriter = false,
                AllowSynchronousContinuations = false
            });
            _consumer = ConsumeAsync();
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

        public void SetUnsubscribe(Action unsubscribe)
        {
            _unsubscribe = unsubscribe;
        }

        public Task EnqueueAsync(BotEvent botEvent)
        {
            lock (_sync)
            {
                if (!_accepting) return Task.CompletedTask;
                _activeProducers++;
            }

            return EnqueueCoreAsync(botEvent);
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
            var dispatches = new List<Task>(MaxConcurrentDispatches);
            await foreach (var botEvent in _events.Reader.ReadAllAsync().ConfigureAwait(false))
            {
                if (dispatches.Count == MaxConcurrentDispatches)
                {
                    var completed = await Task.WhenAny(dispatches).ConfigureAwait(false);
                    dispatches.Remove(completed);
                }

                dispatches.Add(DispatchAsync(botEvent));
            }

            await Task.WhenAll(dispatches).ConfigureAwait(false);
        }

        private async Task DispatchAsync(BotEvent botEvent)
        {
            try
            {
                // Routing identity belongs to the host, not adapter-supplied event data.
                botEvent = botEvent with { InstanceId = _adapterId, Platform = _platform };
                if (botEvent is MessageReactionEvent { Emoji: PlatformReactionEmoji emoji } reaction)
                    botEvent = reaction with { Emoji = emoji with { InstanceId = _adapterId } };
                using var _ = AdapterExecutionContext.Enter(_adapterId);
                await _eventDispatcher.PublishAsync(botEvent).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                CH.Error($"适配器事件分发失败: {botEvent.GetType().Name} - {ex.Message}");
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