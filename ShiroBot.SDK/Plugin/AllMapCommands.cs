using ShiroBot.SDK.Models;

namespace ShiroBot.SDK.Plugin;

/// <summary>
/// 同时注册到群聊与私聊两个命令路由器的便捷入口。
/// </summary>
public sealed class AllMapCommands(
    CommandRouter<MessageEvent> groupCommands,
    CommandRouter<MessageEvent> directCommands)
{
    public void Map(string prefix, Func<MessageEvent, Task> handler) =>
        MapPrefix(prefix, handler);

    public void MapExact(string command, Func<MessageEvent, Task> handler)
    {
        groupCommands.MapExact(command, handler);
        directCommands.MapExact(command, handler);
    }

    public void MapPrefix(string prefix, Func<MessageEvent, Task> handler)
    {
        groupCommands.MapPrefix(prefix, handler);
        directCommands.MapPrefix(prefix, handler);
    }

    public void MapAll(Func<MessageEvent, Task> handler)
    {
        groupCommands.MapAll(handler);
        directCommands.MapAll(handler);
    }

    public void MapWhen(Func<MessageEvent, bool> predicate, Func<MessageEvent, Task> handler)
    {
        ArgumentNullException.ThrowIfNull(predicate);
        groupCommands.MapWhen(predicate, handler);
        directCommands.MapWhen(predicate, handler);
    }
}
