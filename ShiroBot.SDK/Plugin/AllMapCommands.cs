using ShiroBot.Model.Common;

namespace ShiroBot.SDK.Plugin;

public sealed class AllMapCommands(
    CommandRouter<GroupIncomingMessage> groupCommands,
    CommandRouter<FriendIncomingMessage> friendCommands)
{
    public void Map(string prefix, Func<IncomingMessage, Task> handler) =>
        MapPrefix(prefix, handler);

    public void MapExact(string command, Func<IncomingMessage, Task> handler)
    {
        groupCommands.MapExact(command, message => handler(message));
        friendCommands.MapExact(command, message => handler(message));
    }

    public void MapPrefix(string prefix, Func<IncomingMessage, Task> handler)
    {
        groupCommands.MapPrefix(prefix, message => handler(message));
        friendCommands.MapPrefix(prefix, message => handler(message));
    }

    public void MapAll(Func<IncomingMessage, Task> handler)
    {
        groupCommands.MapAll(message => handler(message));
        friendCommands.MapAll(message => handler(message));
    }

    public void MapWhen(Func<IncomingMessage, bool> predicate, Func<IncomingMessage, Task> handler)
    {
        ArgumentNullException.ThrowIfNull(predicate);
        groupCommands.MapWhen(message => predicate(message), message => handler(message));
        friendCommands.MapWhen(message => predicate(message), message => handler(message));
    }
}
