# 账号操作（IDiscordAccountApi）

命名空间：`ShiroBot.Model.Discord`。仅用户账号实现；机器人适配器上为 null。Discord 服务条款禁止自动化用户账号（self-bot），账号可能被封禁，是否使用由部署者自行承担。

用户账号特有的操作，仅用户账号（机器人适配器不实现）。好友、历史消息、成员列表和退出服务器（`LeaveChannelAsync`）使用 SDK 通用服务。 Discord 服务条款禁止自动化用户账号，账号可能被封禁。

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

## MarkAsReadAsync

把频道标记为已读；`maxMessageId` 为 null 时读到最新消息。

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
| `channel` | `Channel` | `必传` | 目标 SDK 频道；私聊为 `Channel.Direct(用户 ID)`，适配器自动打开私聊。 |
| `maxMessageId` | `string?` | `null` | 读到的最后一条消息 ID；null 为最新。 |
| `cancellationToken` | `CancellationToken` | `default` | 取消令牌；默认不主动取消。 |

### 返回

`Task`。完成后无返回值。

## 相关类型

见 [C# 类型参考](./types)。`required` 属性创建对象时必须填写；`?` 表示可空。
