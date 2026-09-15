using ShiroBot.SDK.Adapter;
using ShiroBot.SDK.Core;
using ShiroBot.SDK.Models;
using ShiroBot.SDK.Plugin;
using ShiroBot.Hosting.Events;

namespace ShiroBot.Hosting.Context;

internal sealed class BotContext
{
    private IReadOnlyList<string> _ownerList;
    private IReadOnlyList<string> _adminList;
    private IRenderContext? _renderer;
    private readonly Lock _adapterLock = new();
    private IBotAdapter[] _adapters = [];

    public BotContext(IBotAdapter? adapter, IReadOnlyList<string> ownerList, IReadOnlyList<string> adminList, IWebHostContext webHost)
    {
        if (adapter is not null) _adapters = [adapter];
        Channel = new SwitchableChannelService(this);
        User = new SwitchableUserService(this);
        ReplySubscriptions = new ReplySubscriptionManager();
        Message = new MessageContext(GetMessageService, () => Platform, ReplySubscriptions, "__host");
        Updater = new UpdaterContext();
        WebHost = webHost;
        _ownerList = ownerList;
        _adminList = adminList;
    }

    public string Platform => CurrentAdapter?.Platform ?? "none";
    // ReSharper disable once InconsistentlySynchronizedField
    public bool HasAdapter => Volatile.Read(ref _adapters).Length > 0;
    public IMessageContext Message { get; }
    public IChannelService Channel { get; }
    public IUserService User { get; }
    public IUpdater Updater { get; }
    public IWebHostContext WebHost { get; }

    public IReadOnlyList<string> OwnerList => Volatile.Read(ref _ownerList);
    public IReadOnlyList<string> AdminList => Volatile.Read(ref _adminList);

    /// <summary>
    /// 由宿主渲染集成提供的服务。渲染集成未启用时为 null。
    /// </summary>
    public IRenderContext? Renderer => Volatile.Read(ref _renderer);

    internal ReplySubscriptionManager ReplySubscriptions { get; }

    internal IMessageContext CreatePluginMessageContext(string pluginName) =>
        new MessageContext(GetMessageService, () => Platform, ReplySubscriptions, pluginName);

    internal TService? GetAdapterExtension<TService>() where TService : class =>
        CurrentAdapter?.GetExtension<TService>();

    internal IDisposable UsePlatform(string platform)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(platform);
        var adapters = Volatile.Read(ref _adapters);
        if (!adapters.Any(adapter => string.Equals(
                adapter.Platform,
                platform,
                StringComparison.OrdinalIgnoreCase)))
        {
            throw new InvalidOperationException($"Adapter platform '{platform}' is not loaded.");
        }

        return AdapterExecutionContext.Enter(platform);
    }

    internal void RegisterAdapter(IBotAdapter adapter)
    {
        lock (_adapterLock)
        {
            var adapters = Volatile.Read(ref _adapters);
            if (adapters.Contains(adapter)) return;
            Volatile.Write(ref _adapters, [.. adapters, adapter]);
        }
    }

    internal void UnregisterAdapter(IBotAdapter adapter)
    {
        lock (_adapterLock)
        {
            var adapters = Volatile.Read(ref _adapters);
            Volatile.Write(ref _adapters, adapters.Where(candidate => !ReferenceEquals(candidate, adapter)).ToArray());
        }
    }

    private IBotAdapter? CurrentAdapter
    {
        get
        {
            // ReSharper disable once InconsistentlySynchronizedField
            var adapters = Volatile.Read(ref _adapters);
            var platform = AdapterExecutionContext.Current;
            return platform is null
                ? adapters.FirstOrDefault()
                : adapters.FirstOrDefault(adapter => string.Equals(adapter.Platform, platform, StringComparison.OrdinalIgnoreCase));
        }
    }

    private IMessageService GetMessageService() => CurrentAdapter?.Message ?? NullMessageService.Instance;

    public void UpdateOwnerList(IReadOnlyList<string> ownerList)
    {
        Volatile.Write(ref _ownerList, ownerList);
    }

    public void UpdateAdminList(IReadOnlyList<string> adminList)
    {
        Volatile.Write(ref _adminList, adminList);
    }

    public void AttachRenderer(IRenderContext renderer)
    {
        Volatile.Write(ref _renderer, renderer);
    }

    private sealed class NullMessageService : IMessageService
    {
        public static NullMessageService Instance { get; } = new();
    }

    private sealed class NullChannelService : IChannelService
    {
        public static NullChannelService Instance { get; } = new();
    }

    private sealed class NullUserService : IUserService
    {
        public static NullUserService Instance { get; } = new();
    }

    private sealed class SwitchableChannelService(BotContext context) : IChannelService
    {
        private IChannelService Current => context.CurrentAdapter?.Channel ?? NullChannelService.Instance;

        public Task<IReadOnlyList<Channel>> GetChannelsAsync() => Current.GetChannelsAsync();
        public Task<Channel?> GetChannelAsync(string channelId) => Current.GetChannelAsync(channelId);
        public Task<IReadOnlyList<Member>> GetMembersAsync(string channelId) => Current.GetMembersAsync(channelId);
        public Task<Member?> GetMemberAsync(string channelId, string userId) => Current.GetMemberAsync(channelId, userId);
        public Task SetChannelNameAsync(string channelId, string name) => Current.SetChannelNameAsync(channelId, name);
        public Task KickMemberAsync(string channelId, string userId) => Current.KickMemberAsync(channelId, userId);
        public Task MuteMemberAsync(string channelId, string userId, TimeSpan duration) => Current.MuteMemberAsync(channelId, userId, duration);
        public Task LeaveChannelAsync(string channelId) => Current.LeaveChannelAsync(channelId);
    }

    private sealed class SwitchableUserService(BotContext context) : IUserService
    {
        private IUserService Current => context.CurrentAdapter?.User ?? NullUserService.Instance;

        public Task<User> GetSelfAsync() => Current.GetSelfAsync();
        public Task<User?> GetUserAsync(string userId) => Current.GetUserAsync(userId);
        public Task<IReadOnlyList<User>> GetFriendsAsync() => Current.GetFriendsAsync();
        public Task AcceptFriendRequestAsync(string token) => Current.AcceptFriendRequestAsync(token);
        public Task RejectFriendRequestAsync(string token, string? reason = null) => Current.RejectFriendRequestAsync(token, reason);
    }
}
