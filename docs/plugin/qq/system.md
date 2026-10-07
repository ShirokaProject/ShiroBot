# 账号与资料（IQSystemApi）

命名空间：`ShiroBot.Model.QQ`。QQ Model ABI：`1.0.0.0`。

```csharp
var api = Context.GetAdapterExtension<IQSystemApi>();
if (api is null) return;
```

接口可获取不表示每个方法都已实现；未实现的操作会抛出 `NotSupportedException`。平台权限与参数错误仍可能导致调用失败。

## GetUserProfileAsync

查询用户资料。

```csharp
Task<QUserProfile> GetUserProfileAsync(string userId, CancellationToken cancellationToken = default);
```

### 参数

| 参数 | C# 类型 | 默认值 | 说明 |
| --- | --- | --- | --- |
| `userId` | `string` | `必传` | 当前实例内的用户 ID。 |
| `cancellationToken` | `CancellationToken` | `default` | 取消令牌；默认不主动取消。 |

### 返回

`Task<QUserProfile>`。等待异步完成后取得签名所列模型或列表。

## GetFriendListAsync

查询好友列表。

```csharp
Task<IReadOnlyList<QFriend>> GetFriendListAsync(
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

`Task<IReadOnlyList<QFriend>>`。等待异步完成后取得签名所列模型或列表。

## GetFriendInfoAsync

查询好友资料。

```csharp
Task<QFriend> GetFriendInfoAsync(
    string userId,
    bool noCache = false,
    CancellationToken cancellationToken = default
);
```

### 参数

| 参数 | C# 类型 | 默认值 | 说明 |
| --- | --- | --- | --- |
| `userId` | `string` | `必传` | 当前实例内的用户 ID。 |
| `noCache` | `bool` | `false` | 是否绕过协议端缓存。 |
| `cancellationToken` | `CancellationToken` | `default` | 取消令牌；默认不主动取消。 |

### 返回

`Task<QFriend>`。等待异步完成后取得签名所列模型或列表。

## GetPeerPinsAsync

获取置顶的好友和群。

```csharp
Task<(IReadOnlyList<QFriend> Friends, IReadOnlyList<QGroup> Groups)> GetPeerPinsAsync(
    CancellationToken cancellationToken = default
);
```

### 参数

| 参数 | C# 类型 | 默认值 | 说明 |
| --- | --- | --- | --- |
| `cancellationToken` | `CancellationToken` | `default` | 取消令牌；默认不主动取消。 |

### 返回

`Task<(IReadOnlyList<QFriend> Friends, IReadOnlyList<QGroup> Groups)>`。返回置顶好友 Friends 与置顶群 Groups。

## SetAvatarAsync

修改机器人账号头像。

```csharp
Task SetAvatarAsync(string imageUri, CancellationToken cancellationToken = default);
```

### 参数

| 参数 | C# 类型 | 默认值 | 说明 |
| --- | --- | --- | --- |
| `imageUri` | `string` | `必传` | 图片 URI，由适配器解析。 |
| `cancellationToken` | `CancellationToken` | `default` | 取消令牌；默认不主动取消。 |

### 返回

`Task`。异步完成后不返回业务值。

## SetNicknameAsync

修改机器人账号昵称。

```csharp
Task SetNicknameAsync(string nickname, CancellationToken cancellationToken = default);
```

### 参数

| 参数 | C# 类型 | 默认值 | 说明 |
| --- | --- | --- | --- |
| `nickname` | `string` | `必传` | 账号昵称。 |
| `cancellationToken` | `CancellationToken` | `default` | 取消令牌；默认不主动取消。 |

### 返回

`Task`。异步完成后不返回业务值。

## SetBioAsync

修改机器人账号简介。

```csharp
Task SetBioAsync(string bio, CancellationToken cancellationToken = default);
```

### 参数

| 参数 | C# 类型 | 默认值 | 说明 |
| --- | --- | --- | --- |
| `bio` | `string` | `必传` | 账号简介。 |
| `cancellationToken` | `CancellationToken` | `default` | 取消令牌；默认不主动取消。 |

### 返回

`Task`。异步完成后不返回业务值。

## GetCookiesAsync

查询指定域名的账号 Cookie。

```csharp
Task<string> GetCookiesAsync(string domain, CancellationToken cancellationToken = default);
```

### 参数

| 参数 | C# 类型 | 默认值 | 说明 |
| --- | --- | --- | --- |
| `domain` | `string` | `必传` | 需要查询 Cookie 的域名。 |
| `cancellationToken` | `CancellationToken` | `default` | 取消令牌；默认不主动取消。 |

### 返回

`Task<string>`。等待异步完成后取得签名所列模型或列表。

## GetCsrfTokenAsync

查询账号 CSRF Token。

```csharp
Task<string> GetCsrfTokenAsync(CancellationToken cancellationToken = default);
```

### 参数

| 参数 | C# 类型 | 默认值 | 说明 |
| --- | --- | --- | --- |
| `cancellationToken` | `CancellationToken` | `default` | 取消令牌；默认不主动取消。 |

### 返回

`Task<string>`。等待异步完成后取得签名所列模型或列表。

## GetLoginInfoAsync

获取登录账号信息。

```csharp
Task<QLoginInfo> GetLoginInfoAsync(CancellationToken cancellationToken = default);
```

### 参数

| 参数 | C# 类型 | 默认值 | 说明 |
| --- | --- | --- | --- |
| `cancellationToken` | `CancellationToken` | `default` | 取消令牌；默认不主动取消。 |

### 返回

`Task<QLoginInfo>`。等待异步完成后取得签名所列模型或列表。

## GetImplInfoAsync

获取协议实现端信息(实现名/版本/QQ协议类型)。

```csharp
Task<QImplInfo> GetImplInfoAsync(CancellationToken cancellationToken = default);
```

### 参数

| 参数 | C# 类型 | 默认值 | 说明 |
| --- | --- | --- | --- |
| `cancellationToken` | `CancellationToken` | `default` | 取消令牌；默认不主动取消。 |

### 返回

`Task<QImplInfo>`。等待异步完成后取得签名所列模型或列表。

## GetCustomFaceUrlListAsync

获取收藏表情 URL 列表。

```csharp
Task<IReadOnlyList<string>> GetCustomFaceUrlListAsync(CancellationToken cancellationToken = default);
```

### 参数

| 参数 | C# 类型 | 默认值 | 说明 |
| --- | --- | --- | --- |
| `cancellationToken` | `CancellationToken` | `default` | 取消令牌；默认不主动取消。 |

### 返回

`Task<IReadOnlyList<string>>`。等待异步完成后取得签名所列模型或列表。

## SetPeerPinAsync

设置会话置顶。

```csharp
Task SetPeerPinAsync(
    QMessageScene scene,
    string peerId,
    bool isPinned = true,
    CancellationToken cancellationToken = default
);
```

### 参数

| 参数 | C# 类型 | 默认值 | 说明 |
| --- | --- | --- | --- |
| `scene` | `QMessageScene` | `必传` | 好友、群或临时会话场景。 |
| `peerId` | `string` | `必传` | 会话 ID，与 scene 对应。 |
| `isPinned` | `bool` | `true` | 是否置顶会话。 |
| `cancellationToken` | `CancellationToken` | `default` | 取消令牌；默认不主动取消。 |

### 返回

`Task`。异步完成后不返回业务值。

## 相关类型

见[模型、消息段与事件类型](./types)。完整声明中的 `required` 字段创建对象时必须填写；`?` 表示可空。
