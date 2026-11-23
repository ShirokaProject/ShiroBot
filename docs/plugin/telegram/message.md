# 原生消息（ITelegramMessageApi）

命名空间：`ShiroBot.Model.Telegram`。Telegram Model ABI：`1.1.0.0`。两种账号都实现。

Telegram 原生消息：投票、位置、相册、发票、编辑、转发、输入状态和回调提示。

```csharp
var api = Context.GetAdapterExtension<ITelegramMessageApi>();
if (api is null) return;
```

取得接口不代表每个方法都已实现；未实现的方法抛出 `NotSupportedException`，调用还受平台权限和参数约束。

## SendAsync

发送 Telegram 原生内容；相册返回每条消息，其他内容返回一条。

```csharp
Task<IReadOnlyList<TelegramSentMessage>> SendAsync(
    Channel channel,
    TelegramOutgoingContent content,
    TelegramSendOptions? options = null,
    CancellationToken cancellationToken = default
);
```

### 参数

| 参数 | C# 类型 | 默认值 | 说明 |
| --- | --- | --- | --- |
| `channel` | `Channel` | `必传` | 目标 SDK 频道；私聊为 `Channel.Direct(用户 ID)`。 |
| `content` | `TelegramOutgoingContent` | `必传` | 要发送的内容。 |
| `options` | `TelegramSendOptions?` | `null` | 可选设置；null 使用默认值。 |
| `cancellationToken` | `CancellationToken` | `default` | 取消令牌；默认不主动取消。 |

### 返回

`Task<IReadOnlyList<TelegramSentMessage>>`。异步返回 `IReadOnlyList<TelegramSentMessage>`。

## EditTextAsync

修改文本消息；`replyMarkup` 为 null 时移除内联键盘。

```csharp
Task EditTextAsync(
    Channel channel,
    string messageId,
    string text,
    TelegramParseMode parseMode = TelegramParseMode.None,
    TelegramInlineKeyboard? replyMarkup = null,
    CancellationToken cancellationToken = default
);
```

### 参数

| 参数 | C# 类型 | 默认值 | 说明 |
| --- | --- | --- | --- |
| `channel` | `Channel` | `必传` | 目标 SDK 频道；私聊为 `Channel.Direct(用户 ID)`。 |
| `messageId` | `string` | `必传` | 消息 ID，与 SDK 的 `MessageId` 相同。 |
| `text` | `string` | `必传` | 文本内容。 |
| `parseMode` | `TelegramParseMode` | `TelegramParseMode.None` | 正文格式。 |
| `replyMarkup` | `TelegramInlineKeyboard?` | `null` | 内联键盘；null 移除。 |
| `cancellationToken` | `CancellationToken` | `default` | 取消令牌；默认不主动取消。 |

### 返回

`Task`。完成后无返回值。

## EditCaptionAsync

修改媒体消息的说明。

```csharp
Task EditCaptionAsync(
    Channel channel,
    string messageId,
    string? caption,
    TelegramParseMode parseMode = TelegramParseMode.None,
    TelegramInlineKeyboard? replyMarkup = null,
    CancellationToken cancellationToken = default
);
```

### 参数

| 参数 | C# 类型 | 默认值 | 说明 |
| --- | --- | --- | --- |
| `channel` | `Channel` | `必传` | 目标 SDK 频道；私聊为 `Channel.Direct(用户 ID)`。 |
| `messageId` | `string` | `必传` | 消息 ID，与 SDK 的 `MessageId` 相同。 |
| `caption` | `string?` | `必传` | 说明文字；null 清除。 |
| `parseMode` | `TelegramParseMode` | `TelegramParseMode.None` | 正文格式。 |
| `replyMarkup` | `TelegramInlineKeyboard?` | `null` | 内联键盘；null 移除。 |
| `cancellationToken` | `CancellationToken` | `default` | 取消令牌；默认不主动取消。 |

### 返回

`Task`。完成后无返回值。

## EditReplyMarkupAsync

替换或移除（null）内联键盘。

```csharp
Task EditReplyMarkupAsync(
    Channel channel,
    string messageId,
    TelegramInlineKeyboard? replyMarkup,
    CancellationToken cancellationToken = default
);
```

### 参数

| 参数 | C# 类型 | 默认值 | 说明 |
| --- | --- | --- | --- |
| `channel` | `Channel` | `必传` | 目标 SDK 频道；私聊为 `Channel.Direct(用户 ID)`。 |
| `messageId` | `string` | `必传` | 消息 ID，与 SDK 的 `MessageId` 相同。 |
| `replyMarkup` | `TelegramInlineKeyboard?` | `必传` | 内联键盘；null 移除。 |
| `cancellationToken` | `CancellationToken` | `default` | 取消令牌；默认不主动取消。 |

### 返回

`Task`。完成后无返回值。

## EditInlineTextAsync

修改 inline 模式发送的消息文本，ID 来自 `TelegramChosenInlineResult.InlineMessageId`。

```csharp
Task EditInlineTextAsync(
    string inlineMessageId,
    string text,
    TelegramParseMode parseMode = TelegramParseMode.None,
    TelegramInlineKeyboard? replyMarkup = null,
    CancellationToken cancellationToken = default
);
```

### 参数

| 参数 | C# 类型 | 默认值 | 说明 |
| --- | --- | --- | --- |
| `inlineMessageId` | `string` | `必传` | inline 消息 ID，来自 `TelegramChosenInlineResult.InlineMessageId`。 |
| `text` | `string` | `必传` | 文本内容。 |
| `parseMode` | `TelegramParseMode` | `TelegramParseMode.None` | 正文格式。 |
| `replyMarkup` | `TelegramInlineKeyboard?` | `null` | 内联键盘；null 移除。 |
| `cancellationToken` | `CancellationToken` | `default` | 取消令牌；默认不主动取消。 |

### 返回

`Task`。完成后无返回值。

## ForwardAsync

转发并显示来源。

```csharp
Task<TelegramSentMessage> ForwardAsync(
    Channel target,
    Channel source,
    string messageId,
    bool disableNotification = false,
    CancellationToken cancellationToken = default
);
```

### 参数

| 参数 | C# 类型 | 默认值 | 说明 |
| --- | --- | --- | --- |
| `target` | `Channel` | `必传` | 目标 SDK 频道。 |
| `source` | `Channel` | `必传` | 来源 SDK 频道。 |
| `messageId` | `string` | `必传` | 消息 ID，与 SDK 的 `MessageId` 相同。 |
| `disableNotification` | `bool` | `false` | 静默发送，不推送通知。 |
| `cancellationToken` | `CancellationToken` | `default` | 取消令牌；默认不主动取消。 |

### 返回

`Task<TelegramSentMessage>`。异步返回 `TelegramSentMessage`。

## CopyAsync

复制内容，不显示来源；投票、发票等无法复制。返回新消息 ID。

```csharp
Task<string> CopyAsync(
    Channel target,
    Channel source,
    string messageId,
    TelegramSendOptions? options = null,
    CancellationToken cancellationToken = default
);
```

### 参数

| 参数 | C# 类型 | 默认值 | 说明 |
| --- | --- | --- | --- |
| `target` | `Channel` | `必传` | 目标 SDK 频道。 |
| `source` | `Channel` | `必传` | 来源 SDK 频道。 |
| `messageId` | `string` | `必传` | 消息 ID，与 SDK 的 `MessageId` 相同。 |
| `options` | `TelegramSendOptions?` | `null` | 可选设置；null 使用默认值。 |
| `cancellationToken` | `CancellationToken` | `default` | 取消令牌；默认不主动取消。 |

### 返回

`Task<string>`。异步返回 `string`。

## SendChatActionAsync

显示“正在输入”等状态约 5 秒。

```csharp
Task SendChatActionAsync(
    Channel channel,
    TelegramChatAction action,
    CancellationToken cancellationToken = default
);
```

### 参数

| 参数 | C# 类型 | 默认值 | 说明 |
| --- | --- | --- | --- |
| `channel` | `Channel` | `必传` | 目标 SDK 频道；私聊为 `Channel.Direct(用户 ID)`。 |
| `action` | `TelegramChatAction` | `必传` | 要显示的动作。 |
| `cancellationToken` | `CancellationToken` | `default` | 取消令牌；默认不主动取消。 |

### 返回

`Task`。完成后无返回值。

## StopPollAsync

结束投票并返回最终结果。

```csharp
Task<TelegramPoll> StopPollAsync(
    Channel channel,
    string messageId,
    CancellationToken cancellationToken = default
);
```

### 参数

| 参数 | C# 类型 | 默认值 | 说明 |
| --- | --- | --- | --- |
| `channel` | `Channel` | `必传` | 目标 SDK 频道；私聊为 `Channel.Direct(用户 ID)`。 |
| `messageId` | `string` | `必传` | 消息 ID，与 SDK 的 `MessageId` 相同。 |
| `cancellationToken` | `CancellationToken` | `default` | 取消令牌；默认不主动取消。 |

### 返回

`Task<TelegramPoll>`。异步返回 `TelegramPoll`。

## AnswerCallbackAsync

带提示回答按钮回调（仅机器人），`interactionId` 为 `InteractionEvent.InteractionId`。 适配器自动确认回调时，此前的空确认已经发出，提示不会再显示；需要提示时关闭适配器的自动确认。

```csharp
Task AnswerCallbackAsync(
    string interactionId,
    TelegramCallbackAnswer answer,
    CancellationToken cancellationToken = default
);
```

### 参数

| 参数 | C# 类型 | 默认值 | 说明 |
| --- | --- | --- | --- |
| `interactionId` | `string` | `必传` | 交互 ID，即 SDK `InteractionEvent.InteractionId`。 |
| `answer` | `TelegramCallbackAnswer` | `必传` | 回答内容。 |
| `cancellationToken` | `CancellationToken` | `default` | 取消令牌；默认不主动取消。 |

### 返回

`Task`。完成后无返回值。

## AutoAnswersCallbacks

适配器是否会自动确认按钮回调。为 true 时 `AnswerCallbackAsync` 的提示不生效。

```csharp
bool AutoAnswersCallbacks { get; }
```

接口默认值：`true`；适配器按实际情况返回。

## 相关类型

见 [C# 类型参考](./types)。`required` 属性创建对象时必须填写；`?` 表示可空。
