# Bot API 原始调用（ITelegramBotApi）

命名空间：`ShiroBot.Model.Telegram`。Telegram Model ABI：`1.1.0.0`。仅机器人账号（BotFather 创建的机器人）实现；用户账号适配器上为 null。

直接调用任意 Bot API 方法，覆盖本模型没有建模的功能；仅 Bot API 传输的机器人适配器实现。 只使用 System.Text.Json 类型，插件无需引用适配器程序集。适配器负责长轮询：调用 getUpdates 或 setWebhook 会与之冲突。

```csharp
var api = Context.GetAdapterExtension<ITelegramBotApi>();
if (api is null) return;
```

取得接口不代表每个方法都已实现；未实现的方法抛出 `NotSupportedException`，调用还受平台权限和参数约束。

## BotApiVersion

Bot API 版本，如 10.4。

```csharp
string BotApiVersion { get; }
```

接口默认值：`string.Empty`；适配器按实际情况返回。

## CallAsync

调用方法并返回原始 result；失败抛出 `InvalidOperationException`，消息包含 Telegram 错误码和说明。

```csharp
Task<JsonElement> CallAsync(
    string method,
    JsonObject? parameters = null,
    IReadOnlyList<TelegramUploadFile>? files = null,
    CancellationToken cancellationToken = default
);
```

### 参数

| 参数 | C# 类型 | 默认值 | 说明 |
| --- | --- | --- | --- |
| `method` | `string` | `必传` | 方法名，如 `sendMessage`。 |
| `parameters` | `JsonObject?` | `null` | snake_case 参数。 |
| `files` | `IReadOnlyList<TelegramUploadFile>?` | `null` | 随请求上传的文件；提供时使用 multipart。 |
| `cancellationToken` | `CancellationToken` | `default` | 取消令牌；默认不主动取消。 |

### 返回

`Task<JsonElement>`。异步返回 `JsonElement`。

## UpdateReceived

每个 Bot API Update 的原始 JSON，先于 SDK 事件触发。

```csharp
event Func<JsonElement, Task>? UpdateReceived;
```

## 相关类型

见 [C# 类型参考](./types)。`required` 属性创建对象时必须填写；`?` 表示可空。
