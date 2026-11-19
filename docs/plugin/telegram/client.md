# 账号信息（ITelegramClientApi）

命名空间：`ShiroBot.Model.Telegram`。Telegram Model ABI：`1.1.0.0`。两种账号都实现。

当前实例的账号与传输信息，两种账号都实现。

```csharp
var api = Context.GetAdapterExtension<ITelegramClientApi>();
if (api is null) return;
```

取得接口不代表每个方法都已实现；未实现的方法抛出 `NotSupportedException`，调用还受平台权限和参数约束。

## AccountKind

属性。

```csharp
TelegramAccountKind AccountKind { get; }
```

接口默认值：`TelegramAccountKind.Bot`；适配器按实际情况返回。

## Transport

属性。

```csharp
TelegramTransport Transport { get; }
```

接口默认值：`TelegramTransport.BotApi`；适配器按实际情况返回。

## MaxUploadBytes

单个文件的上传上限（字节）：官方 Bot API 为 50 MB，自建 Bot API Server 与 MTProto 为 2000 MB。

```csharp
long MaxUploadBytes { get; }
```

接口默认值：`50L * 1024 * 1024`；适配器按实际情况返回。

## MaxDownloadBytes

单个文件的下载上限（字节）：官方 Bot API 为 20 MB，自建 Bot API Server 与 MTProto 为 2000 MB。

```csharp
long MaxDownloadBytes { get; }
```

接口默认值：`20L * 1024 * 1024`；适配器按实际情况返回。

## GetMeAsync

当前登录账号的完整资料。

```csharp
Task<TelegramUser> GetMeAsync(CancellationToken cancellationToken = default);
```

### 参数

| 参数 | C# 类型 | 默认值 | 说明 |
| --- | --- | --- | --- |
| `cancellationToken` | `CancellationToken` | `default` | 取消令牌；默认不主动取消。 |

### 返回

`Task<TelegramUser>`。异步返回 `TelegramUser`。

## FindKnownUser

已在当前实例中见过的用户。机器人不能按 ID 查询任意用户，只能返回见过的。

```csharp
TelegramUser? FindKnownUser(string userId);
```

### 参数

| 参数 | C# 类型 | 默认值 | 说明 |
| --- | --- | --- | --- |
| `userId` | `string` | `必传` | 用户 ID，与 SDK 的 `User.Id` 相同。 |

### 返回

`TelegramUser?`。同步返回结果。

## 相关类型

见 [C# 类型参考](./types)。`required` 属性创建对象时必须填写；`?` 表示可空。
