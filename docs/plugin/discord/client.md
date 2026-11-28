# 账号信息（IDiscordClientApi）

命名空间：`ShiroBot.Model.Discord`。两种账号都实现。

当前实例的账号信息、自身资料、用户查询与在线状态，两种账号都实现。

```csharp
var api = Context.GetAdapterExtension<IDiscordClientApi>();
if (api is null) return;
```

取得接口不代表每个方法都已实现；未实现的方法抛出 `NotSupportedException`，调用还受平台权限和参数约束。

## AccountKind

属性。

```csharp
DiscordAccountKind AccountKind { get; }
```

接口默认值：`DiscordAccountKind.Bot`；适配器按实际情况返回。

## MaxUploadBytes

单个附件的上传上限（字节）。机器人默认 10 MB，服务器助力等级可提高；用户账号取决于订阅。

```csharp
long MaxUploadBytes { get; }
```

接口默认值：`10L * 1024 * 1024`；适配器按实际情况返回。

## GetCurrentUserAsync

当前登录账号的资料。

```csharp
Task<DiscordUser> GetCurrentUserAsync(CancellationToken cancellationToken = default);
```

### 参数

| 参数 | C# 类型 | 默认值 | 说明 |
| --- | --- | --- | --- |
| `cancellationToken` | `CancellationToken` | `default` | 取消令牌；默认不主动取消。 |

### 返回

`Task<DiscordUser>`。异步返回 `DiscordUser`。

## FetchUserAsync

查询任意用户的公开资料。

```csharp
Task<DiscordUser> FetchUserAsync(string userId, CancellationToken cancellationToken = default);
```

### 参数

| 参数 | C# 类型 | 默认值 | 说明 |
| --- | --- | --- | --- |
| `userId` | `string` | `必传` | 用户 ID，与 SDK 的 `User.Id` 相同。 |
| `cancellationToken` | `CancellationToken` | `default` | 取消令牌；默认不主动取消。 |

### 返回

`Task<DiscordUser>`。异步返回 `DiscordUser`。

## SetPresenceAsync

通过 Gateway 更新在线状态；断线重连后由适配器重新发送。

```csharp
Task SetPresenceAsync(DiscordPresence presence, CancellationToken cancellationToken = default);
```

### 参数

| 参数 | C# 类型 | 默认值 | 说明 |
| --- | --- | --- | --- |
| `presence` | `DiscordPresence` | `必传` | 在线状态与活动。 |
| `cancellationToken` | `CancellationToken` | `default` | 取消令牌；默认不主动取消。 |

### 返回

`Task`。完成后无返回值。

## ModifyCurrentUserAsync

修改当前账号的用户名或头像；null 表示不修改。Discord 对用户名修改有频率限制。

```csharp
Task<DiscordUser> ModifyCurrentUserAsync(
    string? username = null,
    string? avatarUri = null,
    CancellationToken cancellationToken = default
);
```

### 参数

| 参数 | C# 类型 | 默认值 | 说明 |
| --- | --- | --- | --- |
| `username` | `string?` | `null` | 新用户名；null 不修改。 |
| `avatarUri` | `string?` | `null` | 新头像地址；null 不修改。 |
| `cancellationToken` | `CancellationToken` | `default` | 取消令牌；默认不主动取消。 |

### 返回

`Task<DiscordUser>`。异步返回 `DiscordUser`。

## 相关类型

见 [C# 类型参考](./types)。`required` 属性创建对象时必须填写；`?` 表示可空。
