# QQ Model 特有能力

`ShiroBot.Model.QQ` 补充通用 SDK 无法表达的 QQ 数据与操作。插件先使用 `ShiroBot.SDK.Models` 处理普通消息，再按需探测 QQ 扩展接口。Milky、OneBot、QQ 官方适配器只需实现各自真正支持的接口。

以下示例使用 `ShiroBot.Model.QQ`；记录日志时还需要 `ShiroBot.SDK.Abstractions`。

全部方法、参数与返回类型见 [C# 接口参考](/plugin/qq-reference)，完整模型声明见 [C# 类型参考](/plugin/qq/types)。

## 常用 Model 类型

| 名称 | 类型 | 必填字段 | 描述 |
| --- | --- | --- | --- |
| `QFriend` | 实体 | `UserId`、`Nickname` | 好友资料；QQ ID 使用 `string` |
| `QGroup` | 实体 | `GroupId`、`GroupName` | 群资料 |
| `QGroupMember` | 实体 | `UserId`、`GroupId`、`Nickname` | 群成员资料和角色 |
| `QFriendMessage` / `QGroupMessage` / `QTempMessage` | 入站消息 | `PeerId`、`MessageId`、`SenderId`；好友消息还需 `Friend`，群消息还需 `Group`、`GroupMember` | 好友、群和临时会话消息 |
| `QIncomingSegment` / `QOutgoingSegment` | 消息段基类 | 由具体子类型决定 | QQ 原生入站、出站消息段 |
| `QEventPayload` | 事件负载基类 | 由具体子类型决定 | 放在 `PlatformEvent.Raw` 中 |
| `QOfficialMessageTarget` | 值对象 | `Scene`、`Id` | QQ 官方消息目标；单聊和群聊的 `Id` 为 openid |

适配器如何将这些类型映射为通用 `MessageEvent`、`User`、`Channel`，见[适配不同 Model](/adapter/models)。

## 目前提供的功能

以下列出 `ShiroBot.Model.QQ` 当前定义的功能。**Model 提供的是数据类型和可选接口，不代表每个 QQ 适配器都实现了全部功能。**插件通过 `Context.GetAdapterExtension<T>()` 获取扩展；返回 `null` 表示适配器未实现该接口。即使拿到了接口，具体方法仍可能抛出 `NotSupportedException`。

| 功能 | Model 接口 | 适配器实现内容 | 必需 |
| --- | --- | --- | --- |
| 好友 | `IQFriendApi` | 戳一戳、资料点赞、删除好友；查询、接受和拒绝好友请求 | 否，按协议能力实现 |
| 群管理 | `IQGroupApi` | 群名、头像、名片、头衔、管理员、禁言、踢人、退群；戳一戳、表情回应、公告、精华和入群请求 | 否，按协议能力实现 |
| 文件 | `IQFileApi` | 私聊和群文件上传、下载链接；群文件与文件夹查询、移动、重命名、删除和永久转存 | 否，按协议能力实现 |
| 账号与资料 | `IQSystemApi` | 用户、好友、置顶会话、登录账号和协议实现信息查询；头像、昵称、简介、置顶设置 | 否，按协议能力实现 |
| 原生消息 | `IQMessageApi` | QQ 原生段收发、单条与历史查询、撤回、已读、资源链接和合并转发 | 否，按协议能力实现 |
| QQ 官方消息 | `IQOfficialMessageApi` | Markdown 与按钮能力探测、发送和互动回应；另定义文本与 Ark 发送方法，旧适配器可能不支持 | 否，仅官方平台适配器按需实现 |
| QQ 官方私聊 | `IQOfficialDirectMessageApi` | 输入状态与流式回复；参见[官方消息](/plugin/qq-official) | 否，适配器单独实现 |

`IQSystemApi` 还定义了 Cookie、CSRF Token 和收藏表情 URL 查询；`IQGroupApi` 还支持查询和处理群通知、入群申请与邀请。表中是功能概览，具体方法以接口签名为准。

这些接口互相独立。实现了 `IQMessageApi` 不代表能发送官方 Markdown；插件必须单独探测 `IQOfficialMessageApi`。

## QQ 原生消息与段

`QIncomingMessage` 有好友、群和临时会话子类型。当前定义的消息段如下；具体适配器可能只支持其中一部分。

- **入站 `QIncomingSegment`**：文本、@ 用户、@ 全体、QQ 表情、引用、图片、语音、视频、文件、合并转发、商城表情、小程序或卡片、XML、Markdown。
- **出站 `QOutgoingSegment`**：文本、@ 用户、@ 全体、QQ 表情、引用、图片、语音、视频、小程序或卡片、合并转发。出站暂未定义文件、XML 或 Markdown 段；QQ 官方 Markdown 使用 `IQOfficialMessageApi`。

```csharp
var messageApi = Context.GetAdapterExtension<IQMessageApi>();
if (messageApi is not null)
{
    await messageApi.SendMessageAsync(
        QMessageScene.Group,
        groupId,
        [new QOutgoingText("QQ 原生消息")]);
}
```

`IQMessageApi` 使用字符串 ID 和 `MessageId`，Milky 的数值转换由适配器处理。QQ 官方开放平台使用 openid 的发送能力走单独的 `IQOfficialMessageApi`，参见[官方 Markdown 与按钮](/plugin/qq-official)。

## QQ 特有事件

QQ 特有事件以 `PlatformEvent` 上报：`Kind` 使用 `QEventKinds` 常量，`Raw` 使用对应的 `QEventPayload` 子类型。当前定义的事件种类包括：

- **好友**：戳一戳、文件上传。
- **群**：管理员变更、精华消息变更、群名变更、消息表情回应、成员禁言、全员禁言、戳一戳、文件上传、统一的入群申请（含邀请他人入群）、群解散。
- **其他**：会话置顶变更、QQ 官方消息按钮互动。

插件通过 `Events.MapPlatform(kind, handler)` 订阅，例如 `QGroupNudge`：

```csharp
Events.MapPlatform(QEventKinds.GroupNudge, evt =>
{
    if (evt.Raw is not QGroupNudge nudge) return Task.CompletedTask;
    BotLog.Info($"群 {nudge.GroupId} 发生戳一戳");
    return Task.CompletedTask;
});
```

上述模型只定义数据和调用方式；实际支持情况取决于当前 QQ 适配器。


通用 Markdown、基础按钮、卡片、互动事件及 Reaction 契约已加入 SDK，详见 [通用富消息与互动](./rich-messages.md)。
