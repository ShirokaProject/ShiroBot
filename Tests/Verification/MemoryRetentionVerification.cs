using System.Net.WebSockets;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Threading.Channels;
using Channel = ShiroBot.SDK.Models.Channel;
using ShiroBot.Hosting.Events;
using ShiroBot.Hosting.Logging;
using ShiroBot.SDK.Models;
using ShiroBot.SDK.Plugin;

internal static class MemoryRetentionVerification
{
    public static async Task RunAsync()
    {
        var manager = new ReplySubscriptionManager();
        var (handle, payload) = Expiring(manager);
        for (int attempt = 0; attempt < 50 && payload.IsAlive; attempt++)
        {
            await Task.Delay(100);
            Collect();
        }
        if (payload.IsAlive) throw new Exception("Expired subscription still roots its callback payload while the caller retains the handle.");
        GC.KeepAlive(handle);

        var (removed, removedPayload) = Expiring(manager, TimeSpan.FromMinutes(10));
        manager.UnregisterOwner("fixture"); Collect();
        if (removedPayload.IsAlive) throw new Exception("Owner removal retained a callback.");
        GC.KeepAlive(removed);
        int calls = 0;
        manager.Subscribe("once", new("test", Channel.Group("123"), "1"), Timeout.InfiniteTimeSpan,
            _ => { calls++; return Task.CompletedTask; });
        var message = new MessageEvent { Platform="qq", InstanceId="test", Channel=Channel.Group("123"), Sender=new User("456"), MessageId="2", Segments=[new QuoteSegment("1")] };
        await manager.PublishAsync(message); await manager.PublishAsync(message);
        if(calls != 1) throw new Exception("Dispose-on-reply changed callback dispatch.");
        var temporaryManager = TemporaryManager(); Collect();
        if(temporaryManager.IsAlive) throw new Exception("Expiration timer roots a temporary manager.");

        var hub = new HostLogHub();
        using var cancellation = new CancellationTokenSource();
        using var socket = new SlowSocket();
        var stream = hub.StreamAsync(socket, "all", 0, cancellation.Token);
        var subscriptions = (System.Collections.IDictionary)typeof(HostLogHub).GetField("_subscribers", BindingFlags.Instance|BindingFlags.NonPublic)!.GetValue(hub)!;
        var channel = (Channel<HostLogHub.LogEntry>)subscriptions.Values.Cast<object>().Single();
        hub.Record("fixture", "info", "first");
        await socket.Blocked.Task.WaitAsync(TimeSpan.FromSeconds(5));
        for(int i=0;i<2500;i++)hub.Record("fixture", "info", i+new string('x',4096));
        if(channel.Reader.Count > 1000 || subscriptions.Count != 0)
            throw new Exception("Slow log client did not stop accepting logs at its bounded capacity.");
        if(hub.GetHistory("all", 10000).Length != 1000) throw new Exception("Recent log history changed.");
        cancellation.Cancel();
        try { await stream; } catch(OperationCanceledException) when(cancellation.IsCancellationRequested) { }
        Console.WriteLine("PASS expiration without incoming messages, expired/disposed handle releases payload, owner removal, single reply dispatch, weak timer lifetime, bounded slow log subscriber and recent history.");
    }
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static (IReplySubscription Handle, WeakReference Payload) Expiring(ReplySubscriptionManager manager, TimeSpan? duration=null)
    {
        var bytes=new byte[2*1024*1024];
        var weak=new WeakReference(bytes);
        var handle=manager.Subscribe("fixture",new("test",Channel.Group("123"),Guid.NewGuid().ToString()),duration??TimeSpan.Zero,
            _=> {GC.KeepAlive(bytes);return Task.CompletedTask;});
        return (handle,weak);
    }
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static WeakReference TemporaryManager()
    {
        var manager=new ReplySubscriptionManager(); Expiring(manager,TimeSpan.FromMinutes(10));return new WeakReference(manager);
    }
    private static void Collect(){GC.Collect();GC.WaitForPendingFinalizers();GC.Collect();}
    private sealed class SlowSocket:WebSocket
    {
        private int _sent;
        public TaskCompletionSource Blocked {get;}=new(TaskCreationOptions.RunContinuationsAsynchronously);
        public override WebSocketCloseStatus? CloseStatus=>null; public override string? CloseStatusDescription=>null;
        public override string? SubProtocol=>null;public override WebSocketState State=>WebSocketState.Open;
        public override void Abort(){}public override void Dispose(){}
        public override Task CloseAsync(WebSocketCloseStatus s,string? d,CancellationToken ct)=>Task.CompletedTask;
        public override Task CloseOutputAsync(WebSocketCloseStatus s,string? d,CancellationToken ct)=>Task.CompletedTask;
        public override Task<WebSocketReceiveResult> ReceiveAsync(ArraySegment<byte> b,CancellationToken ct)=>throw new NotSupportedException();
        public override async Task SendAsync(ArraySegment<byte> b,WebSocketMessageType t,bool e,CancellationToken ct)
        {if(Interlocked.Increment(ref _sent)>2){Blocked.TrySetResult();await Task.Delay(Timeout.Infinite,ct);}}
    }
}
