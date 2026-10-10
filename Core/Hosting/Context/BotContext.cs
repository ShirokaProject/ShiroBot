using System.Reflection;
using ShiroBot.Hosting.Files;
using ShiroBot.SDK.Adapter;
using ShiroBot.SDK.Core;
using ShiroBot.SDK.Models;
using ShiroBot.SDK.Plugin;
using ShiroBot.Hosting.Events;
using ShiroBot.Hosting.Logging;

namespace ShiroBot.Hosting.Context;

internal sealed class BotContext
{
    private IReadOnlyList<UserReference> _ownerList;
    private IReadOnlyList<UserReference> _adminList;
    private IRenderContext? _renderer;
    private readonly HostLogHub? _logHub;
    private readonly Lock _adapterLock = new();
    private AdapterRegistration[] _adapters = [];
    private sealed record AdapterRegistration(string Id, IBotAdapter Adapter, AdapterInstanceInfo Info);

    // Package, name and version come from the adapter's own [BotAdapter] metadata.
    private static AdapterRegistration CreateRegistration(IBotAdapter adapter, string id, string? instanceName)
    {
        var metadata = adapter.GetType().GetCustomAttribute<BotAdapterAttribute>(inherit: false);
        return new(id, adapter, new AdapterInstanceInfo(
            id, string.IsNullOrWhiteSpace(instanceName) ? id : instanceName, metadata?.Id ?? id,
            metadata?.Name ?? adapter.GetType().Name, metadata?.Version ?? string.Empty, adapter.Platform, metadata?.Protocol));
    }

    public BotContext(IBotAdapter? adapter, IReadOnlyList<string> ownerList, IReadOnlyList<string> adminList,
        IWebHostContext webHost, HostLogHub? logHub = null, TemporaryFileManager? temporaryFiles = null)
    {
        _logHub = logHub;
        TemporaryFiles = temporaryFiles;
        if (adapter is not null) _adapters = [CreateRegistration(adapter, adapter.Platform, null)];
        Channel = new SwitchableChannelService(this);
        User = new SwitchableUserService(this);
        ReplySubscriptions = new ReplySubscriptionManager();
        Message = new MessageContext(GetMessageRoute, UseMessageSource, UseInstance, ReplySubscriptions, "__host", _logHub);
        Updater = new UpdaterContext();
        WebHost = webHost;
        _ownerList = ownerList.Select(UserReference.Parse).ToArray();
        _adminList = adminList.Select(UserReference.Parse).ToArray();
    }

    public string Platform => CurrentRegistration?.Adapter.Platform ?? "none";
    public string? InstanceId => CurrentRegistration?.Id;
    public AdapterInstanceInfo? AdapterInstance => CurrentRegistration?.Info;
    // ReSharper disable once InconsistentlySynchronizedField
    public IReadOnlyList<AdapterInstanceInfo> GetAdapterInstances() => Volatile.Read(ref _adapters).Select(entry => entry.Info).ToArray();
    private IBotAdapter? CurrentAdapter => CurrentRegistration?.Adapter;
    // ReSharper disable once InconsistentlySynchronizedField
    public bool HasAdapter => Volatile.Read(ref _adapters).Length > 0;
    public IMessageContext Message { get; }
    public IChannelService Channel { get; }
    public IUserService User { get; }
    public IUpdater Updater { get; }
    public IWebHostContext WebHost { get; }
    internal TemporaryFileManager? TemporaryFiles { get; }

    public IReadOnlyList<UserReference> OwnerList => Volatile.Read(ref _ownerList);
    public IReadOnlyList<UserReference> AdminList => Volatile.Read(ref _adminList);

    public bool IsOwner(UserReference user) => OwnerList.Any(entry => entry.Matches(user));
    public bool IsOwner(string userId) => InstanceId is { } id && IsOwner(new UserReference(id, userId));

    /// <summary>Owner 始终拥有管理员权限，无需重复加入 admin_list；每次读取当前热重载的列表。</summary>
    public bool IsAdmin(UserReference user) => IsOwner(user) || AdminList.Any(entry => entry.Matches(user));
    public bool IsAdmin(string userId) => InstanceId is { } id && IsAdmin(new UserReference(id, userId));

    /// <summary>
    /// 由宿主渲染集成提供的服务。渲染集成未启用时为 null。
    /// </summary>
    public IRenderContext? Renderer => Volatile.Read(ref _renderer);

    internal ReplySubscriptionManager ReplySubscriptions { get; }

    internal IMessageContext CreatePluginMessageContext(string pluginName) =>
        new MessageContext(GetMessageRoute, UseMessageSource, UseInstance, ReplySubscriptions, pluginName, _logHub);

    internal TService? GetAdapterExtension<TService>() where TService : class
    {
        if (typeof(TService) == typeof(IMessageReactionService) && CurrentAdapter?.GetExtension<IMessageReactionService>() is not null)
            return new RoutedReactionService(this) as TService;
        if (typeof(TService) == typeof(IMessageInteractionService) && CurrentAdapter?.GetExtension<IMessageInteractionService>() is not null)
            return new RoutedInteractionService(this) as TService;
        return CurrentAdapter?.GetExtension<TService>();
    }
    private sealed class RoutedReactionService(BotContext context) : IMessageReactionService
    {
        public ReactionCapabilities GetReactionCapabilities(Channel channel) => context.CurrentAdapter?.GetExtension<IMessageReactionService>()?.GetReactionCapabilities(channel) ?? ReactionCapabilities.None;
        public async Task SetReactionAsync(MessageReference message, ReactionEmoji emoji, bool isAdd = true, CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(message);
            if (emoji is PlatformReactionEmoji custom && !string.Equals(custom.InstanceId, message.InstanceId, StringComparison.OrdinalIgnoreCase))
                throw new ArgumentException("Platform emoji belongs to a different instance.");
            using var scope = context.UseInstance(message.InstanceId);
            var service = context.CurrentAdapter?.GetExtension<IMessageReactionService>() ?? throw new NotSupportedException("Source instance does not implement reactions.");
            await service.SetReactionAsync(message, emoji, isAdd, cancellationToken).ConfigureAwait(false);
        }
    }
    private sealed class RoutedInteractionService(BotContext context) : IMessageInteractionService
    {
        public async Task AcknowledgeAsync(InteractionEvent interaction, CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(interaction);
            using var scope = context.UseInstance(interaction.InstanceId ?? throw new ArgumentException("Interaction has no source instance."));
            var service = context.CurrentAdapter?.GetExtension<IMessageInteractionService>() ?? throw new NotSupportedException("Source instance does not implement interaction acknowledgements.");
            await service.AcknowledgeAsync(interaction, cancellationToken).ConfigureAwait(false);
        }
    }

    // Only for messages built without an InstanceId (inbound events always carry one): the platform's sole instance.
    private IDisposable UseOnlyInstanceOf(string platform)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(platform);
        var matches = Volatile.Read(ref _adapters).Where(entry => string.Equals(
            entry.Adapter.Platform, platform, StringComparison.OrdinalIgnoreCase)).ToArray();
        if (matches.Length == 0)
            throw new InvalidOperationException($"Adapter platform '{platform}' is not loaded.");
        if (matches.Length > 1)
            throw new InvalidOperationException($"Adapter platform '{platform}' has multiple instances; set the message's InstanceId or select one with UseInstance(id).");
        return AdapterExecutionContext.Enter(matches[0].Id);
    }

    internal IDisposable UseInstance(string instanceId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(instanceId);
        if (!Volatile.Read(ref _adapters).Any(entry => string.Equals(entry.Id, instanceId, StringComparison.OrdinalIgnoreCase)))
            throw new InvalidOperationException($"Adapter instance '{instanceId}' is not loaded.");
        return AdapterExecutionContext.Enter(instanceId);
    }

    private IDisposable UseMessageSource(MessageEvent message) =>
        message.InstanceId is { } id ? UseInstance(id) : UseOnlyInstanceOf(message.Platform);

    internal void RegisterAdapter(IBotAdapter adapter, string? adapterId = null, string? instanceName = null)
    {
        adapterId ??= adapter.Platform;
        ArgumentException.ThrowIfNullOrWhiteSpace(adapterId);
        lock (_adapterLock)
        {
            var adapters = Volatile.Read(ref _adapters);
            if (adapters.Any(entry => ReferenceEquals(entry.Adapter, adapter))) return;
            if (adapters.Any(entry => string.Equals(entry.Id, adapterId, StringComparison.OrdinalIgnoreCase)))
                throw new InvalidOperationException($"Adapter instance '{adapterId}' is already loaded.");
            Volatile.Write(ref _adapters, [.. adapters, CreateRegistration(adapter, adapterId, instanceName)]);
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

    private (IMessageService Service, string Platform, string? InstanceId) GetMessageRoute()
    {
        // Capture a single registration so a concurrent default-instance change cannot mix service and identity.
        var registration = CurrentRegistration;
        return (registration?.Adapter.Message ?? NullMessageService.Instance,
            registration?.Adapter.Platform ?? "none", registration?.Id);
    }

    public void UpdateOwnerList(IReadOnlyList<string> ownerList)
    {
        Volatile.Write(ref _ownerList, ownerList.Select(UserReference.Parse).ToArray());
    }

    public void UpdateAdminList(IReadOnlyList<string> adminList)
    {
        Volatile.Write(ref _adminList, adminList.Select(UserReference.Parse).ToArray());
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

        public Task<IReadOnlyList<Channel>> GetChannelsAsync(CancellationToken cancellationToken = default) => Current.GetChannelsAsync(cancellationToken: cancellationToken);
        public Task<Channel?> GetChannelAsync(string channelId, CancellationToken cancellationToken = default) => Current.GetChannelAsync(channelId, cancellationToken: cancellationToken);
        public Task<IReadOnlyList<Member>> GetMembersAsync(string channelId, CancellationToken cancellationToken = default) => Current.GetMembersAsync(channelId, cancellationToken: cancellationToken);
        public Task<Member?> GetMemberAsync(string channelId, string userId, CancellationToken cancellationToken = default) => Current.GetMemberAsync(channelId, userId, cancellationToken: cancellationToken);
        public Task SetChannelNameAsync(string channelId, string name, CancellationToken cancellationToken = default) => Current.SetChannelNameAsync(channelId, name, cancellationToken: cancellationToken);
        public Task KickMemberAsync(string channelId, string userId, CancellationToken cancellationToken = default) => Current.KickMemberAsync(channelId, userId, cancellationToken: cancellationToken);
        public Task MuteMemberAsync(string channelId, string userId, TimeSpan duration, CancellationToken cancellationToken = default) => Current.MuteMemberAsync(channelId, userId, duration, cancellationToken: cancellationToken);
        public Task LeaveChannelAsync(string channelId, CancellationToken cancellationToken = default) => Current.LeaveChannelAsync(channelId, cancellationToken: cancellationToken);
    }

    private sealed class SwitchableUserService(BotContext context) : IUserService
    {
        private IUserService Current => context.CurrentAdapter?.User ?? NullUserService.Instance;

        public Task<User> GetSelfAsync(CancellationToken cancellationToken = default) => Current.GetSelfAsync(cancellationToken: cancellationToken);
        public Task<User?> GetUserAsync(string userId, CancellationToken cancellationToken = default) => Current.GetUserAsync(userId, cancellationToken: cancellationToken);
        public Task<IReadOnlyList<User>> GetFriendsAsync(CancellationToken cancellationToken = default) => Current.GetFriendsAsync(cancellationToken: cancellationToken);
        public Task AcceptFriendRequestAsync(string token, CancellationToken cancellationToken = default) => Current.AcceptFriendRequestAsync(token, cancellationToken);
        public Task RejectFriendRequestAsync(string token, string? reason = null, CancellationToken cancellationToken = default) => Current.RejectFriendRequestAsync(token, reason, cancellationToken);
    }
}
