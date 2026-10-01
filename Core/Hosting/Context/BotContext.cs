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
    private AdapterRegistration[] _adapters = [];
    private sealed record AdapterRegistration(string Id, IBotAdapter Adapter);

    public BotContext(IBotAdapter? adapter, IReadOnlyList<string> ownerList, IReadOnlyList<string> adminList, IWebHostContext webHost)
    {
        if (adapter is not null) _adapters = [new(adapter.Platform, adapter)];
        Channel = new SwitchableChannelService(this);
        User = new SwitchableUserService(this);
        ReplySubscriptions = new ReplySubscriptionManager();
        Message = new MessageContext(GetMessageService, () => Platform, () => AdapterId, UseMessageSource, ReplySubscriptions, "__host");
        Updater = new UpdaterContext();
        WebHost = webHost;
        _ownerList = ownerList;
        _adminList = adminList;
    }

    public string Platform => CurrentRegistration?.Adapter.Platform ?? "none";
    public string? AdapterId => CurrentRegistration?.Id;
    private IBotAdapter? CurrentAdapter => CurrentRegistration?.Adapter;
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
        new MessageContext(GetMessageService, () => Platform, () => AdapterId, UseMessageSource, ReplySubscriptions, pluginName);

    internal TService? GetAdapterExtension<TService>() where TService : class =>
        CurrentAdapter?.GetExtension<TService>();

    internal IDisposable UsePlatform(string platform)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(platform);
        var matches = Volatile.Read(ref _adapters).Where(entry => string.Equals(
            entry.Adapter.Platform, platform, StringComparison.OrdinalIgnoreCase)).ToArray();
        if (matches.Length == 0)
            throw new InvalidOperationException($"Adapter platform '{platform}' is not loaded.");
        if (matches.Length > 1)
            throw new InvalidOperationException($"Adapter platform '{platform}' has multiple instances; select one with UseAdapter(id).");
        return AdapterExecutionContext.Enter(matches[0].Id);
    }

    internal IDisposable UseAdapter(string adapterId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(adapterId);
        if (!Volatile.Read(ref _adapters).Any(entry => string.Equals(entry.Id, adapterId, StringComparison.OrdinalIgnoreCase)))
            throw new InvalidOperationException($"Adapter instance '{adapterId}' is not loaded.");
        return AdapterExecutionContext.Enter(adapterId);
    }

    private IDisposable UseMessageSource(MessageEvent message) =>
        message.AdapterId is { } id ? UseAdapter(id) : UsePlatform(message.Platform);

    internal void RegisterAdapter(IBotAdapter adapter, string? adapterId = null)
    {
        adapterId ??= adapter.Platform;
        ArgumentException.ThrowIfNullOrWhiteSpace(adapterId);
        lock (_adapterLock)
        {
            var adapters = Volatile.Read(ref _adapters);
            if (adapters.Any(entry => ReferenceEquals(entry.Adapter, adapter))) return;
            if (adapters.Any(entry => string.Equals(entry.Id, adapterId, StringComparison.OrdinalIgnoreCase)))
                throw new InvalidOperationException($"Adapter instance '{adapterId}' is already loaded.");
            Volatile.Write(ref _adapters, [.. adapters, new AdapterRegistration(adapterId, adapter)]);
        }
    }

    internal void UnregisterAdapter(IBotAdapter adapter)
    {
        lock (_adapterLock)
        {
            var adapters = Volatile.Read(ref _adapters);
            Volatile.Write(ref _adapters, adapters.Where(entry => !ReferenceEquals(entry.Adapter, adapter)).ToArray());
        }
    }

    private AdapterRegistration? CurrentRegistration
    {
        get
        {
            var adapters = Volatile.Read(ref _adapters);
            var id = AdapterExecutionContext.Current;
            return id is null ? adapters.FirstOrDefault()
                : adapters.FirstOrDefault(entry => string.Equals(entry.Id, id, StringComparison.OrdinalIgnoreCase))
                  ?? throw new InvalidOperationException($"Adapter instance '{id}' is not loaded.");
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
