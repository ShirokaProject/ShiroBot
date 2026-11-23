# 原生消息（IDiscordMessageApi）

命名空间：`ShiroBot.Model.Discord`。Discord Model ABI：`1.1.0.0`。两种账号都实现。

Discord 原生消息：Embed、组件、投票、附件、编辑、置顶、批量删除和 Reaction 管理。

```csharp
var api = Context.GetAdapterExtension<IDiscordMessageApi>();
if (api is null) return;
```

取得接口不代表每个方法都已实现；未实现的方法抛出 `NotSupportedException`，调用还受平台权限和参数约束。

## SendAsync

发送 Discord 原生富消息。

```csharp
Task<DiscordSentMessage> SendAsync(
    Channel channel,
    DiscordMessageContent content,
    CancellationToken cancellationToken = default
);
```

### 参数

| 参数 | C# 类型 | 默认值 | 说明 |
| --- | --- | --- | --- |
| `channel` | `Channel` | `必传` | 目标 SDK 频道；私聊为 `Channel.Direct(用户 ID)`，适配器自动打开私聊。 |
| `content` | `DiscordMessageContent` | `必传` | 要发送的内容。 |
| `cancellationToken` | `CancellationToken` | `default` | 取消令牌；默认不主动取消。 |

### 返回

`Task<DiscordSentMessage>`。异步返回 `DiscordSentMessage`。

## EditAsync

编辑消息；未设置的部分保持不变，空列表会清除对应内容，新附件追加到已有附件之后。

```csharp
Task<DiscordSentMessage> EditAsync(
    Channel channel,
    string messageId,
    DiscordMessageContent content,
    CancellationToken cancellationToken = default
);
```

### 参数

| 参数 | C# 类型 | 默认值 | 说明 |
| --- | --- | --- | --- |
| `channel` | `Channel` | `必传` | 目标 SDK 频道；私聊为 `Channel.Direct(用户 ID)`，适配器自动打开私聊。 |
| `messageId` | `string` | `必传` | 消息 ID，与 SDK 的 `MessageId` 相同。 |
| `content` | `DiscordMessageContent` | `必传` | 要发送的内容。 |
| `cancellationToken` | `CancellationToken` | `default` | 取消令牌；默认不主动取消。 |

### 返回

`Task<DiscordSentMessage>`。异步返回 `DiscordSentMessage`。

## CrosspostAsync

把公告频道的消息推送到关注它的频道。

```csharp
Task CrosspostAsync(
    Channel channel,
    string messageId,
    CancellationToken cancellationToken = default
);
```

### 参数

| 参数 | C# 类型 | 默认值 | 说明 |
| --- | --- | --- | --- |
| `channel` | `Channel` | `必传` | 目标 SDK 频道；私聊为 `Channel.Direct(用户 ID)`，适配器自动打开私聊。 |
| `messageId` | `string` | `必传` | 消息 ID，与 SDK 的 `MessageId` 相同。 |
| `cancellationToken` | `CancellationToken` | `default` | 取消令牌；默认不主动取消。 |

### 返回

`Task`。完成后无返回值。

## PinAsync

置顶消息。

```csharp
Task PinAsync(
    Channel channel,
    string messageId,
    string? reason = null,
    CancellationToken cancellationToken = default
);
```

### 参数

| 参数 | C# 类型 | 默认值 | 说明 |
| --- | --- | --- | --- |
| `channel` | `Channel` | `必传` | 目标 SDK 频道；私聊为 `Channel.Direct(用户 ID)`，适配器自动打开私聊。 |
| `messageId` | `string` | `必传` | 消息 ID，与 SDK 的 `MessageId` 相同。 |
| `reason` | `string?` | `null` | 写入服务器审计日志的原因；null 不写。 |
| `cancellationToken` | `CancellationToken` | `default` | 取消令牌；默认不主动取消。 |

### 返回

`Task`。完成后无返回值。

## UnpinAsync

取消置顶。

```csharp
Task UnpinAsync(
    Channel channel,
    string messageId,
    string? reason = null,
    CancellationToken cancellationToken = default
);
```

### 参数

| 参数 | C# 类型 | 默认值 | 说明 |
| --- | --- | --- | --- |
| `channel` | `Channel` | `必传` | 目标 SDK 频道；私聊为 `Channel.Direct(用户 ID)`，适配器自动打开私聊。 |
| `messageId` | `string` | `必传` | 消息 ID，与 SDK 的 `MessageId` 相同。 |
| `reason` | `string?` | `null` | 写入服务器审计日志的原因；null 不写。 |
| `cancellationToken` | `CancellationToken` | `default` | 取消令牌；默认不主动取消。 |

### 返回

`Task`。完成后无返回值。

## GetPinnedAsync

查询频道的全部置顶消息。

```csharp
Task<IReadOnlyList<DiscordMessage>> GetPinnedAsync(
    Channel channel,
    CancellationToken cancellationToken = default
);
```

### 参数

| 参数 | C# 类型 | 默认值 | 说明 |
| --- | --- | --- | --- |
| `channel` | `Channel` | `必传` | 目标 SDK 频道；私聊为 `Channel.Direct(用户 ID)`，适配器自动打开私聊。 |
| `cancellationToken` | `CancellationToken` | `default` | 取消令牌；默认不主动取消。 |

### 返回

`Task<IReadOnlyList<DiscordMessage>>`。异步返回 `IReadOnlyList<DiscordMessage>`。

## BulkDeleteAsync

批量删除 2–100 条 14 天内的消息。

```csharp
Task BulkDeleteAsync(
    Channel channel,
    IReadOnlyList<string> messageIds,
    string? reason = null,
    CancellationToken cancellationToken = default
);
```

### 参数

| 参数 | C# 类型 | 默认值 | 说明 |
| --- | --- | --- | --- |
| `channel` | `Channel` | `必传` | 目标 SDK 频道；私聊为 `Channel.Direct(用户 ID)`，适配器自动打开私聊。 |
| `messageIds` | `IReadOnlyList<string>` | `必传` | 消息 ID 列表。 |
| `reason` | `string?` | `null` | 写入服务器审计日志的原因；null 不写。 |
| `cancellationToken` | `CancellationToken` | `default` | 取消令牌；默认不主动取消。 |

### 返回

`Task`。完成后无返回值。

## GetReactionUsersAsync

查询对消息添加了某个表情的用户。

```csharp
Task<IReadOnlyList<DiscordUser>> GetReactionUsersAsync(
    Channel channel,
    string messageId,
    string emoji,
    int limit = 25,
    CancellationToken cancellationToken = default
);
```

### 参数

| 参数 | C# 类型 | 默认值 | 说明 |
| --- | --- | --- | --- |
| `channel` | `Channel` | `必传` | 目标 SDK 频道；私聊为 `Channel.Direct(用户 ID)`，适配器自动打开私聊。 |
| `messageId` | `string` | `必传` | 消息 ID，与 SDK 的 `MessageId` 相同。 |
| `emoji` | `string` | `必传` | Unicode 表情，或自定义表情的 name:id。 |
| `limit` | `int` | `25` | 返回数量上限。 |
| `cancellationToken` | `CancellationToken` | `default` | 取消令牌；默认不主动取消。 |

### 返回

`Task<IReadOnlyList<DiscordUser>>`。异步返回 `IReadOnlyList<DiscordUser>`。

## ClearReactionsAsync

清除消息的全部 Reaction，或只清除 `emoji`。

```csharp
Task ClearReactionsAsync(
    Channel channel,
    string messageId,
    string? emoji = null,
    CancellationToken cancellationToken = default
);
```

### 参数

| 参数 | C# 类型 | 默认值 | 说明 |
| --- | --- | --- | --- |
| `channel` | `Channel` | `必传` | 目标 SDK 频道；私聊为 `Channel.Direct(用户 ID)`，适配器自动打开私聊。 |
| `messageId` | `string` | `必传` | 消息 ID，与 SDK 的 `MessageId` 相同。 |
| `emoji` | `string?` | `null` | 表情：Unicode 字符或自定义表情的 name:id。 |
| `cancellationToken` | `CancellationToken` | `default` | 取消令牌；默认不主动取消。 |

### 返回

`Task`。完成后无返回值。

## EndPollAsync

提前结束机器人发起的投票。

```csharp
Task<DiscordSentMessage> EndPollAsync(
    Channel channel,
    string messageId,
    CancellationToken cancellationToken = default
);
```

### 参数

| 参数 | C# 类型 | 默认值 | 说明 |
| --- | --- | --- | --- |
| `channel` | `Channel` | `必传` | 目标 SDK 频道；私聊为 `Channel.Direct(用户 ID)`，适配器自动打开私聊。 |
| `messageId` | `string` | `必传` | 消息 ID，与 SDK 的 `MessageId` 相同。 |
| `cancellationToken` | `CancellationToken` | `default` | 取消令牌；默认不主动取消。 |

### 返回

`Task<DiscordSentMessage>`。异步返回 `DiscordSentMessage`。

## GetPollVotersAsync

查询投票中选择了某个选项的用户。

```csharp
Task<IReadOnlyList<DiscordUser>> GetPollVotersAsync(
    Channel channel,
    string messageId,
    int answerId,
    int limit = 25,
    CancellationToken cancellationToken = default
);
```

### 参数

| 参数 | C# 类型 | 默认值 | 说明 |
| --- | --- | --- | --- |
| `channel` | `Channel` | `必传` | 目标 SDK 频道；私聊为 `Channel.Direct(用户 ID)`，适配器自动打开私聊。 |
| `messageId` | `string` | `必传` | 消息 ID，与 SDK 的 `MessageId` 相同。 |
| `answerId` | `int` | `必传` | 投票选项 ID。 |
| `limit` | `int` | `25` | 返回数量上限。 |
| `cancellationToken` | `CancellationToken` | `default` | 取消令牌；默认不主动取消。 |

### 返回

`Task<IReadOnlyList<DiscordUser>>`。异步返回 `IReadOnlyList<DiscordUser>`。

## 相关类型

见 [C# 类型参考](./types)。`required` 属性创建对象时必须填写；`?` 表示可空。
