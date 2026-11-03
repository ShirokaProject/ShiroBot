# 好友（IQFriendApi）

命名空间：`ShiroBot.Model.QQ`。QQ Model ABI：`1.0.0.0`。

```csharp
var api = Context.GetAdapterExtension<IQFriendApi>();
if (api is null) return;
```

未实现的方法抛出 `NotSupportedException`；调用还受平台权限和参数约束。

## SendNudgeAsync

发送戳一戳。

```csharp
Task SendNudgeAsync(
    string userId,
    bool isSelf = false,
    CancellationToken cancellationToken = default
);
```

### 参数

| 参数 | C# 类型 | 默认值 | 说明 |
| --- | --- | --- | --- |
| `userId` | `string` | `必传` | 当前实例内的用户 ID。 |
| `isSelf` | `bool` | `false` | 是否戳机器人自己。 |
| `cancellationToken` | `CancellationToken` | `default` | 取消令牌；默认不主动取消。 |

### 返回

`Task`。完成后无返回值。

## SendProfileLikeAsync

给用户资料名片点赞。

```csharp
Task SendProfileLikeAsync(
    string userId,
    int count = 1,
    CancellationToken cancellationToken = default
);
```

### 参数

| 参数 | C# 类型 | 默认值 | 说明 |
| --- | --- | --- | --- |
| `userId` | `string` | `必传` | 当前实例内的用户 ID。 |
| `count` | `int` | `1` | 请求执行的次数。 |
| `cancellationToken` | `CancellationToken` | `default` | 取消令牌；默认不主动取消。 |

### 返回

`Task`。完成后无返回值。

## DeleteFriendAsync

删除好友。

```csharp
Task DeleteFriendAsync(string userId, CancellationToken cancellationToken = default);
```

### 参数

| 参数 | C# 类型 | 默认值 | 说明 |
| --- | --- | --- | --- |
| `userId` | `string` | `必传` | 当前实例内的用户 ID。 |
| `cancellationToken` | `CancellationToken` | `default` | 取消令牌；默认不主动取消。 |

### 返回

`Task`。完成后无返回值。

## GetFriendRequestsAsync

获取好友请求列表。

```csharp
Task<IReadOnlyList<QFriendRequest>> GetFriendRequestsAsync(
    int limit = 20,
    bool isFiltered = false,
    CancellationToken cancellationToken = default
);
```

### 参数

| 参数 | C# 类型 | 默认值 | 说明 |
| --- | --- | --- | --- |
| `limit` | `int` | `20` | 本页请求数量。 |
| `isFiltered` | `bool` | `false` | 是否处理被协议端过滤的请求。 |
| `cancellationToken` | `CancellationToken` | `default` | 取消令牌；默认不主动取消。 |

### 返回

`Task<IReadOnlyList<QFriendRequest>>`。返回好友请求列表。

## AcceptFriendRequestAsync

接受好友请求。initiatorUid 来自 QFriendRequest.InitiatorUid 或好友请求事件的 Token。

```csharp
Task AcceptFriendRequestAsync(
    string initiatorUid,
    bool isFiltered = false,
    CancellationToken cancellationToken = default
);
```

### 参数

| 参数 | C# 类型 | 默认值 | 说明 |
| --- | --- | --- | --- |
| `initiatorUid` | `string` | `必传` | 好友请求返回的发起者 UID。 |
| `isFiltered` | `bool` | `false` | 是否处理被协议端过滤的请求。 |
| `cancellationToken` | `CancellationToken` | `default` | 取消令牌；默认不主动取消。 |

### 返回

`Task`。完成后无返回值。

## RejectFriendRequestAsync

拒绝好友申请。

```csharp
Task RejectFriendRequestAsync(
    string initiatorUid,
    bool isFiltered = false,
    string? reason = null,
    CancellationToken cancellationToken = default
);
```

### 参数

| 参数 | C# 类型 | 默认值 | 说明 |
| --- | --- | --- | --- |
| `initiatorUid` | `string` | `必传` | 好友请求返回的发起者 UID。 |
| `isFiltered` | `bool` | `false` | 是否处理被协议端过滤的请求。 |
| `reason` | `string?` | `null` | 可选的拒绝理由。 |
| `cancellationToken` | `CancellationToken` | `default` | 取消令牌；默认不主动取消。 |

### 返回

`Task`。完成后无返回值。

## 相关类型

见[模型、消息段与事件类型](./types)。完整声明中的 `required` 字段创建对象时必须填写；`?` 表示可空。
