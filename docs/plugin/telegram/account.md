# 账号操作（ITelegramAccountApi）

命名空间：`ShiroBot.Model.Telegram`。Telegram Model ABI：`1.1.0.0`。仅用户账号（MTProto 登录）实现；机器人适配器上为 null。

用户账号特有的操作，仅用户账号（MTProto 登录；机器人适配器不实现）。历史消息、完整成员列表、会话列表和好友 使用 SDK 通用服务（`GetHistoryMessagesAsync`、`GetMembersAsync`、`GetChannelsAsync`、`GetFriendsAsync`）。

```csharp
var api = Context.GetAdapterExtension<ITelegramAccountApi>();
if (api is null) return;
```

取得接口不代表每个方法都已实现；未实现的方法抛出 `NotSupportedException`，调用还受平台权限和参数约束。

## GetDialogsAsync

会话列表（对话），按最近活动排序。

```csharp
Task<IReadOnlyList<TelegramChat>> GetDialogsAsync(
    int limit = 100,
    CancellationToken cancellationToken = default
);
```

### 参数

| 参数 | C# 类型 | 默认值 | 说明 |
| --- | --- | --- | --- |
| `limit` | `int` | `100` | 返回数量上限。 |
| `cancellationToken` | `CancellationToken` | `default` | 取消令牌；默认不主动取消。 |

### 返回

`Task<IReadOnlyList<TelegramChat>>`。异步返回 `IReadOnlyList<TelegramChat>`。

## JoinChatAsync

通过邀请链接（`t.me/+…`）或公开用户名加入群组或频道。

```csharp
Task<TelegramChat> JoinChatAsync(
    string inviteLinkOrUsername,
    CancellationToken cancellationToken = default
);
```

### 参数

| 参数 | C# 类型 | 默认值 | 说明 |
| --- | --- | --- | --- |
| `inviteLinkOrUsername` | `string` | `必传` | 邀请链接（`t.me/+…`）或公开用户名。 |
| `cancellationToken` | `CancellationToken` | `default` | 取消令牌；默认不主动取消。 |

### 返回

`Task<TelegramChat>`。异步返回 `TelegramChat`。

## MarkAsReadAsync

把会话标记为已读；`maxMessageId` 为 null 时读到最新消息。

```csharp
Task MarkAsReadAsync(
    Channel channel,
    string? maxMessageId = null,
    CancellationToken cancellationToken = default
);
```

### 参数

| 参数 | C# 类型 | 默认值 | 说明 |
| --- | --- | --- | --- |
| `channel` | `Channel` | `必传` | 目标 SDK 频道；私聊为 `Channel.Direct(用户 ID)`。 |
| `maxMessageId` | `string?` | `null` | 读到的最后一条消息 ID；null 为最新。 |
| `cancellationToken` | `CancellationToken` | `default` | 取消令牌；默认不主动取消。 |

### 返回

`Task`。完成后无返回值。

## GetContactsAsync

通讯录联系人。

```csharp
Task<IReadOnlyList<TelegramUser>> GetContactsAsync(CancellationToken cancellationToken = default);
```

### 参数

| 参数 | C# 类型 | 默认值 | 说明 |
| --- | --- | --- | --- |
| `cancellationToken` | `CancellationToken` | `default` | 取消令牌；默认不主动取消。 |

### 返回

`Task<IReadOnlyList<TelegramUser>>`。异步返回 `IReadOnlyList<TelegramUser>`。

## PressCallbackButtonAsync

点击其他机器人消息上的回调按钮，返回机器人回答的提示文本（没有时为 null）。

```csharp
Task<string?> PressCallbackButtonAsync(
    Channel channel,
    string messageId,
    string callbackData,
    CancellationToken cancellationToken = default
);
```

### 参数

| 参数 | C# 类型 | 默认值 | 说明 |
| --- | --- | --- | --- |
| `channel` | `Channel` | `必传` | 目标 SDK 频道；私聊为 `Channel.Direct(用户 ID)`。 |
| `messageId` | `string` | `必传` | 消息 ID，与 SDK 的 `MessageId` 相同。 |
| `callbackData` | `string` | `必传` | 按钮的回调数据。 |
| `cancellationToken` | `CancellationToken` | `default` | 取消令牌；默认不主动取消。 |

### 返回

`Task<string?>`。异步返回 `string?`。

## 相关类型

见 [C# 类型参考](./types)。`required` 属性创建对象时必须填写；`?` 表示可空。
