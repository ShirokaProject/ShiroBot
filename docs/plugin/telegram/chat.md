# 会话管理（ITelegramChatApi）

命名空间：`ShiroBot.Model.Telegram`。两种账号都实现。

Telegram 会话、成员、话题和邀请管理。改名（含话题名）、退出、踢出、禁言、删除单条消息和成员资料使用 SDK 通用服务 （`SetChannelNameAsync`、`LeaveChannelAsync`、`KickMemberAsync`、`MuteMemberAsync`、`DeleteMessageAsync`、`GetMemberAsync`）， 这里只放通用服务表达不了的部分。

```csharp
var api = Context.GetAdapterExtension<ITelegramChatApi>();
if (api is null) return;
```

取得接口不代表每个方法都已实现；未实现的方法抛出 `NotSupportedException`，调用还受平台权限和参数约束。

## GetChatAsync

查询会话资料，包括简介、默认权限、慢速模式和关联频道。

```csharp
Task<TelegramChat> GetChatAsync(string chatId, CancellationToken cancellationToken = default);
```

### 参数

| 参数 | C# 类型 | 默认值 | 说明 |
| --- | --- | --- | --- |
| `chatId` | `string` | `必传` | 会话：Bot API 格式的数字 ID、`@username` 或 SDK 频道 ID（`chatId:threadId`）。 |
| `cancellationToken` | `CancellationToken` | `default` | 取消令牌；默认不主动取消。 |

### 返回

`Task<TelegramChat>`。异步返回 `TelegramChat`。

## GetMemberCountAsync

查询会话成员数。

```csharp
Task<int> GetMemberCountAsync(string chatId, CancellationToken cancellationToken = default);
```

### 参数

| 参数 | C# 类型 | 默认值 | 说明 |
| --- | --- | --- | --- |
| `chatId` | `string` | `必传` | 会话：Bot API 格式的数字 ID、`@username` 或 SDK 频道 ID（`chatId:threadId`）。 |
| `cancellationToken` | `CancellationToken` | `default` | 取消令牌；默认不主动取消。 |

### 返回

`Task<int>`。异步返回 `int`。

## GetMemberAsync

查询指定成员的状态、权限和头衔。

```csharp
Task<TelegramChatMember> GetMemberAsync(
    string chatId,
    string userId,
    CancellationToken cancellationToken = default
);
```

### 参数

| 参数 | C# 类型 | 默认值 | 说明 |
| --- | --- | --- | --- |
| `chatId` | `string` | `必传` | 会话：Bot API 格式的数字 ID、`@username` 或 SDK 频道 ID（`chatId:threadId`）。 |
| `userId` | `string` | `必传` | 用户 ID，与 SDK 的 `User.Id` 相同。 |
| `cancellationToken` | `CancellationToken` | `default` | 取消令牌；默认不主动取消。 |

### 返回

`Task<TelegramChatMember>`。异步返回 `TelegramChatMember`。

## GetAdministratorsAsync

管理员列表（不含其他机器人）。Telegram 不提供完整成员列表。

```csharp
Task<IReadOnlyList<TelegramChatMember>> GetAdministratorsAsync(
    string chatId,
    CancellationToken cancellationToken = default
);
```

### 参数

| 参数 | C# 类型 | 默认值 | 说明 |
| --- | --- | --- | --- |
| `chatId` | `string` | `必传` | 会话：Bot API 格式的数字 ID、`@username` 或 SDK 频道 ID（`chatId:threadId`）。 |
| `cancellationToken` | `CancellationToken` | `default` | 取消令牌；默认不主动取消。 |

### 返回

`Task<IReadOnlyList<TelegramChatMember>>`。异步返回 `IReadOnlyList<TelegramChatMember>`。

## BanMemberAsync

封禁成员；`until` 为 null 或超过 366 天时为永久封禁。

```csharp
Task BanMemberAsync(
    string chatId,
    string userId,
    DateTimeOffset? until = null,
    bool revokeMessages = false,
    CancellationToken cancellationToken = default
);
```

### 参数

| 参数 | C# 类型 | 默认值 | 说明 |
| --- | --- | --- | --- |
| `chatId` | `string` | `必传` | 会话：Bot API 格式的数字 ID、`@username` 或 SDK 频道 ID（`chatId:threadId`）。 |
| `userId` | `string` | `必传` | 用户 ID，与 SDK 的 `User.Id` 相同。 |
| `until` | `DateTimeOffset?` | `null` | 结束时间；null 表示永久或解除。 |
| `revokeMessages` | `bool` | `false` | 同时删除该成员的消息。 |
| `cancellationToken` | `CancellationToken` | `default` | 取消令牌；默认不主动取消。 |

### 返回

`Task`。完成后无返回值。

## UnbanMemberAsync

解除封禁。`onlyIfBanned` 为 false 时，对仍在群内的成员调用会把他移出。

```csharp
Task UnbanMemberAsync(
    string chatId,
    string userId,
    bool onlyIfBanned = true,
    CancellationToken cancellationToken = default
);
```

### 参数

| 参数 | C# 类型 | 默认值 | 说明 |
| --- | --- | --- | --- |
| `chatId` | `string` | `必传` | 会话：Bot API 格式的数字 ID、`@username` 或 SDK 频道 ID（`chatId:threadId`）。 |
| `userId` | `string` | `必传` | 用户 ID，与 SDK 的 `User.Id` 相同。 |
| `onlyIfBanned` | `bool` | `true` | 只在已封禁时解封；false 时会移出仍在群内的成员。 |
| `cancellationToken` | `CancellationToken` | `default` | 取消令牌；默认不主动取消。 |

### 返回

`Task`。完成后无返回值。

## RestrictMemberAsync

限制成员；`until` 为 null 时永久有效。

```csharp
Task RestrictMemberAsync(
    string chatId,
    string userId,
    TelegramChatPermissions permissions,
    DateTimeOffset? until = null,
    CancellationToken cancellationToken = default
);
```

### 参数

| 参数 | C# 类型 | 默认值 | 说明 |
| --- | --- | --- | --- |
| `chatId` | `string` | `必传` | 会话：Bot API 格式的数字 ID、`@username` 或 SDK 频道 ID（`chatId:threadId`）。 |
| `userId` | `string` | `必传` | 用户 ID，与 SDK 的 `User.Id` 相同。 |
| `permissions` | `TelegramChatPermissions` | `必传` | 权限设置。 |
| `until` | `DateTimeOffset?` | `null` | 结束时间；null 表示永久或解除。 |
| `cancellationToken` | `CancellationToken` | `default` | 取消令牌；默认不主动取消。 |

### 返回

`Task`。完成后无返回值。

## PromoteMemberAsync

设置管理员权限；所有权限为 false 时撤销管理员。

```csharp
Task PromoteMemberAsync(
    string chatId,
    string userId,
    TelegramAdminRights rights,
    CancellationToken cancellationToken = default
);
```

### 参数

| 参数 | C# 类型 | 默认值 | 说明 |
| --- | --- | --- | --- |
| `chatId` | `string` | `必传` | 会话：Bot API 格式的数字 ID、`@username` 或 SDK 频道 ID（`chatId:threadId`）。 |
| `userId` | `string` | `必传` | 用户 ID，与 SDK 的 `User.Id` 相同。 |
| `rights` | `TelegramAdminRights` | `必传` | 管理员权限；全部为 false 时撤销管理员。 |
| `cancellationToken` | `CancellationToken` | `default` | 取消令牌；默认不主动取消。 |

### 返回

`Task`。完成后无返回值。

## SetAdministratorTitleAsync

设置机器人提升的管理员的头衔，0–16 个字符。

```csharp
Task SetAdministratorTitleAsync(
    string chatId,
    string userId,
    string title,
    CancellationToken cancellationToken = default
);
```

### 参数

| 参数 | C# 类型 | 默认值 | 说明 |
| --- | --- | --- | --- |
| `chatId` | `string` | `必传` | 会话：Bot API 格式的数字 ID、`@username` 或 SDK 频道 ID（`chatId:threadId`）。 |
| `userId` | `string` | `必传` | 用户 ID，与 SDK 的 `User.Id` 相同。 |
| `title` | `string` | `必传` | 标题。 |
| `cancellationToken` | `CancellationToken` | `default` | 取消令牌；默认不主动取消。 |

### 返回

`Task`。完成后无返回值。

## SetPermissionsAsync

设置所有成员的默认权限。

```csharp
Task SetPermissionsAsync(
    string chatId,
    TelegramChatPermissions permissions,
    CancellationToken cancellationToken = default
);
```

### 参数

| 参数 | C# 类型 | 默认值 | 说明 |
| --- | --- | --- | --- |
| `chatId` | `string` | `必传` | 会话：Bot API 格式的数字 ID、`@username` 或 SDK 频道 ID（`chatId:threadId`）。 |
| `permissions` | `TelegramChatPermissions` | `必传` | 权限设置。 |
| `cancellationToken` | `CancellationToken` | `default` | 取消令牌；默认不主动取消。 |

### 返回

`Task`。完成后无返回值。

## SetDescriptionAsync

修改群组或频道简介；null 清除。

```csharp
Task SetDescriptionAsync(
    string chatId,
    string? description,
    CancellationToken cancellationToken = default
);
```

### 参数

| 参数 | C# 类型 | 默认值 | 说明 |
| --- | --- | --- | --- |
| `chatId` | `string` | `必传` | 会话：Bot API 格式的数字 ID、`@username` 或 SDK 频道 ID（`chatId:threadId`）。 |
| `description` | `string?` | `必传` | 介绍文字；null 或空字符串清除。 |
| `cancellationToken` | `CancellationToken` | `default` | 取消令牌；默认不主动取消。 |

### 返回

`Task`。完成后无返回值。

## SetPhotoAsync

设置会话头像；`imageUri` 为 HTTP(S) 地址或适配器支持的本地资源 URI。

```csharp
Task SetPhotoAsync(string chatId, string imageUri, CancellationToken cancellationToken = default);
```

### 参数

| 参数 | C# 类型 | 默认值 | 说明 |
| --- | --- | --- | --- |
| `chatId` | `string` | `必传` | 会话：Bot API 格式的数字 ID、`@username` 或 SDK 频道 ID（`chatId:threadId`）。 |
| `imageUri` | `string` | `必传` | 图片地址：HTTP(S) 或适配器支持的本地资源 URI。 |
| `cancellationToken` | `CancellationToken` | `default` | 取消令牌；默认不主动取消。 |

### 返回

`Task`。完成后无返回值。

## DeletePhotoAsync

删除会话头像。

```csharp
Task DeletePhotoAsync(string chatId, CancellationToken cancellationToken = default);
```

### 参数

| 参数 | C# 类型 | 默认值 | 说明 |
| --- | --- | --- | --- |
| `chatId` | `string` | `必传` | 会话：Bot API 格式的数字 ID、`@username` 或 SDK 频道 ID（`chatId:threadId`）。 |
| `cancellationToken` | `CancellationToken` | `default` | 取消令牌；默认不主动取消。 |

### 返回

`Task`。完成后无返回值。

## PinMessageAsync

置顶消息。

```csharp
Task PinMessageAsync(
    string chatId,
    string messageId,
    bool disableNotification = false,
    CancellationToken cancellationToken = default
);
```

### 参数

| 参数 | C# 类型 | 默认值 | 说明 |
| --- | --- | --- | --- |
| `chatId` | `string` | `必传` | 会话：Bot API 格式的数字 ID、`@username` 或 SDK 频道 ID（`chatId:threadId`）。 |
| `messageId` | `string` | `必传` | 消息 ID，与 SDK 的 `MessageId` 相同。 |
| `disableNotification` | `bool` | `false` | 静默发送，不推送通知。 |
| `cancellationToken` | `CancellationToken` | `default` | 取消令牌；默认不主动取消。 |

### 返回

`Task`。完成后无返回值。

## UnpinMessageAsync

取消置顶；`messageId` 为 null 时取消最近一条置顶。

```csharp
Task UnpinMessageAsync(
    string chatId,
    string? messageId = null,
    CancellationToken cancellationToken = default
);
```

### 参数

| 参数 | C# 类型 | 默认值 | 说明 |
| --- | --- | --- | --- |
| `chatId` | `string` | `必传` | 会话：Bot API 格式的数字 ID、`@username` 或 SDK 频道 ID（`chatId:threadId`）。 |
| `messageId` | `string?` | `null` | 消息 ID，与 SDK 的 `MessageId` 相同。 |
| `cancellationToken` | `CancellationToken` | `default` | 取消令牌；默认不主动取消。 |

### 返回

`Task`。完成后无返回值。

## UnpinAllMessagesAsync

取消全部置顶；对话题频道 ID 只取消该话题的置顶。

```csharp
Task UnpinAllMessagesAsync(string chatId, CancellationToken cancellationToken = default);
```

### 参数

| 参数 | C# 类型 | 默认值 | 说明 |
| --- | --- | --- | --- |
| `chatId` | `string` | `必传` | 会话：Bot API 格式的数字 ID、`@username` 或 SDK 频道 ID（`chatId:threadId`）。 |
| `cancellationToken` | `CancellationToken` | `default` | 取消令牌；默认不主动取消。 |

### 返回

`Task`。完成后无返回值。

## ExportInviteLinkAsync

重新生成主邀请链接，旧链接失效。

```csharp
Task<string> ExportInviteLinkAsync(string chatId, CancellationToken cancellationToken = default);
```

### 参数

| 参数 | C# 类型 | 默认值 | 说明 |
| --- | --- | --- | --- |
| `chatId` | `string` | `必传` | 会话：Bot API 格式的数字 ID、`@username` 或 SDK 频道 ID（`chatId:threadId`）。 |
| `cancellationToken` | `CancellationToken` | `default` | 取消令牌；默认不主动取消。 |

### 返回

`Task<string>`。异步返回 `string`。

## CreateInviteLinkAsync

创建额外的邀请链接。

```csharp
Task<TelegramInviteLink> CreateInviteLinkAsync(
    string chatId,
    TelegramInviteLinkOptions? options = null,
    CancellationToken cancellationToken = default
);
```

### 参数

| 参数 | C# 类型 | 默认值 | 说明 |
| --- | --- | --- | --- |
| `chatId` | `string` | `必传` | 会话：Bot API 格式的数字 ID、`@username` 或 SDK 频道 ID（`chatId:threadId`）。 |
| `options` | `TelegramInviteLinkOptions?` | `null` | 可选设置；null 使用默认值。 |
| `cancellationToken` | `CancellationToken` | `default` | 取消令牌；默认不主动取消。 |

### 返回

`Task<TelegramInviteLink>`。异步返回 `TelegramInviteLink`。

## EditInviteLinkAsync

修改机器人创建的邀请链接。

```csharp
Task<TelegramInviteLink> EditInviteLinkAsync(
    string chatId,
    string link,
    TelegramInviteLinkOptions options,
    CancellationToken cancellationToken = default
);
```

### 参数

| 参数 | C# 类型 | 默认值 | 说明 |
| --- | --- | --- | --- |
| `chatId` | `string` | `必传` | 会话：Bot API 格式的数字 ID、`@username` 或 SDK 频道 ID（`chatId:threadId`）。 |
| `link` | `string` | `必传` | 邀请链接。 |
| `options` | `TelegramInviteLinkOptions` | `必传` | 可选设置；null 使用默认值。 |
| `cancellationToken` | `CancellationToken` | `default` | 取消令牌；默认不主动取消。 |

### 返回

`Task<TelegramInviteLink>`。异步返回 `TelegramInviteLink`。

## RevokeInviteLinkAsync

撤销邀请链接；撤销主链接时会生成新的主链接。

```csharp
Task<TelegramInviteLink> RevokeInviteLinkAsync(
    string chatId,
    string link,
    CancellationToken cancellationToken = default
);
```

### 参数

| 参数 | C# 类型 | 默认值 | 说明 |
| --- | --- | --- | --- |
| `chatId` | `string` | `必传` | 会话：Bot API 格式的数字 ID、`@username` 或 SDK 频道 ID（`chatId:threadId`）。 |
| `link` | `string` | `必传` | 邀请链接。 |
| `cancellationToken` | `CancellationToken` | `default` | 取消令牌；默认不主动取消。 |

### 返回

`Task<TelegramInviteLink>`。异步返回 `TelegramInviteLink`。

## ApproveJoinRequestAsync

批准入群申请。

```csharp
Task ApproveJoinRequestAsync(
    string chatId,
    string userId,
    CancellationToken cancellationToken = default
);
```

### 参数

| 参数 | C# 类型 | 默认值 | 说明 |
| --- | --- | --- | --- |
| `chatId` | `string` | `必传` | 会话：Bot API 格式的数字 ID、`@username` 或 SDK 频道 ID（`chatId:threadId`）。 |
| `userId` | `string` | `必传` | 用户 ID，与 SDK 的 `User.Id` 相同。 |
| `cancellationToken` | `CancellationToken` | `default` | 取消令牌；默认不主动取消。 |

### 返回

`Task`。完成后无返回值。

## DeclineJoinRequestAsync

拒绝入群申请。

```csharp
Task DeclineJoinRequestAsync(
    string chatId,
    string userId,
    CancellationToken cancellationToken = default
);
```

### 参数

| 参数 | C# 类型 | 默认值 | 说明 |
| --- | --- | --- | --- |
| `chatId` | `string` | `必传` | 会话：Bot API 格式的数字 ID、`@username` 或 SDK 频道 ID（`chatId:threadId`）。 |
| `userId` | `string` | `必传` | 用户 ID，与 SDK 的 `User.Id` 相同。 |
| `cancellationToken` | `CancellationToken` | `default` | 取消令牌；默认不主动取消。 |

### 返回

`Task`。完成后无返回值。

## CreateTopicAsync

创建话题；返回的 `TelegramForumTopic.ThreadId` 组成 SDK 频道 ID `chatId:threadId`。

```csharp
Task<TelegramForumTopic> CreateTopicAsync(
    string chatId,
    string name,
    int? iconColor = null,
    string? iconCustomEmojiId = null,
    CancellationToken cancellationToken = default
);
```

### 参数

| 参数 | C# 类型 | 默认值 | 说明 |
| --- | --- | --- | --- |
| `chatId` | `string` | `必传` | 会话：Bot API 格式的数字 ID、`@username` 或 SDK 频道 ID（`chatId:threadId`）。 |
| `name` | `string` | `必传` | 名称；null 表示不修改。 |
| `iconColor` | `int?` | `null` | RGB 图标颜色；null 使用默认值。 |
| `iconCustomEmojiId` | `string?` | `null` | 自定义表情图标 ID；null 不设置。 |
| `cancellationToken` | `CancellationToken` | `default` | 取消令牌；默认不主动取消。 |

### 返回

`Task<TelegramForumTopic>`。异步返回 `TelegramForumTopic`。

## EditTopicAsync

修改话题；null 表示保持不变。

```csharp
Task EditTopicAsync(
    string chatId,
    long threadId,
    string? name = null,
    string? iconCustomEmojiId = null,
    CancellationToken cancellationToken = default
);
```

### 参数

| 参数 | C# 类型 | 默认值 | 说明 |
| --- | --- | --- | --- |
| `chatId` | `string` | `必传` | 会话：Bot API 格式的数字 ID、`@username` 或 SDK 频道 ID（`chatId:threadId`）。 |
| `threadId` | `long` | `必传` | 话题 ID（SDK 频道 ID `chatId:threadId` 中的 threadId）。 |
| `name` | `string?` | `null` | 名称；null 表示不修改。 |
| `iconCustomEmojiId` | `string?` | `null` | 自定义表情图标 ID；null 不设置。 |
| `cancellationToken` | `CancellationToken` | `default` | 取消令牌；默认不主动取消。 |

### 返回

`Task`。完成后无返回值。

## CloseTopicAsync

关闭话题，普通成员不能再发言。

```csharp
Task CloseTopicAsync(string chatId, long threadId, CancellationToken cancellationToken = default);
```

### 参数

| 参数 | C# 类型 | 默认值 | 说明 |
| --- | --- | --- | --- |
| `chatId` | `string` | `必传` | 会话：Bot API 格式的数字 ID、`@username` 或 SDK 频道 ID（`chatId:threadId`）。 |
| `threadId` | `long` | `必传` | 话题 ID（SDK 频道 ID `chatId:threadId` 中的 threadId）。 |
| `cancellationToken` | `CancellationToken` | `default` | 取消令牌；默认不主动取消。 |

### 返回

`Task`。完成后无返回值。

## ReopenTopicAsync

重新开启已关闭的话题。

```csharp
Task ReopenTopicAsync(string chatId, long threadId, CancellationToken cancellationToken = default);
```

### 参数

| 参数 | C# 类型 | 默认值 | 说明 |
| --- | --- | --- | --- |
| `chatId` | `string` | `必传` | 会话：Bot API 格式的数字 ID、`@username` 或 SDK 频道 ID（`chatId:threadId`）。 |
| `threadId` | `long` | `必传` | 话题 ID（SDK 频道 ID `chatId:threadId` 中的 threadId）。 |
| `cancellationToken` | `CancellationToken` | `default` | 取消令牌；默认不主动取消。 |

### 返回

`Task`。完成后无返回值。

## DeleteTopicAsync

删除话题及其中的全部消息。

```csharp
Task DeleteTopicAsync(string chatId, long threadId, CancellationToken cancellationToken = default);
```

### 参数

| 参数 | C# 类型 | 默认值 | 说明 |
| --- | --- | --- | --- |
| `chatId` | `string` | `必传` | 会话：Bot API 格式的数字 ID、`@username` 或 SDK 频道 ID（`chatId:threadId`）。 |
| `threadId` | `long` | `必传` | 话题 ID（SDK 频道 ID `chatId:threadId` 中的 threadId）。 |
| `cancellationToken` | `CancellationToken` | `default` | 取消令牌；默认不主动取消。 |

### 返回

`Task`。完成后无返回值。

## 相关类型

见 [C# 类型参考](./types)。`required` 属性创建对象时必须填写；`?` 表示可空。
