# 账号操作（IDiscordAccountApi）

命名空间：`ShiroBot.Model.Discord`。Discord Model ABI：`1.1.0.0`。仅用户账号实现；机器人适配器上为 null。当前为预留契约。Discord 服务条款禁止自动化用户账号（self-bot），账号可能被封禁，是否使用由部署者自行承担。

用户账号特有的操作，仅用户账号（机器人适配器不实现）。好友、历史消息和成员列表使用 SDK 通用服务。 Discord 服务条款禁止自动化用户账号，账号可能被封禁。

```csharp
var api = Context.GetAdapterExtension<IDiscordAccountApi>();
if (api is null) return;
```

取得接口不代表每个方法都已实现；未实现的方法抛出 `NotSupportedException`，调用还受平台权限和参数约束。

## GetPrivateChannelsAsync

当前账号的私聊和群组私聊。

```csharp
Task<IReadOnlyList<DiscordChannel>> GetPrivateChannelsAsync(
    CancellationToken cancellationToken = default
);
```

### 参数

| 参数 | C# 类型 | 默认值 | 说明 |
| --- | --- | --- | --- |
| `cancellationToken` | `CancellationToken` | `default` | 取消令牌；默认不主动取消。 |

### 返回

`Task<IReadOnlyList<DiscordChannel>>`。异步返回 `IReadOnlyList<DiscordChannel>`。

## JoinGuildAsync

通过邀请码加入服务器。

```csharp
Task<DiscordGuild> JoinGuildAsync(string inviteCode, CancellationToken cancellationToken = default);
```

### 参数

| 参数 | C# 类型 | 默认值 | 说明 |
| --- | --- | --- | --- |
| `inviteCode` | `string` | `必传` | 邀请码。 |
| `cancellationToken` | `CancellationToken` | `default` | 取消令牌；默认不主动取消。 |

### 返回

`Task<DiscordGuild>`。异步返回 `DiscordGuild`。

## LeaveGuildAsync

离开服务器。

```csharp
Task LeaveGuildAsync(string guildId, CancellationToken cancellationToken = default);
```

### 参数

| 参数 | C# 类型 | 默认值 | 说明 |
| --- | --- | --- | --- |
| `guildId` | `string` | `必传` | 服务器 ID。 |
| `cancellationToken` | `CancellationToken` | `default` | 取消令牌；默认不主动取消。 |

### 返回

`Task`。完成后无返回值。

## AcknowledgeAsync

把频道标记为已读到 `messageId`。

```csharp
Task AcknowledgeAsync(
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

## 相关类型

见 [C# 类型参考](./types)。`required` 属性创建对象时必须填写；`?` 表示可空。
