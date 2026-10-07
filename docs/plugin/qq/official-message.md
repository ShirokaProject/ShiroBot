# 官方消息（IQOfficialMessageApi）

命名空间：`ShiroBot.Model.QQ`。QQ Model ABI：`1.0.0.0`。

```csharp
var api = Context.GetAdapterExtension<IQOfficialMessageApi>();
if (api is null) return;
```

接口可获取不表示每个方法都已实现；未实现的操作会抛出 `NotSupportedException`。平台权限与参数错误仍可能导致调用失败。

## SendAsync

发送一条类型化 QQ 官方消息；媒体类型需要适配器同时实现 IQOfficialMediaApi。

```csharp
Task<string> SendAsync(
    QOfficialMessageTarget target,
    QOfficialMessage message,
    QOfficialMessageReply? reply = null,
    CancellationToken cancellationToken = default
);
```

### 参数

| 参数 | C# 类型 | 默认值 | 说明 |
| --- | --- | --- | --- |
| `target` | `QOfficialMessageTarget` | `必传` | 官方消息目标，含场景与 ID。 |
| `message` | `QOfficialMessage` | `必传` | 类型化的官方出站消息。 |
| `reply` | `QOfficialMessageReply?` | `null` | 被动回复的消息或事件凭据；null 表示未指定。 |
| `cancellationToken` | `CancellationToken` | `default` | 取消令牌；默认不主动取消。 |

### 返回

`Task<string>`。返回发送后的消息 ID。

## SendTextAsync

发送官方文本消息，可通过 reply 指定消息或事件被动回复。

```csharp
Task<string> SendTextAsync(
    QOfficialMessageTarget target,
    string content,
    QOfficialMessageReply? reply = null,
    CancellationToken cancellationToken = default
);
```

### 参数

| 参数 | C# 类型 | 默认值 | 说明 |
| --- | --- | --- | --- |
| `target` | `QOfficialMessageTarget` | `必传` | 官方消息目标，含场景与 ID。 |
| `content` | `string` | `必传` | 待发送的文本、Markdown 或可读媒体流，具体类型见签名。 |
| `reply` | `QOfficialMessageReply?` | `null` | 被动回复的消息或事件凭据；null 表示未指定。 |
| `cancellationToken` | `CancellationToken` | `default` | 取消令牌；默认不主动取消。 |

### 返回

`Task<string>`。返回发送后的消息 ID。

## SendArkAsync

发送 QQ 官方 Ark 模板消息；不支持时抛出 NotSupportedException。

```csharp
Task<string> SendArkAsync(
    QOfficialMessageTarget target,
    int templateId,
    IReadOnlyDictionary<string, string> fields,
    QOfficialMessageReply? reply = null,
    CancellationToken cancellationToken = default
);
```

### 参数

| 参数 | C# 类型 | 默认值 | 说明 |
| --- | --- | --- | --- |
| `target` | `QOfficialMessageTarget` | `必传` | 官方消息目标，含场景与 ID。 |
| `templateId` | `int` | `必传` | 平台批准的 Ark 模板编号。 |
| `fields` | `IReadOnlyDictionary<string, string>` | `必传` | 模板字段及内容。 |
| `reply` | `QOfficialMessageReply?` | `null` | 被动回复的消息或事件凭据；null 表示未指定。 |
| `cancellationToken` | `CancellationToken` | `default` | 取消令牌；默认不主动取消。 |

### 返回

`Task<string>`。返回发送后的消息 ID。

## SendEmbedAsync

发送 QQ 官方 Embed 卡片消息。

```csharp
Task<string> SendEmbedAsync(
    QOfficialMessageTarget target,
    QOfficialEmbed embed,
    QOfficialMessageReply? reply = null,
    CancellationToken cancellationToken = default
);
```

### 参数

| 参数 | C# 类型 | 默认值 | 说明 |
| --- | --- | --- | --- |
| `target` | `QOfficialMessageTarget` | `必传` | 官方消息目标，含场景与 ID。 |
| `embed` | `QOfficialEmbed` | `必传` | Embed 卡片内容。 |
| `reply` | `QOfficialMessageReply?` | `null` | 被动回复的消息或事件凭据；null 表示未指定。 |
| `cancellationToken` | `CancellationToken` | `default` | 取消令牌；默认不主动取消。 |

### 返回

`Task<string>`。返回发送后的消息 ID。

## CanSendMarkdown

 探测此目标和按钮形式是否被适配器支持。平台权限可能变化， 返回 true 不保证后续发送一定成功。 

```csharp
bool CanSendMarkdown(
    QOfficialMessageTarget target,
    QOfficialMarkdown markdown,
    QOfficialKeyboard? keyboard = null
);
```

### 参数

| 参数 | C# 类型 | 默认值 | 说明 |
| --- | --- | --- | --- |
| `target` | `QOfficialMessageTarget` | `必传` | 官方消息目标，含场景与 ID。 |
| `markdown` | `QOfficialMarkdown` | `必传` | 自定义 Markdown 或模板 Markdown。 |
| `keyboard` | `QOfficialKeyboard?` | `null` | 可选的消息底部按钮。 |

### 返回

`bool`。返回适配器是否支持此目标与按钮形式；true 不保证平台授权。

## SendMarkdownAsync

 发送 Markdown，可附带底部按钮。返回官方消息 ID。 自定义按钮是否可用取决于机器人开放平台权限和发送场景。 

```csharp
Task<string> SendMarkdownAsync(
    QOfficialMessageTarget target,
    QOfficialMarkdown markdown,
    QOfficialKeyboard? keyboard = null,
    QOfficialMessageReply? reply = null,
    CancellationToken cancellationToken = default
);
```

### 参数

| 参数 | C# 类型 | 默认值 | 说明 |
| --- | --- | --- | --- |
| `target` | `QOfficialMessageTarget` | `必传` | 官方消息目标，含场景与 ID。 |
| `markdown` | `QOfficialMarkdown` | `必传` | 自定义 Markdown 或模板 Markdown。 |
| `keyboard` | `QOfficialKeyboard?` | `null` | 可选的消息底部按钮。 |
| `reply` | `QOfficialMessageReply?` | `null` | 被动回复的消息或事件凭据；null 表示未指定。 |
| `cancellationToken` | `CancellationToken` | `default` | 取消令牌；默认不主动取消。 |

### 返回

`Task<string>`。返回发送后的消息 ID。

## AcknowledgeInteractionAsync

 回应消息按钮互动。每个 InteractionId 仅能回应一次且会过期； 适配器应及时调用，避免等待耗时的插件处理使客户端一直显示加载。 

```csharp
Task AcknowledgeInteractionAsync(
    string interactionId,
    QOfficialInteractionResponseCode code = QOfficialInteractionResponseCode.Success,
    CancellationToken cancellationToken = default
);
```

### 参数

| 参数 | C# 类型 | 默认值 | 说明 |
| --- | --- | --- | --- |
| `interactionId` | `string` | `必传` | 按钮互动事件的 InteractionId。 |
| `code` | `QOfficialInteractionResponseCode` | `QOfficialInteractionResponseCode.Success` | 回应按钮互动的结果代码。 |
| `cancellationToken` | `CancellationToken` | `default` | 取消令牌；默认不主动取消。 |

### 返回

`Task`。异步完成后不返回业务值。

## 相关类型

见[模型、消息段与事件类型](./types)。完整声明中的 `required` 字段创建对象时必须填写；`?` 表示可空。
