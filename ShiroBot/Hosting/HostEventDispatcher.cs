using ShiroBot.Core;
using ShiroBot.Model.Common;
using ShiroBot.SDK.Plugin;

namespace ShiroBot.Hosting;

internal sealed class HostEventDispatcher(
    Lock pluginLifecycleLock,
    ReplySubscriptionManager replySubscriptions,
    HostRuntimeState runtimeState,
    HostLogHub logHub)
{
    private static readonly IReadOnlyDictionary<Type, BotEventSubscriptions> EventSubscriptions =
        CreateEventSubscriptions();

    private readonly Dictionary<string, List<LoadedPluginHandle>> _groupMessageExactHandlers = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, List<LoadedPluginHandle>> _friendMessageExactHandlers = new(StringComparer.OrdinalIgnoreCase);
    private readonly List<LoadedPluginHandle> _groupMessageBroadcastHandlers = [];
    private readonly List<(string Prefix, LoadedPluginHandle Plugin)> _groupMessagePrefixHandlers = [];
    private readonly List<LoadedPluginHandle> _friendMessageBroadcastHandlers = [];
    private readonly List<(string Prefix, LoadedPluginHandle Plugin)> _friendMessagePrefixHandlers = [];
    private readonly Dictionary<Type, List<LoadedPluginHandle>> _eventHandlers = [];
    private readonly TaskCompletionSource _initialPluginsReady = new(
        TaskCreationOptions.RunContinuationsAsynchronously);

    public void RegisterPlugin(LoadedPluginHandle pluginHandle)
    {
        lock (pluginLifecycleLock)
        {
            RegisterMessageRoutes(
                pluginHandle,
                BotEventSubscriptions.GroupMessage,
                pluginHandle.GroupMessageRoutes,
                pluginHandle.HandlesGroupMessagesViaBroadcast,
                _groupMessageBroadcastHandlers,
                _groupMessageExactHandlers,
                _groupMessagePrefixHandlers);

            RegisterMessageRoutes(
                pluginHandle,
                BotEventSubscriptions.FriendMessage,
                pluginHandle.FriendMessageRoutes,
                pluginHandle.HandlesFriendMessagesViaBroadcast,
                _friendMessageBroadcastHandlers,
                _friendMessageExactHandlers,
                _friendMessagePrefixHandlers);

            RegisterEventHandlers(pluginHandle);
        }
    }

    public void UnregisterPlugin(LoadedPluginHandle pluginHandle)
    {
        lock (pluginLifecycleLock)
        {
            _groupMessageBroadcastHandlers.Remove(pluginHandle);
            _friendMessageBroadcastHandlers.Remove(pluginHandle);
            RemoveRoutes(_groupMessageExactHandlers, _groupMessagePrefixHandlers, pluginHandle);
            RemoveRoutes(_friendMessageExactHandlers, _friendMessagePrefixHandlers, pluginHandle);

            foreach (var handlers in _eventHandlers.Values)
            {
                handlers.Remove(pluginHandle);
            }
        }
    }

    public async Task PublishAsync(Event message)
    {
        await _initialPluginsReady.Task.ConfigureAwait(false);
        await (message switch
        {
            GroupIncomingMessage groupMessage => PublishMessageAsync("群消息", groupMessage, MatchGroupMessageHandlers(groupMessage), handler => handler.OnEventAsync(groupMessage)),
            FriendIncomingMessage friendMessage => PublishMessageAsync("好友消息", friendMessage, MatchFriendMessageHandlers(friendMessage), handler => handler.OnEventAsync(friendMessage)),
            _ => DispatchAsync(GetEventDisplayName(message), message, Snapshot(message.GetType()), handler => handler.OnEventAsync(message))
        }).ConfigureAwait(false);
    }

    internal void MarkInitialPluginsReady() => _initialPluginsReady.TrySetResult();

    private async Task PublishMessageAsync<TMessage>(
        string eventName,
        TMessage message,
        IReadOnlyList<LoadedPluginHandle> handlers,
        Func<IBotEventSubscriber, Task> dispatch)
        where TMessage : IncomingMessage
    {
        runtimeState.RecordIncomingMessage();
        await replySubscriptions.PublishAsync(message).ConfigureAwait(false);
        await DispatchAsync(eventName, message, handlers, dispatch).ConfigureAwait(false);
    }

    private void RegisterEventHandlers(LoadedPluginHandle pluginHandle)
    {
        foreach (var (eventType, subscription) in EventSubscriptions)
        {
            if (subscription is BotEventSubscriptions.GroupMessage or BotEventSubscriptions.FriendMessage)
            {
                continue;
            }

            if ((pluginHandle.Subscriptions & subscription) == 0)
            {
                continue;
            }

            if (!_eventHandlers.TryGetValue(eventType, out var handlers))
            {
                handlers = [];
                _eventHandlers[eventType] = handlers;
            }

            handlers.Add(pluginHandle);
        }
    }

    private static void RegisterMessageRoutes(
        LoadedPluginHandle pluginHandle,
        BotEventSubscriptions subscription,
        IReadOnlyList<MessageRouteDescriptor> routes,
        bool requiresBroadcast,
        List<LoadedPluginHandle> broadcastBucket,
        Dictionary<string, List<LoadedPluginHandle>> exactBucket,
        List<(string Prefix, LoadedPluginHandle Plugin)> prefixBucket)
    {
        if ((pluginHandle.Subscriptions & subscription) == 0)
        {
            return;
        }

        if (requiresBroadcast)
        {
            broadcastBucket.Add(pluginHandle);
        }

        foreach (var route in routes)
        {
            switch (route.MatchType)
            {
                case MessageRouteMatchType.All:
                    broadcastBucket.Add(pluginHandle);
                    break;
                case MessageRouteMatchType.Exact:
                    RegisterExact(exactBucket, route.Pattern!, pluginHandle);
                    break;
                case MessageRouteMatchType.Prefix:
                    prefixBucket.Add((route.Pattern!, pluginHandle));
                    break;
            }
        }

        if (!requiresBroadcast && routes.Count == 0)
        {
            broadcastBucket.Add(pluginHandle);
        }
    }

    private static void RegisterExact(
        Dictionary<string, List<LoadedPluginHandle>> bucket,
        string key,
        LoadedPluginHandle pluginHandle)
    {
        if (!bucket.TryGetValue(key, out var handlers))
        {
            handlers = [];
            bucket[key] = handlers;
        }

        handlers.Add(pluginHandle);
    }

    private static void RemoveRoutes(
        Dictionary<string, List<LoadedPluginHandle>> exactBucket,
        List<(string Prefix, LoadedPluginHandle Plugin)> prefixBucket,
        LoadedPluginHandle pluginHandle)
    {
        foreach (var key in exactBucket.Keys.ToArray())
        {
            exactBucket[key].Remove(pluginHandle);
            if (exactBucket[key].Count == 0)
            {
                exactBucket.Remove(key);
            }
        }

        prefixBucket.RemoveAll(entry => ReferenceEquals(entry.Plugin, pluginHandle));
    }

    private LoadedPluginHandle[] Snapshot(Type eventType)
    {
        lock (pluginLifecycleLock)
        {
            return _eventHandlers.TryGetValue(eventType, out var handlers)
                ? handlers.ToArray()
                : [];
        }
    }

    private LoadedPluginHandle[] MatchGroupMessageHandlers(GroupIncomingMessage message)
    {
        lock (pluginLifecycleLock)
        {
            return MatchMessageHandlers(
                message.GetPlainText().Trim(),
                _groupMessageBroadcastHandlers,
                _groupMessageExactHandlers,
                _groupMessagePrefixHandlers);
        }
    }

    private LoadedPluginHandle[] MatchFriendMessageHandlers(FriendIncomingMessage message)
    {
        lock (pluginLifecycleLock)
        {
            return MatchMessageHandlers(
                message.GetPlainText().Trim(),
                _friendMessageBroadcastHandlers,
                _friendMessageExactHandlers,
                _friendMessagePrefixHandlers);
        }
    }

    private static LoadedPluginHandle[] MatchMessageHandlers(
        string text,
        List<LoadedPluginHandle> broadcastHandlers,
        Dictionary<string, List<LoadedPluginHandle>> exactHandlers,
        List<(string Prefix, LoadedPluginHandle Plugin)> prefixHandlers)
    {
        var matches = new HashSet<LoadedPluginHandle>(broadcastHandlers);

        if (exactHandlers.TryGetValue(text, out var exactMatchHandlers))
        {
            foreach (var handler in exactMatchHandlers)
            {
                matches.Add(handler);
            }
        }

        foreach (var (prefix, plugin) in prefixHandlers)
        {
            if (text.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            {
                matches.Add(plugin);
            }
        }

        return matches.ToArray();
    }

    private async Task DispatchAsync<TEvent>(
        string eventName,
        TEvent message,
        IReadOnlyList<LoadedPluginHandle> handlers,
        Func<IBotEventSubscriber, Task> dispatch)
    {
        var text = $"收到{eventName} {Describe(message)}";
        logHub.Record("system", "log", text);
        ConsoleHelper.Log(text);

        if (handlers.Count == 0)
        {
            return;
        }

        var groupId = ExtractGroupId(message);
        var tasks = handlers
            .Where(plugin => plugin.AllowsGroup(groupId))
            .Select(async plugin =>
            {
                try
                {
                    await plugin.DispatchAsync(dispatch);
                }
                catch (Exception ex)
                {
                    var errorMessage = $"插件功能执行失败: {plugin.Name} - {ex.Message}";
                    logHub.Record(plugin.Name, "error", errorMessage);
                    ConsoleHelper.Error(errorMessage);
                    runtimeState.RecordEvent(errorMessage, "error");
                }
            });

        await Task.WhenAll(tasks);
    }

    private static long? ExtractGroupId<TEvent>(TEvent evt)
    {
        return evt switch
        {
            GroupIncomingMessage message => message.Group.GroupId,
            GroupJoinRequestEvent e => e.GroupId,
            GroupInvitedJoinRequestEvent e => e.GroupId,
            GroupInvitationEvent e => e.GroupId,
            GroupAdminChangeEvent e => e.GroupId,
            GroupEssenceMessageChangeEvent e => e.GroupId,
            GroupMemberIncreaseEvent e => e.GroupId,
            GroupMemberDecreaseEvent e => e.GroupId,
            GroupNameChangeEvent e => e.GroupId,
            GroupMessageReactionEvent e => e.GroupId,
            GroupMuteEvent e => e.GroupId,
            GroupWholeMuteEvent e => e.GroupId,
            GroupNudgeEvent e => e.GroupId,
            GroupFileUploadEvent e => e.GroupId,
            MessageRecallEvent { MessageScene: MessageRecallEventMessageScene.Group } e => e.PeerId,
            _ => null
        };
    }

    private static string Describe<TEvent>(TEvent evt)
    {
        return evt switch
        {
            GroupIncomingMessage message => $"{message.Group.GroupName}({message.Group.GroupId}) {message.SenderId}发送: {GetMessageSegments(message.Segments)}",
            FriendIncomingMessage message => $"{message.Friend.Nickname}({message.SenderId})发送: {GetMessageSegments(message.Segments)}",
            GroupJoinRequestEvent e => $"用户 {e.InitiatorId} 申请加入群 {e.GroupId}: {e.Comment}",
            GroupInvitedJoinRequestEvent e => $"用户 {e.InitiatorId} 邀请 {e.TargetUserId} 加入群 {e.GroupId}",
            GroupInvitationEvent e => $"用户 {e.InitiatorId} 邀请机器人加入群 {e.GroupId}",
            FriendRequestEvent e => $"用户 {e.InitiatorId} 发来好友请求: {e.Comment}",
            FriendNudgeEvent e => $"好友 {e.UserId} 发来戳一戳",
            FriendFileUploadEvent e => $"好友 {e.UserId} 上传文件: {e.FileName} ({e.FileSize} bytes)",
            GroupAdminChangeEvent e => $"{e.GroupId} 群内用户 {e.UserId}" + (e.IsSet ? " 被设为管理员" : " 被取消管理员"),
            GroupEssenceMessageChangeEvent e => $"{e.GroupId} 群消息 {e.MessageSeq}" + (e.IsSet ? " 被设为精华" : " 被取消精华"),
            GroupMemberIncreaseEvent e => $"用户 {e.UserId} 加入群 {e.GroupId}",
            GroupMemberDecreaseEvent e => $"用户 {e.UserId} 离开群 {e.GroupId}",
            GroupNameChangeEvent e => $"群 {e.GroupId} 改名为: {e.NewGroupName}",
            GroupMessageReactionEvent e => $"用户 {e.UserId} 对群 {e.GroupId} 的消息 {e.MessageSeq}" + (e.IsAdd ? $" 添加表情 {e.FaceId}" : $" 取消表情 {e.FaceId}"),
            GroupMuteEvent e => e.Duration == 0 ? $"群 {e.GroupId} 中用户 {e.UserId} 被解除禁言" : $"群 {e.GroupId} 中用户 {e.UserId} 被禁言 {e.Duration} 秒",
            GroupWholeMuteEvent e => e.IsMute ? $"群 {e.GroupId} 开启全体禁言" : $"群 {e.GroupId} 关闭全体禁言",
            GroupNudgeEvent e => $"群 {e.GroupId} 中 {e.SenderId} 戳了 {e.ReceiverId} 一下",
            GroupFileUploadEvent e => $"群 {e.GroupId} 中用户 {e.UserId} 上传文件: {e.FileName} ({e.FileSize} bytes)",
            MessageRecallEvent e => $"{GetMessageSceneName(e.MessageScene)} {e.PeerId} 中 {e.SenderId} 的消息被撤回",
            Event e => DescribeWithMetadata(e),
            _ => string.Empty
        };
    }

    private static string GetMessageSegments(IReadOnlyList<IncomingSegment> segments)
    {
        var parts = segments.Select(segment => segment switch
        {
            TextIncomingSegment text => text.Text,
            ImageIncomingSegment image => $"[图片: {image.TempUrl}]",
            VideoIncomingSegment video => $"[视频: {video.TempUrl}]",
            RecordIncomingSegment record => $"[语音: {record.TempUrl}]",
            FileIncomingSegment file => $"[文件: {file.FileName}]",
            MentionIncomingSegment mention => $"[@{mention.UserId}]",
            MentionAllIncomingSegment => "[@全体成员]",
            FaceIncomingSegment face => $"[表情: {face.FaceId}]",
            MarketFaceIncomingSegment face => $"[商城表情: {face.Summary} {face.Url}]",
            ReplyIncomingSegment reply => $"[回复: {reply.MessageSeq}]",
            ForwardIncomingSegment forward => $"[转发: {forward.Title}]",
            LightAppIncomingSegment app => $"[轻应用: {app.AppName}]",
            XmlIncomingSegment xml => $"[XML: {xml.ServiceId}]",
            _ => $"<{segment.GetType().Name}>"
        });

        return string.Concat(parts).Replace("\r", "\\r").Replace("\n", "\\n");
    }

    private static string GetMessageSceneName(MessageRecallEventMessageScene scene)
    {
        return scene switch
        {
            MessageRecallEventMessageScene.Friend => "好友会话",
            MessageRecallEventMessageScene.Group => "群",
            MessageRecallEventMessageScene.Temp => "临时会话",
            _ => scene.ToString()
        };
    }

    private static string GetEventDisplayName(Event evt)
    {
        return EventMetadataRegistry.TryGet(evt.GetType(), out var metadata)
            ? metadata.Description
            : evt.GetType().Name;
    }

    private static IReadOnlyDictionary<Type, BotEventSubscriptions> CreateEventSubscriptions()
    {
        var commonEventTypes = typeof(Event).Assembly
            .GetTypes()
            .Where(type => type is { IsClass: true, IsAbstract: false } && typeof(Event).IsAssignableFrom(type));

        var subscriptions = new Dictionary<Type, BotEventSubscriptions>();

        foreach (var eventType in commonEventTypes)
        {
            var subscription = eventType.Name switch
            {
                nameof(GroupIncomingMessage) => BotEventSubscriptions.GroupMessage,
                nameof(FriendIncomingMessage) => BotEventSubscriptions.FriendMessage,
                _ when Enum.TryParse<BotEventSubscriptions>(TrimEventSuffix(eventType.Name), out var parsed) => parsed,
                _ => BotEventSubscriptions.None
            };

            if (subscription != BotEventSubscriptions.None)
            {
                subscriptions[eventType] = subscription;
            }
        }

        return subscriptions;
    }

    private static string TrimEventSuffix(string typeName) =>
        typeName.EndsWith("Event", StringComparison.Ordinal)
            ? typeName[..^"Event".Length]
            : typeName;

    private static string DescribeWithMetadata(Event evt)
    {
        if (!EventMetadataRegistry.TryGet(evt.GetType(), out var metadata))
        {
            return evt.GetType().Name;
        }

        var parts = metadata.Fields
            .Where(field => field.PropertyName is not nameof(BotOfflineEvent.Time) and not nameof(BotOfflineEvent.SelfId))
            .Select(field =>
            {
                var property = evt.GetType().GetProperty(field.PropertyName);
                var value = property?.GetValue(evt);
                return $"{field.Description}: {FormatMetadataValue(value)}";
            })
            .ToArray();

        return parts.Length == 0 ? metadata.Description : string.Join("；", parts);
    }

    private static string FormatMetadataValue(object? value)
    {
        return value switch
        {
            null => "null",
            string text => text.Replace("\r", "\\r").Replace("\n", "\\n"),
            IReadOnlyList<IncomingSegment> segments => GetMessageSegments(segments),
            System.Collections.IEnumerable enumerable when value is not string =>
                $"[{string.Join(", ", enumerable.Cast<object?>().Select(FormatMetadataValue))}]",
            _ => value.ToString() ?? string.Empty
        };
    }
}
