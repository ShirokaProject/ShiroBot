# 官方私聊（IQOfficialDirectMessageApi）

命名空间：`ShiroBot.Model.QQ`。QQ Model ABI：`1.0.0.0`。

```csharp
var api = Context.GetAdapterExtension<IQOfficialDirectMessageApi>();
if (api is null) return;
```

接口可获取不表示每个方法都已实现；未实现的操作会抛出 `NotSupportedException`。平台权限与参数错误仍可能导致调用失败。

## SendTypingAsync

发送私聊输入状态，使用被动回复凭据。

```csharp
Task SendTypingAsync(
    QOfficialMessageTarget target,
    QOfficialMessageReply reply,
    TimeSpan duration,
    CancellationToken cancellationToken = default
);
```

### 参数

| 参数 | C# 类型 | 默认值 | 说明 |
| --- | --- | --- | --- |
| `target` | `QOfficialMessageTarget` | `必传` | 官方消息目标，含场景与 ID。 |
| `reply` | `QOfficialMessageReply` | `必传` | 必传的被动回复凭据，来自消息或事件。 |
| `duration` | `TimeSpan` | `必传` | 禁言或输入状态的持续时间；零禁言时长表示解除禁言。 |
| `cancellationToken` | `CancellationToken` | `default` | 取消令牌；默认不主动取消。 |

### 返回

`Task`。异步完成后不返回业务值。

## BeginStream

创建流式回复会话，结束后需释放。

```csharp
IQOfficialMessageStream BeginStream(
    QOfficialMessageTarget target,
    QOfficialMessageReply reply,
    QOfficialStreamContentType contentType = QOfficialStreamContentType.Text
);
```

### 参数

| 参数 | C# 类型 | 默认值 | 说明 |
| --- | --- | --- | --- |
| `target` | `QOfficialMessageTarget` | `必传` | 官方消息目标，含场景与 ID。 |
| `reply` | `QOfficialMessageReply` | `必传` | 必传的被动回复凭据，来自消息或事件。 |
| `contentType` | `QOfficialStreamContentType` | `QOfficialStreamContentType.Text` | 流式回复内容类型。 |

### 返回

`IQOfficialMessageStream`。返回可异步释放的流式回复会话。

## 相关类型

见[模型、消息段与事件类型](./types)。完整声明中的 `required` 字段创建对象时必须填写；`?` 表示可空。
