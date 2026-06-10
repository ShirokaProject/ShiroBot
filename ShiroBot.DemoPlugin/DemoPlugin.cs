using ShiroBot.Model.Common;
using ShiroBot.SDK.Abstractions;
using ShiroBot.SDK.Core;
using ShiroBot.SDK.Plugin;

namespace ShiroBot.DemoPlugin;

public sealed class DemoPlugin : PluginBase
{
    public override string Name => "DemoPlugin";
    public override BotComponentMetadata Metadata { get; } = new()
    {
        Name = "标准示例插件",
        Version = "1.0.0",
        Description = "这是一个 ShiroBot 插件开发的标准示例，展示了插件的基本结构和功能。",
        IsPluginSingleFile = false
    };

    protected override async Task LoadAsync()
    {
        var config = Context.Config.Load<DemoPluginConfig>();
        if (config.SendStartupHelloToOwner)
        {
            foreach (var id in Context.OwnerList)
            {
                // await Context.Message.SendPrivateMessageAsync(id, "ShiroBot已启动！");
            }
        }
        FriendCommands.MapExact("#help", HandleFriendHelpAsync);
        FriendCommands.MapExact("#ping", HandleFriendPingAsync);
        FriendCommands.MapPrefix("#echo", HandleFriendEchoAsync);
        FriendCommands.MapPrefix("#server", async message =>
        {
            await Context.Message.ReplyAsync(message, $"当前服务器时间: {DateTime.Now}");
            BotLog.Error("这是一个错误日志示例");
        });
        GroupCommands.MapExact("#help", HandleGroupHelpAsync);
        GroupCommands.MapExact("#ping", HandleGroupPingAsync);
        GroupCommands.MapPrefix("#echo", HandleGroupEchoAsync);
        GroupCommands.MapMention(HandleGroupMentionAsync);
        GroupCommands.MapMentionAll(HandleGroupMentionAllAsync);
        GroupCommands.MapReply(HandleGroupReplyAsync);

        Events.Map<MessageRecallEvent>(HandleMessageRecallAsync);
        Events.Map<FriendRequestEvent>(HandleFriendRequestAsync);
        Events.MapWhen<GroupMemberIncreaseEvent>(
            e => e.GroupId == 123,
            HandleSpecificGroupMemberIncreaseAsync);

        var loginInfo = await Context.System.GetLoginInfoAsync();
        BotLog.Info($"插件上下文已就绪: {loginInfo.Nickname}");
        BotLog.Info("标准示例插件已加载。");
    }

    protected override Task OnUnloadAsync()
    {
        BotLog.Info("标准示例插件已卸载。");
        return Task.CompletedTask;
    }

    private Task HandleFriendHelpAsync(FriendIncomingMessage message) =>
        Context.Message.ReplyAsync(message, "可用命令: #help, #ping, #echo <内容>");

    private Task HandleFriendPingAsync(FriendIncomingMessage message) =>
        Context.Message.ReplyAsync(message, "pong");

    private Task HandleFriendEchoAsync(FriendIncomingMessage message)
    {
        var text = message.GetPlainText();
        var content = text.Length <= "#echo".Length ? string.Empty : text["#echo".Length..].TrimStart();
        return Context.Message.ReplyAsync(message, $"你说了: {content}");
    }

    private Task HandleGroupHelpAsync(GroupIncomingMessage message) =>
        Context.Message.ReplyAsync(message, "可用命令: #help, #ping, #echo <内容>");

    private Task HandleGroupPingAsync(GroupIncomingMessage message) =>
        Context.Message.ReplyAsync(message, "pong");

    private Task HandleGroupEchoAsync(GroupIncomingMessage message)
    {
        var text = message.GetPlainText();
        var content = text.Length <= "#echo".Length ? string.Empty : text["#echo".Length..].TrimStart();
        return Context.Message.ReplyAsync(message, $"你说了: {content}");
    }

    private Task HandleGroupMentionAsync(GroupIncomingMessage message) =>
        Context.Message.ReplyAsync(message, $"你提到了 {message.Segments.OfType<MentionIncomingSegment>().Count()} 个用户。");

    private Task HandleGroupMentionAllAsync(GroupIncomingMessage message) =>
        Context.Message.ReplyAsync(message, "检测到 @全体成员。");

    private Task HandleGroupReplyAsync(GroupIncomingMessage message)
    {
        var reply = message.GetReply();
        return Context.Message.ReplyAsync(message, reply is null
            ? "这条消息没有回复段。"
            : $"你回复了 {reply.SenderId} 的消息: {reply.MessageSeq}");
    }

    private Task HandleMessageRecallAsync(MessageRecallEvent e)
    {
        BotLog.Info($"消息撤回事件: {e.MessageScene} {e.PeerId} #{e.MessageSeq}, sender={e.SenderId}, operator={e.OperatorId}");
        return Task.CompletedTask;
    }

    private Task HandleFriendRequestAsync(FriendRequestEvent e)
    {
        BotLog.Info($"好友请求事件: {e.InitiatorId} ({e.InitiatorUid}) comment={e.Comment}");
        return Task.CompletedTask;
    }

    private Task HandleSpecificGroupMemberIncreaseAsync(GroupMemberIncreaseEvent e)
    {
        BotLog.Info($"指定群成员增加事件: group={e.GroupId}, user={e.UserId}");
        return Task.CompletedTask;
    }
}
