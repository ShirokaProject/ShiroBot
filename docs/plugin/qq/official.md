# QQ 官方机器人

本节集中介绍 QQ 官方开放平台的消息、媒体、私聊和流式回复能力，以及 Markdown 与按钮的使用方式。相关契约都位于 `ShiroBot.Model.QQ`。

## 使用示例

[Markdown 与按钮](../qq-official)介绍类型化消息发送、Markdown、键盘按钮、Embed、Ark 和按钮互动事件。需要 QQ 专属参数时使用这些扩展；普通文本和跨平台富消息优先使用[通用富消息与互动](../rich-messages)。

## C# 接口参考

| 能力 | 接口 | 文档 |
| --- | --- | --- |
| 消息发送、Markdown、按钮与互动回应 | `IQOfficialMessageApi` | [消息接口](./official-message) |
| 媒体上传与发送 | `IQOfficialMediaApi` | [媒体接口](./official-media) |
| 私聊输入状态与创建流式回复 | `IQOfficialDirectMessageApi` | [私聊接口](./official-direct) |
| 更新、完成与释放流式回复 | `IQOfficialMessageStream` | [流式会话](./official-stream) |

前三个接口通过 `Context.GetAdapterExtension<T>()` 探测；返回 `null` 表示当前适配器没有暴露该能力。`IQOfficialMessageStream` 由 `IQOfficialDirectMessageApi.BeginStream` 创建，使用完应释放。

```csharp
var official = Context.GetAdapterExtension<IQOfficialMessageApi>();
if (official is null) return;
```

取得接口后，具体操作仍可能受适配器实现、开放平台权限和频控限制。未实现的方法可能抛出 `NotSupportedException`。

## 目标与类型

官方单聊和群聊使用开放平台的 **openid**，不能直接传入普通 QQ 号或群号；消息和事件的标识也应交回产生它们的适配器实例。

- [官方消息、Markdown、按钮与媒体类型](./types#官方消息、markdown、按钮与媒体)
- [完整 C# 类型参考](./types)
- [ABI 兼容性与版本](../api-compatibility)

好友、群管理、文件和 QQ 原生消息等其他能力，见 [C# 接口参考](../qq-reference)。
