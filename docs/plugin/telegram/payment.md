# 支付（ITelegramPaymentApi）

命名空间：`ShiroBot.Model.Telegram`。Telegram Model ABI：`1.1.0.0`。仅机器人账号（BotFather 创建的机器人）实现；用户账号适配器上为 null。

发票与 Telegram Stars 收款，仅机器人。发送发票使用 `ITelegramMessageApi.SendAsync` 和 `TelegramInvoiceContent`。

```csharp
var api = Context.GetAdapterExtension<ITelegramPaymentApi>();
if (api is null) return;
```

取得接口不代表每个方法都已实现；未实现的方法抛出 `NotSupportedException`，调用还受平台权限和参数约束。

## AnswerPreCheckoutQueryAsync

确认或拒绝付款；`errorMessage` 在拒绝时显示给用户。

```csharp
Task AnswerPreCheckoutQueryAsync(
    string queryId,
    bool ok,
    string? errorMessage = null,
    CancellationToken cancellationToken = default
);
```

### 参数

| 参数 | C# 类型 | 默认值 | 说明 |
| --- | --- | --- | --- |
| `queryId` | `string` | `必传` | 查询 ID，来自对应事件负载。 |
| `ok` | `bool` | `必传` | true 表示确认，false 表示拒绝。 |
| `errorMessage` | `string?` | `null` | 拒绝时显示给用户的原因。 |
| `cancellationToken` | `CancellationToken` | `default` | 取消令牌；默认不主动取消。 |

### 返回

`Task`。完成后无返回值。

## AnswerShippingQueryAsync

返回可选的收货方式，或拒绝该地址。

```csharp
Task AnswerShippingQueryAsync(
    string queryId,
    bool ok,
    IReadOnlyList<TelegramShippingOption>? options = null,
    string? errorMessage = null,
    CancellationToken cancellationToken = default
);
```

### 参数

| 参数 | C# 类型 | 默认值 | 说明 |
| --- | --- | --- | --- |
| `queryId` | `string` | `必传` | 查询 ID，来自对应事件负载。 |
| `ok` | `bool` | `必传` | true 表示确认，false 表示拒绝。 |
| `options` | `IReadOnlyList<TelegramShippingOption>?` | `null` | 可选设置；null 使用默认值。 |
| `errorMessage` | `string?` | `null` | 拒绝时显示给用户的原因。 |
| `cancellationToken` | `CancellationToken` | `default` | 取消令牌；默认不主动取消。 |

### 返回

`Task`。完成后无返回值。

## RefundStarPaymentAsync

退还 Telegram Stars 付款。

```csharp
Task RefundStarPaymentAsync(
    string userId,
    string telegramPaymentChargeId,
    CancellationToken cancellationToken = default
);
```

### 参数

| 参数 | C# 类型 | 默认值 | 说明 |
| --- | --- | --- | --- |
| `userId` | `string` | `必传` | 用户 ID，与 SDK 的 `User.Id` 相同。 |
| `telegramPaymentChargeId` | `string` | `必传` | 成功付款服务消息中的 `TelegramPaymentChargeId`。 |
| `cancellationToken` | `CancellationToken` | `default` | 取消令牌；默认不主动取消。 |

### 返回

`Task`。完成后无返回值。

## GetStarBalanceAsync

机器人当前的 Telegram Stars 余额。

```csharp
Task<long> GetStarBalanceAsync(CancellationToken cancellationToken = default);
```

### 参数

| 参数 | C# 类型 | 默认值 | 说明 |
| --- | --- | --- | --- |
| `cancellationToken` | `CancellationToken` | `default` | 取消令牌；默认不主动取消。 |

### 返回

`Task<long>`。异步返回 `long`。

## 相关类型

见 [C# 类型参考](./types)。`required` 属性创建对象时必须填写；`?` 表示可空。
