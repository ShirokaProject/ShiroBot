# Discord Model 特有能力

`ShiroBot.Model.Discord` 补充通用 SDK 无法表达的 Discord 数据与操作。插件先使用 `ShiroBot.SDK.Models`
处理普通消息，再按需探测 Discord 扩展接口。Model 由宿主提供，插件不需要引用适配器程序集。

本文对应宿主与 SDK 1.0.4（Discord Model ABI `1.1.0.0`）。只使用 `DiscordUser` 原有成员的组件可在更早的宿主上运行。

```csharp
[assembly: RequiresShiroBotPackage("shirobot.model.discord", MinimumVersion = "1.0.4")]
```

## 常用 Model 类型

| 名称 | 类型 | 描述 |
| --- | --- | --- |
| `DiscordUser` | 实体 | 用户；`DisplayName` 依次取昵称、全局显示名、用户名 |
| `DiscordGuild` / `DiscordChannel` / `DiscordRole` / `DiscordMember` | 实体 | 服务器、频道与子区、角色、成员（角色、超时、计算后的权限） |
| `DiscordPermissions` | 位标志 | 全部权限位，与 API 的 permissions 字符串对应 |
| `DiscordMessageContent` | 出站内容 | 正文、Embed、组件、附件、贴纸、投票、允许提及、消息标志、回复 |
| `DiscordComponent` | 组件基类 | 行、按钮、下拉菜单、文本输入；`DiscordRawComponent` 发送 Components V2 等未建模组件 |
| `DiscordMessage` | 入站资料 | 放在 `MessageEvent.Raw`：作者成员、编辑时间、类型、标志、子区、原始 JSON |
| `DiscordInteraction` | 交互详情 | 放在 `InteractionEvent.Raw`：命令名与选项、下拉值、弹窗输入、目标 ID、语言 |
| `DiscordApplicationCommand` | 命令定义 | 斜杠、用户、消息命令及选项、选项值、自动补全、权限和场景 |
| `DiscordEventPayload` | 事件负载基类 | 放在 `PlatformEvent.Raw`，均带原始 `Json` |

接口参数中的 ID 为 Snowflake 字符串，与 SDK 的 `User.Id`、`Channel.Id`、`MessageId` 相同；Model 实体中的 ID 为 `ulong`。

## 目前提供的功能

| 功能 | Model 接口 | 内容 |
| --- | --- | --- |
| 服务器管理 | `IDiscordGuildApi` | 服务器与频道查询；频道创建、修改、删除与权限覆盖；角色增删改；成员查询、搜索、角色、昵称、超时、移出；封禁；邀请；表情；子区 |
| 原生消息 | `IDiscordMessageApi` | 发送与编辑富消息；公告推送；置顶；批量删除；输入状态；Reaction 用户与清除；投票结束与投票者 |
| 交互与命令 | `IDiscordInteractionApi` | 回复、延迟、更新组件消息、弹窗、自动补全、编辑/删除原回复、后续消息；命令查询、整体注册、单个新增或更新、删除 |
| 机器人 | `IDiscordBotApi` | 自身资料、用户查询、在线状态与活动、修改用户名和头像 |
| 原始调用 | `IDiscordRawApi` | 任意 REST 路由（含文件上传与审计日志原因）、Gateway 指令、原始 Dispatch |

取得接口后，未实现的方法仍可能抛出 `NotSupportedException`。权限不足等失败由 Discord 返回，适配器抛出带状态码和错误内容的异常。

## 斜杠命令与交互

```csharp
using ShiroBot.Model.Discord;

var interactions = Context.GetAdapterExtension<IDiscordInteractionApi>()!;
await interactions.RegisterCommandsAsync([
    new DiscordApplicationCommand("roll")
    {
        Description = "掷骰子",
        Options = [new DiscordCommandOption(DiscordCommandOptionType.Integer, "sides", "面数") { MinValue = 2, MaxValue = 100 }]
    }
], guildId: "123456789012345678");   // 服务器命令立即生效；全局命令省略 guildId
```

命令、下拉菜单、弹窗和自动补全都产生 SDK `InteractionEvent`，详情在 `Raw` 的 `DiscordInteraction`：

```csharp
Events.Map<InteractionEvent>(async interaction =>
{
    if (interaction.Raw is not DiscordInteraction { CommandName: "roll" } discord) return;
    var sides = discord.Options.FirstOrDefault(o => o.Name == "sides")?.AsInteger() ?? 6;
    await interactions.RespondAsync(discord.InteractionId, new DiscordMessageContent
    {
        Content = $"🎲 {Random.Shared.Next(1, (int)sides + 1)}",
        Flags = DiscordMessageFlags.Ephemeral
    });
});
```

适配器默认自动确认交互（延迟回复），此时首次回复已被占用，只能用 `EditOriginalAsync` 和 `FollowupAsync`；
SDK 的 `ReplyToInteraction` 也走这一路径。需要临时可见回复、弹窗、自动补全或直接更新组件消息时，在适配器配置中
关闭自动确认（`AutoAcknowledges` 为 false），并在 3 秒内调用 `RespondAsync`、`DeferAsync`、`ShowModalAsync`、
`AutocompleteAsync` 或 `UpdateMessageAsync` 之一。

## 富消息

```csharp
var messages = Context.GetAdapterExtension<IDiscordMessageApi>()!;
await messages.SendAsync(message.Channel, new DiscordMessageContent
{
    Embeds = [new DiscordEmbed { Title = "构建完成", Color = 0x57F287, Fields = [new("耗时", "42s", Inline: true)] }],
    Components =
    [
        new DiscordActionRow([
            new DiscordButton { Label = "重新构建", CustomId = "rebuild", Style = DiscordButtonStyle.Success },
            new DiscordButton { Label = "日志", Url = "https://example.org/log", Style = DiscordButtonStyle.Link }
        ]),
        new DiscordActionRow([new DiscordSelectMenu(DiscordSelectKind.String, "env")
        {
            Options = [new("生产", "prod"), new("测试", "test")]
        }])
    ],
    AllowedMentions = DiscordAllowedMentions.None
});
```

## 入站内容与事件

`MessageEvent.Raw` 为 `DiscordMessage`。没有通用段的内容以 `RawSegment` 上报，`Platform` 为 `discord`：

| `RawSegment.Kind` | `Payload` |
| --- | --- |
| `DiscordSegmentKinds.Embed` | `DiscordEmbed`；发送时同样接受 |
| `DiscordSegmentKinds.Sticker` | `DiscordSticker` |
| `DiscordSegmentKinds.Poll` | `DiscordPoll` |
| `DiscordSegmentKinds.Components` | `DiscordComponents`（原始 JSON） |

Discord 特有的 Gateway 事件以 `PlatformEvent` 上报，`Kind` 为事件名小写；`DiscordEventKinds` 列出的事件 `Raw`
为对应负载（消息编辑、服务器、频道与子区、成员更新、角色、封禁、语音状态、输入、Reaction 清除、投票、邀请），
其他事件为 `JsonElement`。消息、删除、成员加入与离开、交互和 Reaction 仍映射为 SDK 事件。

```csharp
Events.MapPlatform(DiscordEventKinds.VoiceStateUpdate, evt =>
{
    if (evt.Raw is DiscordVoiceStateUpdated { ChannelId: null } left)
        BotLog.Info($"{left.UserId} 离开了语音");
    return Task.CompletedTask;
});
```

## 原始调用

```csharp
using System.Text.Json.Nodes;

var raw = Context.GetAdapterExtension<IDiscordRawApi>()!;
var log = await raw.CallAsync("GET", "guilds/123456789012345678/audit-logs?limit=10");
await raw.SendGatewayAsync(8, new JsonObject { ["guild_id"] = "123456789012345678", ["query"] = "", ["limit"] = 0 });
```
