# QQ Model 特有能力

`ShiroBot.Model.QQ` 补充通用 SDK 无法表达的 QQ 数据与操作。插件先使用 `ShiroBot.SDK.Models` 处理普通消息，再按需探测 QQ 扩展接口。Milky、OneBot、QQ 官方适配器只需实现各自真正支持的接口。

以下示例使用 `ShiroBot.Model.QQ`；记录日志时还需要 `ShiroBot.SDK.Abstractions`。

## 扩展接口

插件通过 `Context.GetAdapterExtension<T>()` 获取扩展；返回 `null` 表示适配器未实现。

| 接口 | 能力 |
| --- | --- |
| `IQFriendApi` | 戳一戳、点赞、好友请求与删除好友 |
| `IQGroupApi` | 群资料、成员管理、禁言、公告、精华、表情回应和群请求 |
| `IQFileApi` | 私聊与群文件上传、下载链接、目录管理 |
| `IQSystemApi` | QQ 资料、好友与群列表、协议实现信息、账号设置 |
| `IQMessageApi` | QQ 原生消息段、历史消息、撤回与已读 |
| `IQOfficialMessageApi` | QQ 官方开放平台 Markdown、按钮与互动回应 |

这些接口互相独立。实现了 `IQMessageApi` 不代表能发送官方 Markdown；插件必须单独探测 `IQOfficialMessageApi`。

## QQ 原生消息与段

`QIncomingMessage` 有好友、群和临时会话子类型。`QIncomingSegment` 包含文本、@、表情、引用、图片、语音、视频、文件、合并转发、小程序、XML 和 Markdown 等入站段。`QOutgoingSegment` 包含相应的可发送原生段；具体适配器可能只支持其中一部分。

```csharp
var messageApi = Context.GetAdapterExtension<IQMessageApi>();
if (messageApi is not null && long.TryParse(groupId, out var qqGroupId))
{
    await messageApi.SendMessageAsync(
        QMessageScene.Group,
        qqGroupId,
        [new QOutgoingText("QQ 原生消息")]);
}
```

`IQMessageApi` 使用 QQ 数字 ID 和消息序列号。QQ 官方开放平台使用 openid 的发送能力走单独的 `IQOfficialMessageApi`，参见[官方 Markdown 与按钮](/plugin/qq-official)。

## QQ 特有事件

QQ 特有事件以 `PlatformEvent` 上报：`Kind` 使用 `QEventKinds` 常量，`Raw` 使用对应的 `QEventPayload` 子类型。例如 `QGroupNudge`、`QGroupMessageReaction`、`QFriendRequestReceived`。插件通过 `Events.MapPlatform(kind, handler)` 订阅。

```csharp
Events.MapPlatform(QEventKinds.GroupNudge, evt =>
{
    if (evt.Raw is not QGroupNudge nudge) return Task.CompletedTask;
    BotLog.Info($"群 {nudge.GroupId} 发生戳一戳");
    return Task.CompletedTask;
});
```

上述模型只定义数据和调用方式；实际支持情况取决于当前 QQ 适配器。
