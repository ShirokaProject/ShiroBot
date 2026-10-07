# 原生消息（IQMessageApi）

命名空间：`ShiroBot.Model.QQ`。QQ Model ABI：`1.0.0.0`。

```csharp
var api = Context.GetAdapterExtension<IQMessageApi>();
if (api is null) return;
```

接口可获取不表示每个方法都已实现；未实现的操作会抛出 `NotSupportedException`。平台权限与参数错误仍可能导致调用失败。

## SendMessageAsync

用 QQ 原生段发送消息(LightApp、合并转发等核心模型未覆盖的内容)。

```csharp
Task<string> SendMessageAsync(
    QMessageScene scene,
    string peerId,
    IReadOnlyList<QOutgoingSegment> segments,
    CancellationToken cancellationToken = default
);
```

### 参数

| 参数 | C# 类型 | 默认值 | 说明 |
| --- | --- | --- | --- |
| `scene` | `QMessageScene` | `必传` | 好友、群或临时会话场景。 |
| `peerId` | `string` | `必传` | 会话 ID，与 scene 对应。 |
| `segments` | `IReadOnlyList<QOutgoingSegment>` | `必传` | QQ 原生出站消息段。 |
| `cancellationToken` | `CancellationToken` | `default` | 取消令牌；默认不主动取消。 |

### 返回

`Task<string>`。返回发送后的消息 ID。

## SendMessageDetailedAsync

发送 QQ 原生消息，并返回消息序列号和发送时间。

```csharp
Task<QSentMessage> SendMessageDetailedAsync(
    QMessageScene scene,
    string peerId,
    IReadOnlyList<QOutgoingSegment> segments,
    CancellationToken cancellationToken = default
);
```

### 参数

| 参数 | C# 类型 | 默认值 | 说明 |
| --- | --- | --- | --- |
| `scene` | `QMessageScene` | `必传` | 好友、群或临时会话场景。 |
| `peerId` | `string` | `必传` | 会话 ID，与 scene 对应。 |
| `segments` | `IReadOnlyList<QOutgoingSegment>` | `必传` | QQ 原生出站消息段。 |
| `cancellationToken` | `CancellationToken` | `default` | 取消令牌；默认不主动取消。 |

### 返回

`Task<QSentMessage>`。返回消息 ID 与发送时间。

## GetMessageAsync

获取单条消息(QQ 原生形态)。

```csharp
Task<QIncomingMessage?> GetMessageAsync(
    QMessageScene scene,
    string peerId,
    string messageId,
    CancellationToken cancellationToken = default
);
```

### 参数

| 参数 | C# 类型 | 默认值 | 说明 |
| --- | --- | --- | --- |
| `scene` | `QMessageScene` | `必传` | 好友、群或临时会话场景。 |
| `peerId` | `string` | `必传` | 会话 ID，与 scene 对应。 |
| `messageId` | `string` | `必传` | 当前会话的消息 ID。 |
| `cancellationToken` | `CancellationToken` | `default` | 取消令牌；默认不主动取消。 |

### 返回

`Task<QIncomingMessage?>`。返回找到的原生消息；不存在时为 null。

## GetHistoryMessagesAsync

获取历史消息(QQ 原生形态)。返回消息与下一页起始序号。

```csharp
Task<(IReadOnlyList<QIncomingMessage> Messages, string? NextCursor)> GetHistoryMessagesAsync(
    QMessageScene scene,
    string peerId,
    string? cursor = null,
    int limit = 20,
    CancellationToken cancellationToken = default
);
```

### 参数

| 参数 | C# 类型 | 默认值 | 说明 |
| --- | --- | --- | --- |
| `scene` | `QMessageScene` | `必传` | 好友、群或临时会话场景。 |
| `peerId` | `string` | `必传` | 会话 ID，与 scene 对应。 |
| `cursor` | `string?` | `null` | 上一页返回的 NextCursor；null 从第一页开始。 |
| `limit` | `int` | `20` | 本页请求数量。 |
| `cancellationToken` | `CancellationToken` | `default` | 取消令牌；默认不主动取消。 |

### 返回

`Task<(IReadOnlyList<QIncomingMessage> Messages, string? NextCursor)>`。返回消息列表 Messages 和下一页游标 NextCursor。

## RecallMessageAsync

撤回消息。

```csharp
Task RecallMessageAsync(
    QMessageScene scene,
    string peerId,
    string messageId,
    CancellationToken cancellationToken = default
);
```

### 参数

| 参数 | C# 类型 | 默认值 | 说明 |
| --- | --- | --- | --- |
| `scene` | `QMessageScene` | `必传` | 好友、群或临时会话场景。 |
| `peerId` | `string` | `必传` | 会话 ID，与 scene 对应。 |
| `messageId` | `string` | `必传` | 当前会话的消息 ID。 |
| `cancellationToken` | `CancellationToken` | `default` | 取消令牌；默认不主动取消。 |

### 返回

`Task`。异步完成后不返回业务值。

## GetResourceTempUrlAsync

把接收到的资源 ID 解析为临时下载 URL。

```csharp
Task<string> GetResourceTempUrlAsync(
    string resourceId,
    CancellationToken cancellationToken = default
);
```

### 参数

| 参数 | C# 类型 | 默认值 | 说明 |
| --- | --- | --- | --- |
| `resourceId` | `string` | `必传` | 收到的资源 ID。 |
| `cancellationToken` | `CancellationToken` | `default` | 取消令牌；默认不主动取消。 |

### 返回

`Task<string>`。返回下载链接。

## GetForwardedMessagesAsync

读取合并转发中的原生消息。

```csharp
Task<IReadOnlyList<QForwardedIncomingMessage>> GetForwardedMessagesAsync(
    string forwardId,
    CancellationToken cancellationToken = default
);
```

### 参数

| 参数 | C# 类型 | 默认值 | 说明 |
| --- | --- | --- | --- |
| `forwardId` | `string` | `必传` | 合并转发资源 ID。 |
| `cancellationToken` | `CancellationToken` | `default` | 取消令牌；默认不主动取消。 |

### 返回

`Task<IReadOnlyList<QForwardedIncomingMessage>>`。等待异步完成后取得签名所列模型或列表。

## MarkAsReadAsync

将指定会话标记为已读。

```csharp
Task MarkAsReadAsync(
    QMessageScene scene,
    string peerId,
    string messageId,
    CancellationToken cancellationToken = default
);
```

### 参数

| 参数 | C# 类型 | 默认值 | 说明 |
| --- | --- | --- | --- |
| `scene` | `QMessageScene` | `必传` | 好友、群或临时会话场景。 |
| `peerId` | `string` | `必传` | 会话 ID，与 scene 对应。 |
| `messageId` | `string` | `必传` | 当前会话的消息 ID。 |
| `cancellationToken` | `CancellationToken` | `default` | 取消令牌；默认不主动取消。 |

### 返回

`Task`。异步完成后不返回业务值。

## 相关类型

见[模型、消息段与事件类型](./types)。完整声明中的 `required` 字段创建对象时必须填写；`?` 表示可空。
