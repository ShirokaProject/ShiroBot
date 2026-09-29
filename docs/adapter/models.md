# 适配不同 Model

适配器始终先实现 `IBotAdapter` 与通用 SDK 服务，再按平台加入 Model。通用模型用于跨平台插件；平台 Model 只承载通用模型无法表达的数据、事件和可选 API。

| 平台 | `IBotAdapter.Platform` | 内置 Model | 当前接入方式 |
| --- | --- | --- | --- |
| QQ | `qq` | `ShiroBot.Model.QQ` | 通用消息和服务照常映射；按需实现 `IQFriendApi`、`IQGroupApi`、`IQFileApi`、`IQSystemApi`、`IQMessageApi` 或 `IQOfficialMessageApi` |
| Discord | `discord` | `ShiroBot.Model.Discord` | 通用服务为主；`DiscordUser` 保留原生用户字段，当前没有 Discord 专有扩展接口 |
| Telegram | `telegram` | `ShiroBot.Model.Telegram` | 通用服务为主；`TelegramUser` 保留原生用户字段，当前没有 Telegram 专有扩展接口 |
| 其他平台 | 自定义稳定 ID | 通用 SDK | 从通用模型开始；需要插件共享自定义类型时使用共享契约程序集 |

## 常用 Model 字段

表中的“必填”对应类型构造函数或 C# 的 `required` 属性；“否”表示模型允许省略，并不代表协议一定不会提供。

### 通用消息 `ShiroBot.SDK.Models`

| 名称 | 类型 | 必填 | 描述 |
| --- | --- | --- | --- |
| `BotEvent.Platform` | `string` | 是 | 事件来源，与 `IBotAdapter.Platform` 一致 |
| `MessageEvent.MessageId` | `string` | 是 | 平台消息 ID 转成字符串后的值 |
| `MessageEvent.Channel` | `Channel` | 是 | 消息所在私聊、群、频道或话题 |
| `MessageEvent.Sender` | `User` | 是 | 发送者；`User.Id` 是字符串 |
| `MessageEvent.Segments` | `IReadOnlyList<MessageSegment>` | 是 | 按原顺序映射的消息段 |
| `MessageEvent.Member` | `Member?` | 否 | 群或频道中的成员信息 |
| `MessageEvent.Raw` | `object?` | 否 | 需要保留的平台原生对象 |

### QQ `ShiroBot.Model.QQ`

| 名称 | 类型 | 必填 | 描述 |
| --- | --- | --- | --- |
| `QIncomingMessage.PeerId` | `long` | 是 | QQ 会话目标 ID |
| `QIncomingMessage.MessageSeq` | `long` | 是 | QQ 原生消息序号 |
| `QIncomingMessage.SenderId` | `long` | 是 | 发送者 QQ ID |
| `QGroupMessage.Group` / `GroupMember` | `QGroup` / `QGroupMember` | 是 | 群消息对应的群和发送成员 |
| `QIncomingMessage.Segments` | `IReadOnlyList<QIncomingSegment>` | 否 | QQ 原生消息段；默认空列表 |
| `QIncomingMention.UserId` / `Name` | `long` / `string` | 是 | 原生 @ 的 QQ ID 和显示名 |
| `QOfficialMessageTarget.Scene` / `Id` | `QOfficialMessageScene` / `string` | 是 | QQ 官方发送场景和目标 ID；单聊、群聊使用 openid |

### Discord 与 Telegram 用户

| 名称 | 类型 | 必填 | 描述 |
| --- | --- | --- | --- |
| `DiscordUser.UserId` | `ulong` | 是 | Discord 原生用户 ID，映射到 `User.Id` 时转成字符串 |
| `DiscordUser.Username` | `string` | 是 | Discord 用户名；显示名可用 `DisplayName` |
| `TelegramUser.UserId` | `long` | 是 | Telegram 原生用户 ID，映射到 `User.Id` 时转成字符串 |
| `TelegramUser.FirstName` | `string` | 是 | Telegram 名字；显示名可用 `DisplayName` |

## 功能实现对照

下表的“适配器实现”描述开发适配器时应做什么。当前仓库提供 SDK 和 Model 契约，没有 Milky、OneBot、Discord 或 Telegram 协议适配器的实现代码，所以不能据此判断某个已安装适配器支持哪些方法。

| 功能 | Model / 接口 | 适配器实现 | 必需 |
| --- | --- | --- | --- |
| 通用消息收发与查询 | `IMessageService` | 映射 `MessageSegment`；按协议能力实现发送、删除、单条与历史查询、资源链接 | 服务属性必需；各方法按协议能力实现 |
| 群、频道与成员 | `IChannelService` | 映射 `Channel`、`Member`；按协议能力实现查询和管理 | 服务属性必需；各方法按协议能力实现 |
| 机器人与用户 | `IUserService` | 映射 `User`；按协议能力实现身份、好友与请求处理 | 服务属性必需；各方法按协议能力实现 |
| 事件 | `IEventService` | 声明 `EventReceived` 并上报 `BotEvent` | 必需 |
| QQ 好友操作 | `IQFriendApi` | 戳一戳、点赞、好友请求等 | 可选扩展 |
| QQ 群管理 | `IQGroupApi` | 禁言、公告、精华、表情回应、群请求等 | 可选扩展 |
| QQ 文件 | `IQFileApi` | 上传、下载链接和群文件目录操作 | 可选扩展 |
| QQ 资料与账号 | `IQSystemApi` | 用户、好友、群成员资料及账号设置 | 可选扩展 |
| QQ 原生消息 | `IQMessageApi` | 原生段收发、历史消息、撤回、已读等 | 可选扩展 |
| QQ 官方消息 | `IQOfficialMessageApi` | Markdown 与按钮能力探测、发送和互动回应；按需实现文本与 Ark 发送 | 可选扩展 |
| QQ 官方私聊 | `IQOfficialDirectMessageApi` | 输入状态与流式回复 | 可选扩展 |
| Discord / Telegram 平台扩展 | 当前无专有扩展接口 | 通过通用服务实现；原生用户字段使用对应用户 Model | 无额外接口 |

通用接口的方法清单见[实现服务接口](/adapter/services)，QQ 扩展的功能清单见[QQ Model](/plugin/qq-model)。扩展接口是否存在与某个方法是否真的可用是两回事：未实现的方法默认抛 `NotSupportedException`。

选择模板的 `--platform qq`、`discord` 或 `telegram` 后，会生成对应的 `RequiresShiroBotPackage` 声明。宿主内置这三个 Model，并在加载适配器前校验最低版本。自己创建项目时也应声明所依赖的 Model，例如 QQ：

```csharp
using ShiroBot.SDK.Core;

[assembly: RequiresShiroBotPackage("shirobot.model.qq", MinimumVersion = "0.9.3")]
```

适配器和插件应引用与宿主匹配的 Model 类型；发布包中不要附带另一份同名 Model DLL。当前宿主不支持在运行时额外安装第三方 Model 包。

## 通用层先做什么

1. 把平台会话映射为 `Channel`，消息和消息段映射为 `MessageEvent`、`MessageSegment`；发送后返回 `SentMessage`。
2. 用 `IUserService`、`IChannelService` 暴露平台能提供的用户、群或频道资料。
3. 把协议推送映射为通用 `BotEvent`，通过 `IEventService.EventReceived` 发布。
4. 对通用模型无法表达的内容，使用 `RawSegment`、`BotEvent.Raw` 或 `PlatformEvent`，再决定是否需要平台扩展接口。

通用 ID 是字符串；不要假定 QQ、Discord、Telegram 的 ID 类型相同。平台专有 API 可以要求自己的原生 ID 类型。

## QQ：原生数据与扩展 API

QQ Model 定义 `QIncomingMessage`、`QIncomingSegment`、`QEventPayload` 和多组 `IQ*Api` 接口。下面将 QQ 群消息映射成通用消息，同时把完整 QQ 消息保存在 `Raw` 中：

```csharp
using ShiroBot.Model.QQ;
using ShiroBot.SDK.Models;

static MessageEvent ToCommonMessage(QGroupMessage source, string selfId)
{
    var sender = new User(source.SenderId.ToString())
    {
        Name = source.GroupMember.Nickname
    };

    return new MessageEvent
    {
        Platform = "qq",
        SelfId = selfId,
        MessageId = source.MessageSeq.ToString(),
        Channel = Channel.Group(source.Group.GroupId.ToString()),
        Sender = sender,
        Member = new Member(sender) { Nick = source.GroupMember.Card },
        Segments = source.Segments.Select(MapSegment).ToArray(),
        Timestamp = source.Time,
        Raw = source
    };
}

static MessageSegment MapSegment(QIncomingSegment segment) => segment switch
{
    QIncomingText text => new TextSegment(text.Text),
    QIncomingMention mention => new MentionSegment(mention.UserId.ToString())
    {
        DisplayName = mention.Name
    },
    QIncomingMentionAll => new MentionAllSegment(),
    QIncomingImage image => new ImageSegment(image.TempUrl)
    {
        ResourceId = image.ResourceId,
        Summary = image.Summary
    },
    _ => new RawSegment("qq", segment.GetType().Name, segment)
};
```

发送时反向转换 `Channel` 和通用消息段；`IQMessageApi` 可单独提供 QQ 原生段收发。要让插件调用扩展，最简单的方式是让适配器类实现对应接口：`IBotAdapter.GetExtension<TService>()` 默认返回 `this as TService`。如果扩展由独立服务对象实现，则在适配器中重写 `GetExtension<TService>()`，返回该服务对象。只声明接口不够；未覆盖的方法会抛 `NotSupportedException`。完整的 QQ 扩展功能清单见[QQ Model](/plugin/qq-model)。

QQ 特有事件用 `PlatformEvent` 上报，`Kind` 取 `QEventKinds`，`Raw` 放对应的 `QEventPayload`。QQ 官方消息发送使用单独的 `IQOfficialMessageApi`，其 openid 不能当作普通数字 QQ ID 使用。

## Discord 与 Telegram：通用服务加原生用户字段

这两个 Model 当前只有用户记录类型，没有专有扩展接口。把平台 ID 转成通用字符串 ID，显示名等映射到 `User`；需要保留的原生对象可放在相关事件的 `Raw` 中。频道、话题和媒体仍通过通用 `Channel`、`MessageSegment` 与服务接口处理。

```csharp
using ShiroBot.Model.Discord;
using ShiroBot.Model.Telegram;
using ShiroBot.SDK.Models;

static User ToCommonUser(DiscordUser source) =>
    new(source.UserId.ToString())
    {
        Name = source.DisplayName,
        IsBot = source.IsBot
    };

static User ToCommonUser(TelegramUser source) =>
    new(source.UserId.ToString())
    {
        Name = source.DisplayName,
        IsBot = source.IsBot
    };
```

Discord 文本频道可映射为 `ChannelType.Group`，Thread 映射为 `Thread`；Telegram 私聊映射为 `Direct`，群聊映射为 `Group`，话题映射为 `Thread`。具体协议字段由适配器负责转换，Model 不会自动完成映射。

## 自定义平台契约

若通用 `Raw` 负载和服务不足以表达插件要调用的能力，可把扩展接口与负载类型放在独立的共享契约 DLL。例如程序集名为 `MyPlatform.Contracts` 时，在适配器的 `BotAdapterAttribute.SharedAssemblies` 和插件的 `BotPluginAttribute.SharedAssemblies` 中都写入该名称；共享 DLL 放在适配器入口 DLL 旁。宿主会先把它加载为共享程序集，让插件和适配器看到相同的类型。

不要把协议客户端内部 DTO 直接作为插件 API。若只需要标准消息与事件，无需自定义契约或 Model 包。
