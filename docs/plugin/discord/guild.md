# 服务器管理（IDiscordGuildApi）

命名空间：`ShiroBot.Model.Discord`。两种账号都实现。

服务器、频道、角色、成员、封禁、邀请、表情和子区管理。

```csharp
var api = Context.GetAdapterExtension<IDiscordGuildApi>();
if (api is null) return;
```

取得接口不代表每个方法都已实现；未实现的方法抛出 `NotSupportedException`，调用还受平台权限和参数约束。

## GetGuildAsync

查询服务器资料。

```csharp
Task<DiscordGuild> GetGuildAsync(
    string guildId,
    bool withCounts = false,
    CancellationToken cancellationToken = default
);
```

### 参数

| 参数 | C# 类型 | 默认值 | 说明 |
| --- | --- | --- | --- |
| `guildId` | `string` | `必传` | 服务器 ID。 |
| `withCounts` | `bool` | `false` | 同时返回近似成员数和在线数。 |
| `cancellationToken` | `CancellationToken` | `default` | 取消令牌；默认不主动取消。 |

### 返回

`Task<DiscordGuild>`。异步返回 `DiscordGuild`。

## GetGuildsAsync

机器人所在的全部服务器。

```csharp
Task<IReadOnlyList<DiscordGuild>> GetGuildsAsync(CancellationToken cancellationToken = default);
```

### 参数

| 参数 | C# 类型 | 默认值 | 说明 |
| --- | --- | --- | --- |
| `cancellationToken` | `CancellationToken` | `default` | 取消令牌；默认不主动取消。 |

### 返回

`Task<IReadOnlyList<DiscordGuild>>`。异步返回 `IReadOnlyList<DiscordGuild>`。

## GetGuildChannelsAsync

查询服务器的全部频道（不含子区）。

```csharp
Task<IReadOnlyList<DiscordChannel>> GetGuildChannelsAsync(
    string guildId,
    CancellationToken cancellationToken = default
);
```

### 参数

| 参数 | C# 类型 | 默认值 | 说明 |
| --- | --- | --- | --- |
| `guildId` | `string` | `必传` | 服务器 ID。 |
| `cancellationToken` | `CancellationToken` | `default` | 取消令牌；默认不主动取消。 |

### 返回

`Task<IReadOnlyList<DiscordChannel>>`。异步返回 `IReadOnlyList<DiscordChannel>`。

## CreateChannelAsync

创建频道。

```csharp
Task<DiscordChannel> CreateChannelAsync(
    string guildId,
    DiscordChannelOptions options,
    string? reason = null,
    CancellationToken cancellationToken = default
);
```

### 参数

| 参数 | C# 类型 | 默认值 | 说明 |
| --- | --- | --- | --- |
| `guildId` | `string` | `必传` | 服务器 ID。 |
| `options` | `DiscordChannelOptions` | `必传` | 可选设置；null 使用默认值。 |
| `reason` | `string?` | `null` | 写入服务器审计日志的原因；null 不写。 |
| `cancellationToken` | `CancellationToken` | `default` | 取消令牌；默认不主动取消。 |

### 返回

`Task<DiscordChannel>`。异步返回 `DiscordChannel`。

## ModifyChannelAsync

修改频道设置；未设置的项保持不变。

```csharp
Task<DiscordChannel> ModifyChannelAsync(
    string channelId,
    DiscordChannelOptions options,
    string? reason = null,
    CancellationToken cancellationToken = default
);
```

### 参数

| 参数 | C# 类型 | 默认值 | 说明 |
| --- | --- | --- | --- |
| `channelId` | `string` | `必传` | 频道 ID。 |
| `options` | `DiscordChannelOptions` | `必传` | 可选设置；null 使用默认值。 |
| `reason` | `string?` | `null` | 写入服务器审计日志的原因；null 不写。 |
| `cancellationToken` | `CancellationToken` | `default` | 取消令牌；默认不主动取消。 |

### 返回

`Task<DiscordChannel>`。异步返回 `DiscordChannel`。

## DeleteChannelAsync

删除频道或子区。

```csharp
Task DeleteChannelAsync(
    string channelId,
    string? reason = null,
    CancellationToken cancellationToken = default
);
```

### 参数

| 参数 | C# 类型 | 默认值 | 说明 |
| --- | --- | --- | --- |
| `channelId` | `string` | `必传` | 频道 ID。 |
| `reason` | `string?` | `null` | 写入服务器审计日志的原因；null 不写。 |
| `cancellationToken` | `CancellationToken` | `default` | 取消令牌；默认不主动取消。 |

### 返回

`Task`。完成后无返回值。

## SetChannelPermissionAsync

设置频道对某个角色或成员的权限覆盖。

```csharp
Task SetChannelPermissionAsync(
    string channelId,
    string roleOrUserId,
    bool isRole,
    DiscordPermissions allow,
    DiscordPermissions deny,
    string? reason = null,
    CancellationToken cancellationToken = default
);
```

### 参数

| 参数 | C# 类型 | 默认值 | 说明 |
| --- | --- | --- | --- |
| `channelId` | `string` | `必传` | 频道 ID。 |
| `roleOrUserId` | `string` | `必传` | 角色或成员 ID，由 isRole 区分。 |
| `isRole` | `bool` | `必传` | true 表示角色覆盖，false 表示成员覆盖。 |
| `allow` | `DiscordPermissions` | `必传` | 允许的权限。 |
| `deny` | `DiscordPermissions` | `必传` | 拒绝的权限。 |
| `reason` | `string?` | `null` | 写入服务器审计日志的原因；null 不写。 |
| `cancellationToken` | `CancellationToken` | `default` | 取消令牌；默认不主动取消。 |

### 返回

`Task`。完成后无返回值。

## DeleteChannelPermissionAsync

删除频道上对某个角色或成员的权限覆盖。

```csharp
Task DeleteChannelPermissionAsync(
    string channelId,
    string roleOrUserId,
    string? reason = null,
    CancellationToken cancellationToken = default
);
```

### 参数

| 参数 | C# 类型 | 默认值 | 说明 |
| --- | --- | --- | --- |
| `channelId` | `string` | `必传` | 频道 ID。 |
| `roleOrUserId` | `string` | `必传` | 角色或成员 ID，由 isRole 区分。 |
| `reason` | `string?` | `null` | 写入服务器审计日志的原因；null 不写。 |
| `cancellationToken` | `CancellationToken` | `default` | 取消令牌；默认不主动取消。 |

### 返回

`Task`。完成后无返回值。

## GetRolesAsync

查询服务器角色。

```csharp
Task<IReadOnlyList<DiscordRole>> GetRolesAsync(
    string guildId,
    CancellationToken cancellationToken = default
);
```

### 参数

| 参数 | C# 类型 | 默认值 | 说明 |
| --- | --- | --- | --- |
| `guildId` | `string` | `必传` | 服务器 ID。 |
| `cancellationToken` | `CancellationToken` | `default` | 取消令牌；默认不主动取消。 |

### 返回

`Task<IReadOnlyList<DiscordRole>>`。异步返回 `IReadOnlyList<DiscordRole>`。

## CreateRoleAsync

创建角色。

```csharp
Task<DiscordRole> CreateRoleAsync(
    string guildId,
    DiscordRoleOptions options,
    string? reason = null,
    CancellationToken cancellationToken = default
);
```

### 参数

| 参数 | C# 类型 | 默认值 | 说明 |
| --- | --- | --- | --- |
| `guildId` | `string` | `必传` | 服务器 ID。 |
| `options` | `DiscordRoleOptions` | `必传` | 可选设置；null 使用默认值。 |
| `reason` | `string?` | `null` | 写入服务器审计日志的原因；null 不写。 |
| `cancellationToken` | `CancellationToken` | `default` | 取消令牌；默认不主动取消。 |

### 返回

`Task<DiscordRole>`。异步返回 `DiscordRole`。

## ModifyRoleAsync

修改角色；未设置的项保持不变。

```csharp
Task<DiscordRole> ModifyRoleAsync(
    string guildId,
    string roleId,
    DiscordRoleOptions options,
    string? reason = null,
    CancellationToken cancellationToken = default
);
```

### 参数

| 参数 | C# 类型 | 默认值 | 说明 |
| --- | --- | --- | --- |
| `guildId` | `string` | `必传` | 服务器 ID。 |
| `roleId` | `string` | `必传` | 角色 ID。 |
| `options` | `DiscordRoleOptions` | `必传` | 可选设置；null 使用默认值。 |
| `reason` | `string?` | `null` | 写入服务器审计日志的原因；null 不写。 |
| `cancellationToken` | `CancellationToken` | `default` | 取消令牌；默认不主动取消。 |

### 返回

`Task<DiscordRole>`。异步返回 `DiscordRole`。

## DeleteRoleAsync

删除角色。

```csharp
Task DeleteRoleAsync(
    string guildId,
    string roleId,
    string? reason = null,
    CancellationToken cancellationToken = default
);
```

### 参数

| 参数 | C# 类型 | 默认值 | 说明 |
| --- | --- | --- | --- |
| `guildId` | `string` | `必传` | 服务器 ID。 |
| `roleId` | `string` | `必传` | 角色 ID。 |
| `reason` | `string?` | `null` | 写入服务器审计日志的原因；null 不写。 |
| `cancellationToken` | `CancellationToken` | `default` | 取消令牌；默认不主动取消。 |

### 返回

`Task`。完成后无返回值。

## GetGuildMemberAsync

查询成员资料，并按角色计算服务器级权限。

```csharp
Task<DiscordMember> GetGuildMemberAsync(
    string guildId,
    string userId,
    CancellationToken cancellationToken = default
);
```

### 参数

| 参数 | C# 类型 | 默认值 | 说明 |
| --- | --- | --- | --- |
| `guildId` | `string` | `必传` | 服务器 ID。 |
| `userId` | `string` | `必传` | 用户 ID，与 SDK 的 `User.Id` 相同。 |
| `cancellationToken` | `CancellationToken` | `default` | 取消令牌；默认不主动取消。 |

### 返回

`Task<DiscordMember>`。异步返回 `DiscordMember`。

## SearchMembersAsync

按用户名或昵称前缀搜索成员，最多 1000 个。

```csharp
Task<IReadOnlyList<DiscordMember>> SearchMembersAsync(
    string guildId,
    string query,
    int limit = 100,
    CancellationToken cancellationToken = default
);
```

### 参数

| 参数 | C# 类型 | 默认值 | 说明 |
| --- | --- | --- | --- |
| `guildId` | `string` | `必传` | 服务器 ID。 |
| `query` | `string` | `必传` | 搜索文本。 |
| `limit` | `int` | `100` | 返回数量上限。 |
| `cancellationToken` | `CancellationToken` | `default` | 取消令牌；默认不主动取消。 |

### 返回

`Task<IReadOnlyList<DiscordMember>>`。异步返回 `IReadOnlyList<DiscordMember>`。

## AddMemberRoleAsync

给成员添加角色。

```csharp
Task AddMemberRoleAsync(
    string guildId,
    string userId,
    string roleId,
    string? reason = null,
    CancellationToken cancellationToken = default
);
```

### 参数

| 参数 | C# 类型 | 默认值 | 说明 |
| --- | --- | --- | --- |
| `guildId` | `string` | `必传` | 服务器 ID。 |
| `userId` | `string` | `必传` | 用户 ID，与 SDK 的 `User.Id` 相同。 |
| `roleId` | `string` | `必传` | 角色 ID。 |
| `reason` | `string?` | `null` | 写入服务器审计日志的原因；null 不写。 |
| `cancellationToken` | `CancellationToken` | `default` | 取消令牌；默认不主动取消。 |

### 返回

`Task`。完成后无返回值。

## RemoveMemberRoleAsync

移除成员的角色。

```csharp
Task RemoveMemberRoleAsync(
    string guildId,
    string userId,
    string roleId,
    string? reason = null,
    CancellationToken cancellationToken = default
);
```

### 参数

| 参数 | C# 类型 | 默认值 | 说明 |
| --- | --- | --- | --- |
| `guildId` | `string` | `必传` | 服务器 ID。 |
| `userId` | `string` | `必传` | 用户 ID，与 SDK 的 `User.Id` 相同。 |
| `roleId` | `string` | `必传` | 角色 ID。 |
| `reason` | `string?` | `null` | 写入服务器审计日志的原因；null 不写。 |
| `cancellationToken` | `CancellationToken` | `default` | 取消令牌；默认不主动取消。 |

### 返回

`Task`。完成后无返回值。

## SetNicknameAsync

设置成员昵称；`userId` 为 `@me` 时修改机器人自己的昵称，null 清除昵称。

```csharp
Task SetNicknameAsync(
    string guildId,
    string userId,
    string? nickname,
    string? reason = null,
    CancellationToken cancellationToken = default
);
```

### 参数

| 参数 | C# 类型 | 默认值 | 说明 |
| --- | --- | --- | --- |
| `guildId` | `string` | `必传` | 服务器 ID。 |
| `userId` | `string` | `必传` | 用户 ID，与 SDK 的 `User.Id` 相同。 |
| `nickname` | `string?` | `必传` | 昵称；null 清除。 |
| `reason` | `string?` | `null` | 写入服务器审计日志的原因；null 不写。 |
| `cancellationToken` | `CancellationToken` | `default` | 取消令牌；默认不主动取消。 |

### 返回

`Task`。完成后无返回值。

## TimeoutMemberAsync

超时（禁言）成员，最长 28 天；`until` 为 null 时解除超时。

```csharp
Task TimeoutMemberAsync(
    string guildId,
    string userId,
    DateTimeOffset? until,
    string? reason = null,
    CancellationToken cancellationToken = default
);
```

### 参数

| 参数 | C# 类型 | 默认值 | 说明 |
| --- | --- | --- | --- |
| `guildId` | `string` | `必传` | 服务器 ID。 |
| `userId` | `string` | `必传` | 用户 ID，与 SDK 的 `User.Id` 相同。 |
| `until` | `DateTimeOffset?` | `必传` | 结束时间；null 表示永久或解除。 |
| `reason` | `string?` | `null` | 写入服务器审计日志的原因；null 不写。 |
| `cancellationToken` | `CancellationToken` | `default` | 取消令牌；默认不主动取消。 |

### 返回

`Task`。完成后无返回值。

## RemoveGuildMemberAsync

把成员移出服务器（可再次通过邀请加入）。

```csharp
Task RemoveGuildMemberAsync(
    string guildId,
    string userId,
    string? reason = null,
    CancellationToken cancellationToken = default
);
```

### 参数

| 参数 | C# 类型 | 默认值 | 说明 |
| --- | --- | --- | --- |
| `guildId` | `string` | `必传` | 服务器 ID。 |
| `userId` | `string` | `必传` | 用户 ID，与 SDK 的 `User.Id` 相同。 |
| `reason` | `string?` | `null` | 写入服务器审计日志的原因；null 不写。 |
| `cancellationToken` | `CancellationToken` | `default` | 取消令牌；默认不主动取消。 |

### 返回

`Task`。完成后无返回值。

## BanAsync

封禁成员，并删除其最近 `deleteMessages` 内的消息（最长 7 天）。

```csharp
Task BanAsync(
    string guildId,
    string userId,
    TimeSpan? deleteMessages = null,
    string? reason = null,
    CancellationToken cancellationToken = default
);
```

### 参数

| 参数 | C# 类型 | 默认值 | 说明 |
| --- | --- | --- | --- |
| `guildId` | `string` | `必传` | 服务器 ID。 |
| `userId` | `string` | `必传` | 用户 ID，与 SDK 的 `User.Id` 相同。 |
| `deleteMessages` | `TimeSpan?` | `null` | 同时删除该成员在此时长内的消息，最长 7 天。 |
| `reason` | `string?` | `null` | 写入服务器审计日志的原因；null 不写。 |
| `cancellationToken` | `CancellationToken` | `default` | 取消令牌；默认不主动取消。 |

### 返回

`Task`。完成后无返回值。

## UnbanAsync

解除封禁。

```csharp
Task UnbanAsync(
    string guildId,
    string userId,
    string? reason = null,
    CancellationToken cancellationToken = default
);
```

### 参数

| 参数 | C# 类型 | 默认值 | 说明 |
| --- | --- | --- | --- |
| `guildId` | `string` | `必传` | 服务器 ID。 |
| `userId` | `string` | `必传` | 用户 ID，与 SDK 的 `User.Id` 相同。 |
| `reason` | `string?` | `null` | 写入服务器审计日志的原因；null 不写。 |
| `cancellationToken` | `CancellationToken` | `default` | 取消令牌；默认不主动取消。 |

### 返回

`Task`。完成后无返回值。

## GetBansAsync

查询封禁列表。

```csharp
Task<IReadOnlyList<DiscordBan>> GetBansAsync(
    string guildId,
    CancellationToken cancellationToken = default
);
```

### 参数

| 参数 | C# 类型 | 默认值 | 说明 |
| --- | --- | --- | --- |
| `guildId` | `string` | `必传` | 服务器 ID。 |
| `cancellationToken` | `CancellationToken` | `default` | 取消令牌；默认不主动取消。 |

### 返回

`Task<IReadOnlyList<DiscordBan>>`。异步返回 `IReadOnlyList<DiscordBan>`。

## GetInvitesAsync

查询服务器的邀请。

```csharp
Task<IReadOnlyList<DiscordInvite>> GetInvitesAsync(
    string guildId,
    CancellationToken cancellationToken = default
);
```

### 参数

| 参数 | C# 类型 | 默认值 | 说明 |
| --- | --- | --- | --- |
| `guildId` | `string` | `必传` | 服务器 ID。 |
| `cancellationToken` | `CancellationToken` | `default` | 取消令牌；默认不主动取消。 |

### 返回

`Task<IReadOnlyList<DiscordInvite>>`。异步返回 `IReadOnlyList<DiscordInvite>`。

## CreateInviteAsync

为频道创建邀请。

```csharp
Task<DiscordInvite> CreateInviteAsync(
    string channelId,
    DiscordInviteOptions? options = null,
    string? reason = null,
    CancellationToken cancellationToken = default
);
```

### 参数

| 参数 | C# 类型 | 默认值 | 说明 |
| --- | --- | --- | --- |
| `channelId` | `string` | `必传` | 频道 ID。 |
| `options` | `DiscordInviteOptions?` | `null` | 可选设置；null 使用默认值。 |
| `reason` | `string?` | `null` | 写入服务器审计日志的原因；null 不写。 |
| `cancellationToken` | `CancellationToken` | `default` | 取消令牌；默认不主动取消。 |

### 返回

`Task<DiscordInvite>`。异步返回 `DiscordInvite`。

## DeleteInviteAsync

删除邀请。

```csharp
Task DeleteInviteAsync(
    string code,
    string? reason = null,
    CancellationToken cancellationToken = default
);
```

### 参数

| 参数 | C# 类型 | 默认值 | 说明 |
| --- | --- | --- | --- |
| `code` | `string` | `必传` | 邀请码。 |
| `reason` | `string?` | `null` | 写入服务器审计日志的原因；null 不写。 |
| `cancellationToken` | `CancellationToken` | `default` | 取消令牌；默认不主动取消。 |

### 返回

`Task`。完成后无返回值。

## GetEmojisAsync

查询服务器自定义表情。

```csharp
Task<IReadOnlyList<DiscordEmoji>> GetEmojisAsync(
    string guildId,
    CancellationToken cancellationToken = default
);
```

### 参数

| 参数 | C# 类型 | 默认值 | 说明 |
| --- | --- | --- | --- |
| `guildId` | `string` | `必传` | 服务器 ID。 |
| `cancellationToken` | `CancellationToken` | `default` | 取消令牌；默认不主动取消。 |

### 返回

`Task<IReadOnlyList<DiscordEmoji>>`。异步返回 `IReadOnlyList<DiscordEmoji>`。

## CreateThreadAsync

创建子区：提供 `messageId` 时从该消息创建，否则在频道中创建独立子区。 返回的子区 ID 可直接作为 SDK `ChannelType.Thread` 频道使用。

```csharp
Task<DiscordChannel> CreateThreadAsync(
    string channelId,
    string name,
    string? messageId = null,
    bool isPrivate = false,
    TimeSpan? autoArchive = null,
    CancellationToken cancellationToken = default
);
```

### 参数

| 参数 | C# 类型 | 默认值 | 说明 |
| --- | --- | --- | --- |
| `channelId` | `string` | `必传` | 频道 ID。 |
| `name` | `string` | `必传` | 名称；null 表示不修改。 |
| `messageId` | `string?` | `null` | 消息 ID，与 SDK 的 `MessageId` 相同。 |
| `isPrivate` | `bool` | `false` | 创建私密子区。 |
| `autoArchive` | `TimeSpan?` | `null` | 自动归档时间：1 小时、1 天、3 天或 7 天。 |
| `cancellationToken` | `CancellationToken` | `default` | 取消令牌；默认不主动取消。 |

### 返回

`Task<DiscordChannel>`。异步返回 `DiscordChannel`。

## JoinThreadAsync

让当前账号加入子区。

```csharp
Task JoinThreadAsync(string threadId, CancellationToken cancellationToken = default);
```

### 参数

| 参数 | C# 类型 | 默认值 | 说明 |
| --- | --- | --- | --- |
| `threadId` | `string` | `必传` | 子区 ID。 |
| `cancellationToken` | `CancellationToken` | `default` | 取消令牌；默认不主动取消。 |

### 返回

`Task`。完成后无返回值。

## LeaveThreadAsync

让当前账号离开子区。

```csharp
Task LeaveThreadAsync(string threadId, CancellationToken cancellationToken = default);
```

### 参数

| 参数 | C# 类型 | 默认值 | 说明 |
| --- | --- | --- | --- |
| `threadId` | `string` | `必传` | 子区 ID。 |
| `cancellationToken` | `CancellationToken` | `default` | 取消令牌；默认不主动取消。 |

### 返回

`Task`。完成后无返回值。

## AddThreadMemberAsync

把成员加入子区。

```csharp
Task AddThreadMemberAsync(
    string threadId,
    string userId,
    CancellationToken cancellationToken = default
);
```

### 参数

| 参数 | C# 类型 | 默认值 | 说明 |
| --- | --- | --- | --- |
| `threadId` | `string` | `必传` | 子区 ID。 |
| `userId` | `string` | `必传` | 用户 ID，与 SDK 的 `User.Id` 相同。 |
| `cancellationToken` | `CancellationToken` | `default` | 取消令牌；默认不主动取消。 |

### 返回

`Task`。完成后无返回值。

## 相关类型

见 [C# 类型参考](./types)。`required` 属性创建对象时必须填写；`?` 表示可空。
