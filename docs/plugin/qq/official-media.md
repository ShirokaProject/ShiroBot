# 官方媒体（IQOfficialMediaApi）

命名空间：`ShiroBot.Model.QQ`。QQ Model ABI：`1.0.0.0`。

```csharp
var api = Context.GetAdapterExtension<IQOfficialMediaApi>();
if (api is null) return;
```

接口可获取不表示每个方法都已实现；未实现的操作会抛出 `NotSupportedException`。平台权限与参数错误仍可能导致调用失败。

## UploadAsync

上传本地媒体并返回可复用的官方 file_info。

```csharp
Task<QOfficialMedia> UploadAsync(
    QOfficialMessageTarget target,
    QOfficialMediaType type,
    Stream content,
    string fileName,
    CancellationToken cancellationToken = default
);
```

### 参数

| 参数 | C# 类型 | 默认值 | 说明 |
| --- | --- | --- | --- |
| `target` | `QOfficialMessageTarget` | `必传` | 官方消息目标，含场景与 ID。 |
| `type` | `QOfficialMediaType` | `必传` | 媒体类型。 |
| `content` | `Stream` | `必传` | 待发送的文本、Markdown 或可读媒体流，具体类型见签名。 |
| `fileName` | `string` | `必传` | 文件名。 |
| `cancellationToken` | `CancellationToken` | `default` | 取消令牌；默认不主动取消。 |

### 返回

`Task<QOfficialMedia>`。返回可复用的上传媒体引用。

## SendAsync

发送已上传媒体；可传入消息或事件用于被动回复。

```csharp
Task<string> SendAsync(
    QOfficialMessageTarget target,
    QOfficialMedia media,
    QOfficialMessageReply? reply = null,
    CancellationToken cancellationToken = default
);
```

### 参数

| 参数 | C# 类型 | 默认值 | 说明 |
| --- | --- | --- | --- |
| `target` | `QOfficialMessageTarget` | `必传` | 官方消息目标，含场景与 ID。 |
| `media` | `QOfficialMedia` | `必传` | 已上传的媒体引用。 |
| `reply` | `QOfficialMessageReply?` | `null` | 被动回复的消息或事件凭据；null 表示未指定。 |
| `cancellationToken` | `CancellationToken` | `default` | 取消令牌；默认不主动取消。 |

### 返回

`Task<string>`。返回发送后的消息 ID。

## SendWithCaptionAsync

发送官方媒体并附带文本说明；图片说明可通过同一条富媒体消息发送。

```csharp
Task<string> SendWithCaptionAsync(
    QOfficialMessageTarget target,
    QOfficialMedia media,
    string content,
    QOfficialMessageReply? reply = null,
    CancellationToken cancellationToken = default
);
```

### 参数

| 参数 | C# 类型 | 默认值 | 说明 |
| --- | --- | --- | --- |
| `target` | `QOfficialMessageTarget` | `必传` | 官方消息目标，含场景与 ID。 |
| `media` | `QOfficialMedia` | `必传` | 已上传的媒体引用。 |
| `content` | `string` | `必传` | 待发送的文本、Markdown 或可读媒体流，具体类型见签名。 |
| `reply` | `QOfficialMessageReply?` | `null` | 被动回复的消息或事件凭据；null 表示未指定。 |
| `cancellationToken` | `CancellationToken` | `default` | 取消令牌；默认不主动取消。 |

### 返回

`Task<string>`。返回发送后的消息 ID。

## UploadAndSendAsync

上传并发送媒体。没有 reply 时使用平台主动发送接口。

```csharp
Task<string> UploadAndSendAsync(
    QOfficialMessageTarget target,
    QOfficialMediaType type,
    Stream content,
    string fileName,
    QOfficialMessageReply? reply = null,
    CancellationToken cancellationToken = default
);
```

### 参数

| 参数 | C# 类型 | 默认值 | 说明 |
| --- | --- | --- | --- |
| `target` | `QOfficialMessageTarget` | `必传` | 官方消息目标，含场景与 ID。 |
| `type` | `QOfficialMediaType` | `必传` | 媒体类型。 |
| `content` | `Stream` | `必传` | 待发送的文本、Markdown 或可读媒体流，具体类型见签名。 |
| `fileName` | `string` | `必传` | 文件名。 |
| `reply` | `QOfficialMessageReply?` | `null` | 被动回复的消息或事件凭据；null 表示未指定。 |
| `cancellationToken` | `CancellationToken` | `default` | 取消令牌；默认不主动取消。 |

### 返回

`Task<string>`。返回发送后的消息 ID。

## UploadAndSendWithCaptionAsync

上传媒体并在同一条富媒体消息中附带文本说明。

```csharp
Task<string> UploadAndSendWithCaptionAsync(
    QOfficialMessageTarget target,
    QOfficialMediaType type,
    Stream content,
    string fileName,
    string caption,
    QOfficialMessageReply? reply = null,
    CancellationToken cancellationToken = default
);
```

### 参数

| 参数 | C# 类型 | 默认值 | 说明 |
| --- | --- | --- | --- |
| `target` | `QOfficialMessageTarget` | `必传` | 官方消息目标，含场景与 ID。 |
| `type` | `QOfficialMediaType` | `必传` | 媒体类型。 |
| `content` | `Stream` | `必传` | 待发送的文本、Markdown 或可读媒体流，具体类型见签名。 |
| `fileName` | `string` | `必传` | 文件名。 |
| `caption` | `string` | `必传` | 随媒体一起发送的文字说明。 |
| `reply` | `QOfficialMessageReply?` | `null` | 被动回复的消息或事件凭据；null 表示未指定。 |
| `cancellationToken` | `CancellationToken` | `default` | 取消令牌；默认不主动取消。 |

### 返回

`Task<string>`。返回发送后的消息 ID。

## 相关类型

见[模型、消息段与事件类型](./types)。完整声明中的 `required` 字段创建对象时必须填写；`?` 表示可空。
