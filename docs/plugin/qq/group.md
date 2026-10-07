# 群管理（IQGroupApi）

命名空间：`ShiroBot.Model.QQ`。QQ Model ABI：`1.0.0.0`。

```csharp
var api = Context.GetAdapterExtension<IQGroupApi>();
if (api is null) return;
```

接口可获取不表示每个方法都已实现；未实现的操作会抛出 `NotSupportedException`。平台权限与参数错误仍可能导致调用失败。

先检查 `Capabilities`。官方与 Milky 的 ID 都为字符串，但各自只在来源实例内有意义。入群审批传原申请对象，分页持续使用 NextCursor，即使当前页 Requests 为空。

## Capabilities

适配器实现的能力，不代表账号已获得平台授权。

```csharp
QGroupCapabilities Capabilities { get; }
```

## GetGroupListAsync

查询当前账号的群列表。

```csharp
Task<IReadOnlyList<QGroup>> GetGroupListAsync(
    bool noCache = false,
    CancellationToken cancellationToken = default
);
```

### 参数

| 参数 | C# 类型 | 默认值 | 说明 |
| --- | --- | --- | --- |
| `noCache` | `bool` | `false` | 是否绕过协议端缓存。 |
| `cancellationToken` | `CancellationToken` | `default` | 取消令牌；默认不主动取消。 |

### 返回

`Task<IReadOnlyList<QGroup>>`。等待异步完成后取得签名所列模型或列表。

## GetGroupInfoAsync

查询指定群的资料。

```csharp
Task<QGroup> GetGroupInfoAsync(
    string groupId,
    bool noCache = false,
    CancellationToken cancellationToken = default
);
```

### 参数

| 参数 | C# 类型 | 默认值 | 说明 |
| --- | --- | --- | --- |
| `groupId` | `string` | `必传` | 当前实例内的群 ID。 |
| `noCache` | `bool` | `false` | 是否绕过协议端缓存。 |
| `cancellationToken` | `CancellationToken` | `default` | 取消令牌；默认不主动取消。 |

### 返回

`Task<QGroup>`。等待异步完成后取得签名所列模型或列表。

## GetGroupMemberListAsync

查询指定群的成员列表。

```csharp
Task<IReadOnlyList<QGroupMember>> GetGroupMemberListAsync(
    string groupId,
    bool noCache = false,
    CancellationToken cancellationToken = default
);
```

### 参数

| 参数 | C# 类型 | 默认值 | 说明 |
| --- | --- | --- | --- |
| `groupId` | `string` | `必传` | 当前实例内的群 ID。 |
| `noCache` | `bool` | `false` | 是否绕过协议端缓存。 |
| `cancellationToken` | `CancellationToken` | `default` | 取消令牌；默认不主动取消。 |

### 返回

`Task<IReadOnlyList<QGroupMember>>`。等待异步完成后取得签名所列模型或列表。

## GetGroupMemberInfoAsync

查询群内指定成员的资料。

```csharp
Task<QGroupMember> GetGroupMemberInfoAsync(
    string groupId,
    string userId,
    bool noCache = false,
    CancellationToken cancellationToken = default
);
```

### 参数

| 参数 | C# 类型 | 默认值 | 说明 |
| --- | --- | --- | --- |
| `groupId` | `string` | `必传` | 当前实例内的群 ID。 |
| `userId` | `string` | `必传` | 当前实例内的用户 ID。 |
| `noCache` | `bool` | `false` | 是否绕过协议端缓存。 |
| `cancellationToken` | `CancellationToken` | `default` | 取消令牌；默认不主动取消。 |

### 返回

`Task<QGroupMember>`。等待异步完成后取得签名所列模型或列表。

## SetGroupNameAsync

修改群名。

```csharp
Task SetGroupNameAsync(string groupId, string name, CancellationToken cancellationToken = default);
```

### 参数

| 参数 | C# 类型 | 默认值 | 说明 |
| --- | --- | --- | --- |
| `groupId` | `string` | `必传` | 当前实例内的群 ID。 |
| `name` | `string` | `必传` | 群名。 |
| `cancellationToken` | `CancellationToken` | `default` | 取消令牌；默认不主动取消。 |

### 返回

`Task`。异步完成后不返回业务值。

## SetGroupAvatarAsync

修改群头像。

```csharp
Task SetGroupAvatarAsync(
    string groupId,
    string imageUri,
    CancellationToken cancellationToken = default
);
```

### 参数

| 参数 | C# 类型 | 默认值 | 说明 |
| --- | --- | --- | --- |
| `groupId` | `string` | `必传` | 当前实例内的群 ID。 |
| `imageUri` | `string` | `必传` | 图片 URI，由适配器解析。 |
| `cancellationToken` | `CancellationToken` | `default` | 取消令牌；默认不主动取消。 |

### 返回

`Task`。异步完成后不返回业务值。

## SetMemberCardAsync

修改群成员名片。

```csharp
Task SetMemberCardAsync(
    string groupId,
    string userId,
    string card,
    CancellationToken cancellationToken = default
);
```

### 参数

| 参数 | C# 类型 | 默认值 | 说明 |
| --- | --- | --- | --- |
| `groupId` | `string` | `必传` | 当前实例内的群 ID。 |
| `userId` | `string` | `必传` | 当前实例内的用户 ID。 |
| `card` | `string` | `必传` | 群名片。 |
| `cancellationToken` | `CancellationToken` | `default` | 取消令牌；默认不主动取消。 |

### 返回

`Task`。异步完成后不返回业务值。

## SetMemberSpecialTitleAsync

修改群成员专属头衔。

```csharp
Task SetMemberSpecialTitleAsync(
    string groupId,
    string userId,
    string title,
    CancellationToken cancellationToken = default
);
```

### 参数

| 参数 | C# 类型 | 默认值 | 说明 |
| --- | --- | --- | --- |
| `groupId` | `string` | `必传` | 当前实例内的群 ID。 |
| `userId` | `string` | `必传` | 当前实例内的用户 ID。 |
| `title` | `string` | `必传` | 成员专属头衔。 |
| `cancellationToken` | `CancellationToken` | `default` | 取消令牌；默认不主动取消。 |

### 返回

`Task`。异步完成后不返回业务值。

## SetMemberAdminAsync

设置或取消群管理员。

```csharp
Task SetMemberAdminAsync(
    string groupId,
    string userId,
    bool isSet = true,
    CancellationToken cancellationToken = default
);
```

### 参数

| 参数 | C# 类型 | 默认值 | 说明 |
| --- | --- | --- | --- |
| `groupId` | `string` | `必传` | 当前实例内的群 ID。 |
| `userId` | `string` | `必传` | 当前实例内的用户 ID。 |
| `isSet` | `bool` | `true` | 是否设置；false 为取消。 |
| `cancellationToken` | `CancellationToken` | `default` | 取消令牌；默认不主动取消。 |

### 返回

`Task`。异步完成后不返回业务值。

## MuteMemberAsync

禁言指定成员，零时长解除禁言。

```csharp
Task MuteMemberAsync(
    string groupId,
    string userId,
    TimeSpan duration,
    CancellationToken cancellationToken = default
);
```

### 参数

| 参数 | C# 类型 | 默认值 | 说明 |
| --- | --- | --- | --- |
| `groupId` | `string` | `必传` | 当前实例内的群 ID。 |
| `userId` | `string` | `必传` | 当前实例内的用户 ID。 |
| `duration` | `TimeSpan` | `必传` | 禁言或输入状态的持续时间；零禁言时长表示解除禁言。 |
| `cancellationToken` | `CancellationToken` | `default` | 取消令牌；默认不主动取消。 |

### 返回

`Task`。异步完成后不返回业务值。

## SetWholeMuteAsync

设置或取消全员禁言。

```csharp
Task SetWholeMuteAsync(
    string groupId,
    bool isMute = true,
    CancellationToken cancellationToken = default
);
```

### 参数

| 参数 | C# 类型 | 默认值 | 说明 |
| --- | --- | --- | --- |
| `groupId` | `string` | `必传` | 当前实例内的群 ID。 |
| `isMute` | `bool` | `true` | 是否开启全员禁言。 |
| `cancellationToken` | `CancellationToken` | `default` | 取消令牌；默认不主动取消。 |

### 返回

`Task`。异步完成后不返回业务值。

## KickMemberAsync

移出指定群成员。

```csharp
Task KickMemberAsync(
    string groupId,
    string userId,
    bool rejectAddRequest = false,
    CancellationToken cancellationToken = default
);
```

### 参数

| 参数 | C# 类型 | 默认值 | 说明 |
| --- | --- | --- | --- |
| `groupId` | `string` | `必传` | 当前实例内的群 ID。 |
| `userId` | `string` | `必传` | 当前实例内的用户 ID。 |
| `rejectAddRequest` | `bool` | `false` | 踢人后是否拒绝再次入群，依平台语义执行。 |
| `cancellationToken` | `CancellationToken` | `default` | 取消令牌；默认不主动取消。 |

### 返回

`Task`。异步完成后不返回业务值。

## QuitGroupAsync

让机器人退出指定群。

```csharp
Task QuitGroupAsync(string groupId, CancellationToken cancellationToken = default);
```

### 参数

| 参数 | C# 类型 | 默认值 | 说明 |
| --- | --- | --- | --- |
| `groupId` | `string` | `必传` | 当前实例内的群 ID。 |
| `cancellationToken` | `CancellationToken` | `default` | 取消令牌；默认不主动取消。 |

### 返回

`Task`。异步完成后不返回业务值。

## SendNudgeAsync

发送戳一戳。

```csharp
Task SendNudgeAsync(string groupId, string userId, CancellationToken cancellationToken = default);
```

### 参数

| 参数 | C# 类型 | 默认值 | 说明 |
| --- | --- | --- | --- |
| `groupId` | `string` | `必传` | 当前实例内的群 ID。 |
| `userId` | `string` | `必传` | 当前实例内的用户 ID。 |
| `cancellationToken` | `CancellationToken` | `default` | 取消令牌；默认不主动取消。 |

### 返回

`Task`。异步完成后不返回业务值。

## SendMessageReactionAsync

添加或移除消息表情回应。

```csharp
Task SendMessageReactionAsync(
    string groupId,
    string messageId,
    string faceId,
    bool isAdd = true,
    CancellationToken cancellationToken = default
);
```

### 参数

| 参数 | C# 类型 | 默认值 | 说明 |
| --- | --- | --- | --- |
| `groupId` | `string` | `必传` | 当前实例内的群 ID。 |
| `messageId` | `string` | `必传` | 当前会话的消息 ID。 |
| `faceId` | `string` | `必传` | QQ 表情 ID。 |
| `isAdd` | `bool` | `true` | 是否添加回应；false 为移除。 |
| `cancellationToken` | `CancellationToken` | `default` | 取消令牌；默认不主动取消。 |

### 返回

`Task`。异步完成后不返回业务值。

## SendMessageReactionAsync

发送消息表情回应(指定 Face/Emoji 类型)。

```csharp
Task SendMessageReactionAsync(
    string groupId,
    string messageId,
    string reactionId,
    QReactionType reactionType,
    bool isAdd = true,
    CancellationToken cancellationToken = default
);
```

### 参数

| 参数 | C# 类型 | 默认值 | 说明 |
| --- | --- | --- | --- |
| `groupId` | `string` | `必传` | 当前实例内的群 ID。 |
| `messageId` | `string` | `必传` | 当前会话的消息 ID。 |
| `reactionId` | `string` | `必传` | 回应 ID，与 reactionType 对应。 |
| `reactionType` | `QReactionType` | `必传` | QQ 表情或 Emoji 回应类型。 |
| `isAdd` | `bool` | `true` | 是否添加回应；false 为移除。 |
| `cancellationToken` | `CancellationToken` | `default` | 取消令牌；默认不主动取消。 |

### 返回

`Task`。异步完成后不返回业务值。

## GetAnnouncementsAsync

查询群公告。

```csharp
Task<IReadOnlyList<QGroupAnnouncement>> GetAnnouncementsAsync(
    string groupId,
    CancellationToken cancellationToken = default
);
```

### 参数

| 参数 | C# 类型 | 默认值 | 说明 |
| --- | --- | --- | --- |
| `groupId` | `string` | `必传` | 当前实例内的群 ID。 |
| `cancellationToken` | `CancellationToken` | `default` | 取消令牌；默认不主动取消。 |

### 返回

`Task<IReadOnlyList<QGroupAnnouncement>>`。等待异步完成后取得签名所列模型或列表。

## SendAnnouncementAsync

发布群公告。

```csharp
Task SendAnnouncementAsync(
    string groupId,
    string content,
    string? imageUri = null,
    CancellationToken cancellationToken = default
);
```

### 参数

| 参数 | C# 类型 | 默认值 | 说明 |
| --- | --- | --- | --- |
| `groupId` | `string` | `必传` | 当前实例内的群 ID。 |
| `content` | `string` | `必传` | 待发送的文本、Markdown 或可读媒体流，具体类型见签名。 |
| `imageUri` | `string?` | `null` | 图片 URI，由适配器解析。 |
| `cancellationToken` | `CancellationToken` | `default` | 取消令牌；默认不主动取消。 |

### 返回

`Task`。异步完成后不返回业务值。

## DeleteAnnouncementAsync

删除指定群公告。

```csharp
Task DeleteAnnouncementAsync(
    string groupId,
    string announcementId,
    CancellationToken cancellationToken = default
);
```

### 参数

| 参数 | C# 类型 | 默认值 | 说明 |
| --- | --- | --- | --- |
| `groupId` | `string` | `必传` | 当前实例内的群 ID。 |
| `announcementId` | `string` | `必传` | 群公告 ID。 |
| `cancellationToken` | `CancellationToken` | `default` | 取消令牌；默认不主动取消。 |

### 返回

`Task`。异步完成后不返回业务值。

## GetEssenceMessagesAsync

查询一页群精华消息。

```csharp
Task<IReadOnlyList<QEssenceMessage>> GetEssenceMessagesAsync(
    string groupId,
    int pageIndex,
    int pageSize,
    CancellationToken cancellationToken = default
);
```

### 参数

| 参数 | C# 类型 | 默认值 | 说明 |
| --- | --- | --- | --- |
| `groupId` | `string` | `必传` | 当前实例内的群 ID。 |
| `pageIndex` | `int` | `必传` | 页索引，按协议端约定。 |
| `pageSize` | `int` | `必传` | 每页数量。 |
| `cancellationToken` | `CancellationToken` | `default` | 取消令牌；默认不主动取消。 |

### 返回

`Task<IReadOnlyList<QEssenceMessage>>`。等待异步完成后取得签名所列模型或列表。

## GetEssenceMessagesPageAsync

获取一页群精华消息，并返回是否已到最后一页。

```csharp
Task<(IReadOnlyList<QEssenceMessage> Messages, bool IsEnd)> GetEssenceMessagesPageAsync(
    string groupId,
    int pageIndex,
    int pageSize,
    CancellationToken cancellationToken = default
);
```

### 参数

| 参数 | C# 类型 | 默认值 | 说明 |
| --- | --- | --- | --- |
| `groupId` | `string` | `必传` | 当前实例内的群 ID。 |
| `pageIndex` | `int` | `必传` | 页索引，按协议端约定。 |
| `pageSize` | `int` | `必传` | 每页数量。 |
| `cancellationToken` | `CancellationToken` | `default` | 取消令牌；默认不主动取消。 |

### 返回

`Task<(IReadOnlyList<QEssenceMessage> Messages, bool IsEnd)>`。返回消息列表 Messages 与是否最后一页 IsEnd。

## SetEssenceMessageAsync

设置或取消精华消息。

```csharp
Task SetEssenceMessageAsync(
    string groupId,
    string messageId,
    bool isSet = true,
    CancellationToken cancellationToken = default
);
```

### 参数

| 参数 | C# 类型 | 默认值 | 说明 |
| --- | --- | --- | --- |
| `groupId` | `string` | `必传` | 当前实例内的群 ID。 |
| `messageId` | `string` | `必传` | 当前会话的消息 ID。 |
| `isSet` | `bool` | `true` | 是否设置；false 为取消。 |
| `cancellationToken` | `CancellationToken` | `default` | 取消令牌；默认不主动取消。 |

### 返回

`Task`。异步完成后不返回业务值。

## AcceptJoinRequestAsync

按原样返回的申请对象接受申请或邀请他人入群。

```csharp
Task AcceptJoinRequestAsync(
    QGroupJoinRequest request,
    CancellationToken cancellationToken = default
);
```

### 参数

| 参数 | C# 类型 | 默认值 | 说明 |
| --- | --- | --- | --- |
| `request` | `QGroupJoinRequest` | `必传` | 查询或事件返回的完整入群申请对象。 |
| `cancellationToken` | `CancellationToken` | `default` | 取消令牌；默认不主动取消。 |

### 返回

`Task`。异步完成后不返回业务值。

## RejectJoinRequestAsync

拒绝入群申请，可选择拒绝理由及同时拉黑。

```csharp
Task RejectJoinRequestAsync(
    QGroupJoinRequest request,
    string? reason = null,
    bool addToBlacklist = false,
    CancellationToken cancellationToken = default
);
```

### 参数

| 参数 | C# 类型 | 默认值 | 说明 |
| --- | --- | --- | --- |
| `request` | `QGroupJoinRequest` | `必传` | 查询或事件返回的完整入群申请对象。 |
| `reason` | `string?` | `null` | 可选的拒绝理由。 |
| `addToBlacklist` | `bool` | `false` | 拒绝申请时是否同时拉黑；需适配器支持。 |
| `cancellationToken` | `CancellationToken` | `default` | 取消令牌；默认不主动取消。 |

### 返回

`Task`。异步完成后不返回业务值。

## GetJoinRequestsAsync

统一分页申请列表；Cursor 是不透明分页凭据。

```csharp
Task<QGroupJoinRequestPage> GetJoinRequestsAsync(
    string groupId,
    string? cursor = null,
    int limit = 20,
    CancellationToken cancellationToken = default
);
```

### 参数

| 参数 | C# 类型 | 默认值 | 说明 |
| --- | --- | --- | --- |
| `groupId` | `string` | `必传` | 当前实例内的群 ID。 |
| `cursor` | `string?` | `null` | 上一页返回的 NextCursor；null 从第一页开始。 |
| `limit` | `int` | `20` | 本页请求数量。 |
| `cancellationToken` | `CancellationToken` | `default` | 取消令牌；默认不主动取消。 |

### 返回

`Task<QGroupJoinRequestPage>`。返回申请列表 Requests 和下一页游标 NextCursor；null 表示没有下一页。

## GetMuteStateAsync

查询群禁言状态和平台返回的定时、周期规则。

```csharp
Task<QGroupMuteState> GetMuteStateAsync(
    string groupId,
    CancellationToken cancellationToken = default
);
```

### 参数

| 参数 | C# 类型 | 默认值 | 说明 |
| --- | --- | --- | --- |
| `groupId` | `string` | `必传` | 当前实例内的群 ID。 |
| `cancellationToken` | `CancellationToken` | `default` | 取消令牌；默认不主动取消。 |

### 返回

`Task<QGroupMuteState>`。等待异步完成后取得签名所列模型或列表。

## SetMemberMutesAsync

按项设置成员禁言，零时长解除禁言。先验证全部参数，再逐项执行；失败不回滚已完成的项，继续处理剩余项。

```csharp
Task<QBatchOperationResult> SetMemberMutesAsync(
    string groupId,
    IReadOnlyList<QMemberMute> members,
    CancellationToken cancellationToken = default
);
```

### 参数

| 参数 | C# 类型 | 默认值 | 说明 |
| --- | --- | --- | --- |
| `groupId` | `string` | `必传` | 当前实例内的群 ID。 |
| `members` | `IReadOnlyList<QMemberMute>` | `必传` | 本次操作的成员及禁言时长列表。 |
| `cancellationToken` | `CancellationToken` | `default` | 取消令牌；默认不主动取消。 |

### 返回

`Task<QBatchOperationResult>`。返回每项的 Succeeded、Failed、Unknown 或 NotExecuted 状态。Unknown 不能假定服务端未执行；取消抛 QBatchOperationCanceledException，可读取 PartialResult。

## GetNotificationsAsync

获取群通知列表(入群申请/邀请/管理员变更/踢人/退群)。返回通知与下一页起始序号。

```csharp
Task<(IReadOnlyList<QGroupNotification> Notifications, string? NextCursor)> GetNotificationsAsync(
    string? cursor = null,
    bool isFiltered = false,
    int limit = 20,
    CancellationToken cancellationToken = default
);
```

### 参数

| 参数 | C# 类型 | 默认值 | 说明 |
| --- | --- | --- | --- |
| `cursor` | `string?` | `null` | 上一页返回的 NextCursor；null 从第一页开始。 |
| `isFiltered` | `bool` | `false` | 是否处理被协议端过滤的请求。 |
| `limit` | `int` | `20` | 本页请求数量。 |
| `cancellationToken` | `CancellationToken` | `default` | 取消令牌；默认不主动取消。 |

### 返回

`Task<(IReadOnlyList<QGroupNotification> Notifications, string? NextCursor)>`。返回通知列表 Notifications 和下一页游标 NextCursor。

## AcceptInvitationAsync

接受他人邀请机器人入群。invitationId 来自群邀请事件的 Token。

```csharp
Task AcceptInvitationAsync(
    string groupId,
    string invitationId,
    CancellationToken cancellationToken = default
);
```

### 参数

| 参数 | C# 类型 | 默认值 | 说明 |
| --- | --- | --- | --- |
| `groupId` | `string` | `必传` | 当前实例内的群 ID。 |
| `invitationId` | `string` | `必传` | 群邀请事件返回的邀请 ID。 |
| `cancellationToken` | `CancellationToken` | `default` | 取消令牌；默认不主动取消。 |

### 返回

`Task`。异步完成后不返回业务值。

## RejectInvitationAsync

拒绝邀请机器人入群。

```csharp
Task RejectInvitationAsync(
    string groupId,
    string invitationId,
    CancellationToken cancellationToken = default
);
```

### 参数

| 参数 | C# 类型 | 默认值 | 说明 |
| --- | --- | --- | --- |
| `groupId` | `string` | `必传` | 当前实例内的群 ID。 |
| `invitationId` | `string` | `必传` | 群邀请事件返回的邀请 ID。 |
| `cancellationToken` | `CancellationToken` | `default` | 取消令牌；默认不主动取消。 |

### 返回

`Task`。异步完成后不返回业务值。

## 入群审批示例

```csharp
Events.MapPlatform(QEventKinds.GroupJoinRequest, async evt =>
{
    if (evt.Raw is not QGroupJoinRequest request) return;
    var groups = Context.GetAdapterExtension<IQGroupApi>();
    if (groups?.Capabilities.HasFlag(QGroupCapabilities.JoinRequests) == true)
        await groups.AcceptJoinRequestAsync(request);
});
```

## 相关类型

见[模型、消息段与事件类型](./types)。完整声明中的 `required` 字段创建对象时必须填写；`?` 表示可空。
