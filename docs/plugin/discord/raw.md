# 原始调用（IDiscordRawApi）

命名空间：`ShiroBot.Model.Discord`。两种账号都实现。

直接调用任意 Discord REST 路由、发送 Gateway 指令并接收原始 Dispatch，覆盖本模型没有建模的功能。 只使用 System.Text.Json 类型，插件无需引用适配器程序集。

```csharp
var api = Context.GetAdapterExtension<IDiscordRawApi>();
if (api is null) return;
```

取得接口不代表每个方法都已实现；未实现的方法抛出 `NotSupportedException`，调用还受平台权限和参数约束。

## ApiVersion

Discord API 版本，如 10。

```csharp
int ApiVersion { get; }
```

接口默认值：`0`；适配器按实际情况返回。

## CallAsync

调用 REST 路由，例如 `CallAsync("GET", "guilds/123/audit-logs")`。返回响应 JSON；204 时为 Undefined。 有 `files` 时以 multipart 发送，`body` 作为 payload_json。 失败抛出 `HttpRequestException`，消息包含状态码和 Discord 错误内容。

```csharp
Task<JsonElement> CallAsync(
    string method,
    string path,
    JsonNode? body = null,
    IReadOnlyList<DiscordUploadFile>? files = null,
    string? reason = null,
    CancellationToken cancellationToken = default
);
```

### 参数

| 参数 | C# 类型 | 默认值 | 说明 |
| --- | --- | --- | --- |
| `method` | `string` | `必传` | 方法名或 HTTP 方法。 |
| `path` | `string` | `必传` | REST 路由，如 `guilds/123/audit-logs`。 |
| `body` | `JsonNode?` | `null` | 请求 JSON；有文件时作为 payload_json。 |
| `files` | `IReadOnlyList<DiscordUploadFile>?` | `null` | 随请求上传的文件；提供时使用 multipart。 |
| `reason` | `string?` | `null` | 写入服务器审计日志的原因；null 不写。 |
| `cancellationToken` | `CancellationToken` | `default` | 取消令牌；默认不主动取消。 |

### 返回

`Task<JsonElement>`。异步返回 `JsonElement`。

## SendGatewayAsync

发送 Gateway 指令，例如 op 8 请求成员、op 4 更新语音状态。Identify、Resume 和心跳由适配器管理。

```csharp
Task SendGatewayAsync(int op, JsonNode? data, CancellationToken cancellationToken = default);
```

### 参数

| 参数 | C# 类型 | 默认值 | 说明 |
| --- | --- | --- | --- |
| `op` | `int` | `必传` | Gateway opcode。 |
| `data` | `JsonNode?` | `必传` | Gateway 指令的 d 字段。 |
| `cancellationToken` | `CancellationToken` | `default` | 取消令牌；默认不主动取消。 |

### 返回

`Task`。完成后无返回值。

## DispatchReceived

每个 Gateway Dispatch：事件名（如 MESSAGE_CREATE）与原始 data，先于 SDK 事件触发。

```csharp
event Func<string, JsonElement, Task>? DispatchReceived;
```

## 相关类型

见 [C# 类型参考](./types)。`required` 属性创建对象时必须填写；`?` 表示可空。
