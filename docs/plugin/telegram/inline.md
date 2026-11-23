# inline 模式（ITelegramInlineApi）

命名空间：`ShiroBot.Model.Telegram`。仅机器人账号（BotFather 创建的机器人）实现；用户账号适配器上为 null。

inline 模式（在任意会话输入 @机器人 查询），仅机器人。需要在 BotFather 开启。

```csharp
var api = Context.GetAdapterExtension<ITelegramInlineApi>();
if (api is null) return;
```

取得接口不代表每个方法都已实现；未实现的方法抛出 `NotSupportedException`，调用还受平台权限和参数约束。

## AnswerInlineQueryAsync

回答 `TelegramEventKinds.InlineQuery`，最多 50 项结果；需在 10 秒内调用。

```csharp
Task AnswerInlineQueryAsync(
    string queryId,
    IReadOnlyList<TelegramInlineResult> results,
    TelegramInlineAnswerOptions? options = null,
    CancellationToken cancellationToken = default
);
```

### 参数

| 参数 | C# 类型 | 默认值 | 说明 |
| --- | --- | --- | --- |
| `queryId` | `string` | `必传` | 查询 ID，来自对应事件负载。 |
| `results` | `IReadOnlyList<TelegramInlineResult>` | `必传` | 结果列表。 |
| `options` | `TelegramInlineAnswerOptions?` | `null` | 可选设置；null 使用默认值。 |
| `cancellationToken` | `CancellationToken` | `default` | 取消令牌；默认不主动取消。 |

### 返回

`Task`。完成后无返回值。

## 相关类型

见 [C# 类型参考](./types)。`required` 属性创建对象时必须填写；`?` 表示可空。
