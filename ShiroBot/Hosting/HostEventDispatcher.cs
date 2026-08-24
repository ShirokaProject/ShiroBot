using ShiroBot.Core;
using ShiroBot.SDK.Models;
using ShiroBot.SDK.Plugin;

namespace ShiroBot.Hosting;

internal sealed class HostEventDispatcher(
    Lock pluginLifecycleLock,
    ReplySubscriptionManager replySubscriptions,
    HostRuntimeState runtimeState,
    HostLogHub logHub)
{
    private readonly Dictionary<string, List<LoadedPluginHandle>> _groupMessageExactHandlers = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, List<LoadedPluginHandle>> _directMessageExactHandlers = new(StringComparer.OrdinalIgnoreCase);
    private readonly List<LoadedPluginHandle> _groupMessageBroadcastHandlers = [];
    private readonly List<(string Prefix, LoadedPluginHandle Plugin)> _groupMessagePrefixHandlers = [];
    private readonly List<LoadedPluginHandle> _directMessageBroadcastHandlers = [];
    private readonly List<(string Prefix, LoadedPluginHandle Plugin)> _directMessagePrefixHandlers = [];
    private readonly Dictionary<Type, List<LoadedPluginHandle>> _eventHandlers = [];
    private readonly TaskCompletionSource _initialPluginsReady = new(
        TaskCreationOptions.RunContinuationsAsynchronously);

    public void RegisterPlugin(LoadedPluginHandle pluginHandle)
    {
        lock (pluginLifecycleLock)
        {
            var subscribed = pluginHandle.SubscribesTo(typeof(MessageEvent));

            RegisterMessageRoutes(
                pluginHandle,
                subscribed,
                pluginHandle.GroupMessageRoutes,
                pluginHandle.HandlesGroupMessagesViaBroadcast,
                _groupMessageBroadcastHandlers,
                _groupMessageExactHandlers,
                _groupMessagePrefixHandlers);

            RegisterMessageRoutes(
                pluginHandle,
                subscribed,
                pluginHandle.DirectMessageRoutes,
                pluginHandle.HandlesDirectMessagesViaBroadcast,
                _directMessageBroadcastHandlers,
                _directMessageExactHandlers,
                _directMessagePrefixHandlers);

            RegisterEventHandlers(pluginHandle);
        }
    }

    public void UnregisterPlugin(LoadedPluginHandle pluginHandle)
    {
        lock (pluginLifecycleLock)
        {
            _groupMessageBroadcastHandlers.Remove(pluginHandle);
            _directMessageBroadcastHandlers.Remove(pluginHandle);
            RemoveRoutes(_groupMessageExactHandlers, _groupMessagePrefixHandlers, pluginHandle);
            RemoveRoutes(_directMessageExactHandlers, _directMessagePrefixHandlers, pluginHandle);

            foreach (var handlers in _eventHandlers.Values)
            {
                handlers.Remove(pluginHandle);
            }
        }
    }

    public async Task PublishAsync(BotEvent message)
    {
        await _initialPluginsReady.Task.ConfigureAwait(false);
        await (message switch
        {
            MessageEvent { IsDirect: false } groupMessage => PublishMessageAsync("群消息", groupMessage, MatchMessageHandlers(groupMessage, direct: false), handler => handler.OnEventAsync(groupMessage)),
            MessageEvent directMessage => PublishMessageAsync("私聊消息", directMessage, MatchMessageHandlers(directMessage, direct: true), handler => handler.OnEventAsync(directMessage)),
            _ => DispatchAsync(GetEventDisplayName(message), message, Snapshot(message.GetType()), handler => handler.OnEventAsync(message))
        }).ConfigureAwait(false);
    }

    internal void MarkInitialPluginsReady() => _initialPluginsReady.TrySetResult();

    private async Task PublishMessageAsync(
        string eventName,
        MessageEvent message,
        IReadOnlyList<LoadedPluginHandle> handlers,
        Func<IBotEventSubscriber, Task> dispatch)
    {
        runtimeState.RecordIncomingMessage();
        await replySubscriptions.PublishAsync(message).ConfigureAwait(false);
        await DispatchAsync(eventName, message, handlers, dispatch).ConfigureAwait(false);
    }

    private void RegisterEventHandlers(LoadedPluginHandle pluginHandle)
    {
        foreach (var eventType in pluginHandle.SubscribedEventTypes)
        {
            if (eventType == typeof(MessageEvent))
            {
                continue;
            }

            if (!typeof(BotEvent).IsAssignableFrom(eventType))
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
        bool subscribed,
        IReadOnlyList<MessageRouteDescriptor> routes,
        bool requiresBroadcast,
        List<LoadedPluginHandle> broadcastBucket,
        Dictionary<string, List<LoadedPluginHandle>> exactBucket,
        List<(string Prefix, LoadedPluginHandle Plugin)> prefixBucket)
    {
        if (!subscribed)
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
            return _eventHandlers
                .Where(entry => entry.Key.IsAssignableFrom(eventType))
                .SelectMany(entry => entry.Value)
                .Distinct()
                .ToArray();
        }
    }

    private LoadedPluginHandle[] MatchMessageHandlers(MessageEvent message, bool direct)
    {
        lock (pluginLifecycleLock)
        {
            return direct
                ? MatchMessageHandlers(
                    message.GetPlainText().Trim(),
                    _directMessageBroadcastHandlers,
                    _directMessageExactHandlers,
                    _directMessagePrefixHandlers)
                : MatchMessageHandlers(
                    message.GetPlainText().Trim(),
                    _groupMessageBroadcastHandlers,
                    _groupMessageExactHandlers,
                    _groupMessagePrefixHandlers);
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

    private async Task DispatchAsync(
        string eventName,
        BotEvent message,
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

    private static string? ExtractGroupId(BotEvent evt)
    {
        return evt switch
        {
            MessageEvent { IsDirect: false } message => message.Channel.Id,
            MemberJoinedEvent e => e.Channel.Id,
            MemberLeftEvent e => e.Channel.Id,
            MessageDeletedEvent { Channel.Type: not ChannelType.Direct } e => e.Channel.Id,
            PlatformEvent { Channel: { Type: not ChannelType.Direct } channel } => channel.Id,
            _ => null
        };
    }

    private static string Describe(BotEvent evt)
    {
        return evt switch
        {
            MessageEvent { IsDirect: false } message =>
                $"{message.Channel.Name}({message.Channel.Id}) {message.Sender.Id}发送: {GetMessageSegments(message.Segments)}",
            MessageEvent message =>
                $"{message.Sender.Name}({message.Sender.Id})发送: {GetMessageSegments(message.Segments)}",
            MessageDeletedEvent e => $"{e.Channel.Id} 中消息 {e.MessageId} 被撤回",
            MemberJoinedEvent e => $"用户 {e.UserId} 加入 {e.Channel.Id}",
            MemberLeftEvent e => $"用户 {e.UserId} 离开 {e.Channel.Id}",
            FriendRequestEvent e => $"用户 {e.UserId} 发来好友请求: {e.Comment}",
            GuildInviteEvent e => $"用户 {e.InviterId} 邀请机器人加入 {e.GuildId}",
            BotOfflineEvent e => $"机器人离线: {e.Reason}",
            PlatformEvent e => $"[{e.Platform}:{e.Kind}]" + (e.Channel is null ? string.Empty : $" @{e.Channel.Id}"),
            _ => evt.GetType().Name
        };
    }

    private static string GetMessageSegments(IReadOnlyList<MessageSegment> segments)
    {
        var parts = segments.Select(segment => segment switch
        {
            TextSegment text => text.Text,
            ImageSegment image => $"[图片: {image.Uri}]",
            VideoSegment video => $"[视频: {video.Uri}]",
            AudioSegment audio => $"[语音: {audio.Uri}]",
            FileSegment file => $"[文件: {file.FileName ?? file.Uri}]",
            MentionSegment mention => $"[@{mention.DisplayName ?? mention.UserId}]",
            MentionAllSegment => "[@全体成员]",
            EmojiSegment emoji => $"[表情: {emoji.Name ?? emoji.Id}]",
            QuoteSegment quote => $"[回复: {quote.MessageId}]",
            RawSegment raw => $"[{raw.Platform}:{raw.Kind}]",
            _ => $"<{segment.GetType().Name}>"
        });

        return string.Concat(parts).Replace("\r", "\\r").Replace("\n", "\\n");
    }

    private static string GetEventDisplayName(BotEvent evt)
    {
        return evt switch
        {
            MessageDeletedEvent => "消息撤回",
            MemberJoinedEvent => "成员加入",
            MemberLeftEvent => "成员离开",
            FriendRequestEvent => "好友请求",
            GuildInviteEvent => "群邀请",
            BotOfflineEvent => "机器人离线",
            PlatformEvent e => $"平台事件({e.Kind})",
            _ => evt.GetType().Name
        };
    }
}
