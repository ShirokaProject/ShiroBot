# 插件之间互相调用

需要宿主 1.0.3（SDK ABI 1.4.0.0）。调用方和提供方都要引用 `ShiroBot.SDK` 1.0.3 或更新版本。

插件之间可以按**名称**提供和调用服务，参数和返回值都是 JSON。双方不引用对方的任何类型，也不需要共享程序集，所以任何一方都可以单独卸载、更新和重新加载，另一方不用跟着动。

宿主只负责转发调用和事件，不理解服务的含义。服务名和 JSON 字段由插件之间约定，例如 AI 聊天插件约定的 [`chat.tool.*` 聊天工具](#约定-聊天工具-chat-tool)。

## 提供和调用服务

```csharp
using System.Text.Json.Nodes;

// 提供方
Context.Services.Provide("weather.query", async (args, ct) =>
{
    var city = args?["city"]?.GetValue<string>() ?? "上海";
    return new JsonObject { ["text"] = await QueryAsync(city, ct) };
});

// 调用方
try
{
    var result = await Context.Services.CallAsync("weather.query", new JsonObject { ["city"] = "北京" }, ct);
    var text = result?["text"]?.GetValue<string>();
}
catch (PluginServiceUnavailableException) { /* 没有插件提供这个服务，或提供方正在卸载 */ }
catch (PluginServiceException ex) { /* 提供方处理时抛出异常：ex.RemoteType、ex.RemoteMessage */ }
```

| 接口 | 说明 |
| --- | --- |
| `Provide(name, handler)` | 提供服务。返回的 `IDisposable` 可以提前注销；不注销也会随插件卸载自动注销 |
| `Provide(name, handler, metadata)` | 同上，附带一段描述，供其他插件用 `Find` 发现 |
| `CallAsync(name, arguments, ct)` | 调用服务，返回提供方的 JSON 结果 |
| `IsAvailable(name)` | 当前是否有插件提供这个服务 |
| `Find(prefix)` | 列出名称以 `prefix` 开头、当前可调用的服务 |

### 服务名

- 最长 200 个字符，不能有空白，区分大小写。
- 同一个名字同时只能由一个插件提供，重复提供会抛出 `InvalidOperationException`。
- 建议用“领域.动作”的形式，并以插件或领域名开头，例如 `meme.send`、`weather.query`，避免和其他插件撞名。

### 参数和返回值

- 参数、返回值都按 JSON 文本复制一份再交给对方，双方不会持有对方创建的对象，也就不会因此拉住对方的程序集。
- 只放字符串、数字、布尔、数组和 `JsonObject`，不要用 `JsonValue.Create(自定义对象)`。
- 大文件、图片不要直接放进 JSON，传文件路径、ID 或 URL。
- 每次调用都会复制，适合普通大小的请求，不适合在热路径上高频传大对象。

### 失败和取消

| 情况 | 调用方收到 |
| --- | --- |
| 没有插件提供这个服务 | `PluginServiceUnavailableException` |
| 提供方正在卸载 | `PluginServiceUnavailableException` |
| 提供方的处理函数抛出异常 | `PluginServiceException`，只带异常类型名 `RemoteType` 和消息 `RemoteMessage`，不带原始异常对象 |
| 调用方取消 `ct` | `OperationCanceledException` |

提供方收到的 `ct` 同时连着调用方的取消和提供方自己的卸载：任何一方取消，处理函数都应尽快结束。宿主不设调用超时，需要时由调用方自己传带超时的 `ct`。

::: tip
调用方不能假设提供方一定在。插件的加载顺序不固定，提供方也可能随时被卸载或更新，所以总要处理 `PluginServiceUnavailableException`，把它当作“这个功能暂时不可用”。
:::

### 描述和发现

提供方可以在注册时附带一段描述，调用方用 `Find` 按前缀列出当前可调用的服务，适合做“任何插件都可以来扩展”的扩展点：

```csharp
// 提供方
Context.Services.Provide("chat.tool.weather", HandleAsync, new JsonObject
{
    ["description"] = "查询城市天气"
});

// 调用方
foreach (var service in Context.Services.Find("chat.tool."))
    BotLog.Info($"{service.Name} 由 {service.ProviderId} 提供：{service.Metadata?["description"]}");
```

- `Find` 按名称排序，返回服务名 `Name`、提供方插件 ID `ProviderId` 和描述的副本 `Metadata`。
- 描述在注册时复制保存。要更新描述，先 `Dispose` 原来的注册，再重新 `Provide`。
- 正在卸载的提供方不会再被列出。
- `Find` 只是在宿主内存里查询，开销很小，可以在需要时现查，不必自己缓存；现查还能马上反映提供方的卸载、更新和配置变化。

## 事件总线

事件用于通知，不需要返回结果：

```csharp
// 订阅
Context.Bus.On("weather.updated", async (payload, ct) =>
{
    var city = payload?["city"]?.GetValue<string>();
    await RefreshAsync(city, ct);
});

// 发布：返回成功处理这条事件的订阅者数量
var delivered = await Context.Bus.EmitAsync("weather.updated", new JsonObject { ["city"] = "上海" }, ct);
```

- 每个订阅者拿到独立的一份 JSON 副本，并发处理；`EmitAsync` 等所有订阅者处理完才返回。
- 订阅者处理失败只记在订阅者自己的日志里，不会抛给发布方，也不影响其他订阅者。
- 主题名的规则和服务名相同。同一个主题可以有任意多个订阅者，也可以没有订阅者（返回 0）。
- 订阅随插件卸载自动注销；`On` 返回的 `IDisposable` 可以提前退订。

## 生命周期与热更新

- **注册时机**：在 `LoadAsync` 里 `Provide` 和 `On`。服务和订阅登记在插件的生命周期上，不需要在 `OnUnloadAsync` 里手动注销。
- **加载顺序**：提供方和调用方谁先加载都可以。调用方在需要时才调用，不要在加载阶段就要求对方存在。
- **卸载**：插件开始卸载时，宿主立即停止它接收新的调用和事件（新调用会收到 `PluginServiceUnavailableException`），等它正在处理的调用和事件结束，再执行它的 `OnUnloadAsync` 并释放程序集。完整顺序见[生命周期作用域](./context-config#生命周期作用域)。
- **更新**：提供方替换 DLL 后热重载，重新加载时再次 `Provide`。调用方不需要任何操作，下一次调用就会用到新版本。
- **不记依赖**：`CallAsync`、`Find` 和事件都不会让宿主认为调用方依赖提供方，所以提供方随时可以卸载。

### 要不要声明依赖

按名称调用的双方**不需要**在 `BotPlugin` 里声明依赖。依赖声明表示“没有对方就不能工作”，会让对方无法单独卸载。可选的功能（对方在就用，不在就跳过）不应该声明依赖。

## 设计自己的约定

服务名和字段是插件之间的公开约定，发布后要像公开 API 一样维护：

- **写进文档**：在提供方的 README 里写清楚服务名、参数字段、返回字段和错误情况。
- **读取时宽容**：缺少的字段用默认值，不认识的字段直接忽略，这样双方可以各自加字段而不必同时发布。
- **不兼容的修改换名字**：例如从 `weather.query` 改成 `weather.query.v2`，旧名字保留一段时间。也可以在描述里放版本号，让调用方自己判断。
- **扩展点用前缀**：希望多个插件都能接入时，约定一个前缀（例如 `chat.tool.`），调用方用 `Find` 发现，而不是写死某一个插件的服务名。

## 约定：聊天工具 chat.tool.*

AI 聊天插件（AIChat）约定的扩展点：任何插件按名称提供 `chat.tool.<工具名>` 服务，AI 聊天插件就会把它作为工具交给模型。聊天插件里不需要任何针对具体工具插件的代码，例如 MemePlugin 的发表情就是这样接入的。

### 整体流程

以 MemePlugin 的发表情为例：

```text
群聊：@机器人 发个猫猫表情
│
├─ 1. 聊天插件准备调用模型时，调用 Find("chat.tool.")
│     → 找到 MemePlugin 提供的 chat.tool.send_meme，以及它的说明、参数格式和提示词
│     → 作为工具 send_meme 加入本轮请求，提示词追加到系统提示
│
├─ 2. 模型调用 send_meme {"query": "猫"}
│     → 聊天插件 CallAsync("chat.tool.send_meme", { arguments, context })
│     → MemePlugin 只选一张图，返回
│        { content: "已选好…", after_reply: { service: "meme.send", arguments: { id } } }
│     → content 交还给模型，模型写出最终回复，例如“喵～”
│
├─ 3. 聊天插件照常发出文字回复（分条、折叠、@、引用都按聊天插件自己的设置）
│
└─ 4. 聊天插件 CallAsync("meme.send", { arguments, context, reply })
      → MemePlugin 自己把图发到同一个会话（QQ 原生表情、冷却、使用次数都由它处理）
```

MemePlugin 不在时，第 1 步找不到这个工具，模型就只用文字回复；第 2、4 步之间 MemePlugin 被卸载，调用会收到 `PluginServiceUnavailableException`，聊天插件告诉模型改用文字，或者只记一条日志。

### 工具描述

提供时的 `metadata`：

| 字段 | 必填 | 说明 |
| --- | --- | --- |
| `description` | 是 | 交给模型的工具说明。聊天插件会在前面加上 `[提供方插件 ID]` |
| `parameters` | 否 | 模型参数的 JSON Schema（`type` 为 `object`），缺省为无参数 |
| `prompt` | 否 | 追加到系统提示的使用说明，例如什么时候该用、什么时候不该用 |
| `max_calls_per_turn` | 否 | 每轮回复最多调用几次，1–5，默认 1 |

工具名取服务名的后缀，只能用字母、数字、下划线和连字符，最长 64 个字符；和聊天插件的内置工具、MCP 工具或其他插件工具重名时会被跳过。

### 工具调用

聊天插件调用服务时传：

```json
{
  "arguments": { "query": "猫" },
  "context": {
    "platform": "qq",
    "instance_id": "qq-main",
    "channel_type": "Group",
    "channel_id": "123456",
    "message_id": "998877",
    "sender_id": "10001",
    "is_group": true,
    "proactive": false
  }
}
```

`arguments` 是模型给的参数；`context` 是触发这次回复的会话，提供方可以据此选择内容，或在之后发消息到同一个会话。`proactive` 为 true 表示这是机器人主动插话。

返回：

| 字段 | 说明 |
| --- | --- |
| `content` | 交给模型的结果，字符串；不是字符串时按 JSON 文本交给模型 |
| `after_reply` | 可选，`{ "service": 服务名, "arguments": … }`，要求聊天插件在文字回复发出后调用这个服务 |

没有合适结果时，在 `content` 里告诉模型改用文字回复即可，不要抛异常。

### 回复后的动作

需要发图片、文件等“跟在回复后面”的内容时，不要在工具调用里直接发：那时模型还没写出回复，消息顺序会乱，也可能发出后模型又决定不回复。改为返回 `after_reply`，由聊天插件在文字发出后调用。调用参数：

```json
{
  "arguments": { "id": "68c567ca299d8df1" },
  "context": { "...": "同工具调用" },
  "reply": { "text_sent": true, "mention": false, "quote": false }
}
```

| 字段 | 说明 |
| --- | --- |
| `arguments` | `after_reply.arguments` 的副本 |
| `reply.text_sent` | 聊天插件是否已经发出文字回复 |
| `reply.mention` / `reply.quote` | 没有发出文字时，这个动作是否应该 @ 发送者、引用触发消息（按聊天插件的设置） |

模型只调用了工具、没有写文字时，`text_sent` 为 false，聊天插件不发文字，由这个动作代替回复，并按 `mention` / `quote` @、引用。可以返回 `{ "history": "…" }`，这段文字会代替空回复写入对话历史，例如 `[表情：猫猫]`。

### 写一个工具插件

```csharp
using System.Text.Json.Nodes;
using ShiroBot.SDK.Plugin;

[BotPlugin("weather", Name = "天气")]
public sealed class WeatherPlugin : PluginBase
{
    protected override Task LoadAsync()
    {
        Context.Services.Provide("chat.tool.query_weather", async (input, ct) =>
        {
            var city = input?["arguments"]?["city"]?.GetValue<string>() ?? "上海";
            return new JsonObject { ["content"] = await QueryAsync(city, ct) };
        }, new JsonObject
        {
            ["description"] = "查询城市当前天气。用户问天气时使用。",
            ["parameters"] = new JsonObject
            {
                ["type"] = "object",
                ["required"] = new JsonArray("city"),
                ["properties"] = new JsonObject
                {
                    ["city"] = new JsonObject { ["type"] = "string", ["description"] = "城市名，例如 上海" }
                }
            }
        });
        return Task.CompletedTask;
    }
}
```

不需要引用 AI 聊天插件，也不需要声明依赖。装上就能在聊天里使用，卸载后聊天插件自动不再提供这个工具。

### 注意事项

- **什么时候查询**：聊天插件只在确定要调用模型的那一轮查询工具，普通群消息不会触发。
- **提示词缓存**：工具描述和 `prompt` 在注册时就固定，每轮相同，并且排在系统提示中稳定的部分，不会破坏上游的前缀缓存。不要把会变化的状态（冷却、剩余次数）放进描述，放进工具调用的结果。
- **使用策略由提供方决定**：例如“只在用户明确要求时才发表情”写在提供方的 `description` 和 `prompt` 里，由提供方的配置控制；改配置后重新 `Provide` 即可。
- **聊天插件这边的开关**：AIChat 的 `[tool_use.plugin_tools]` 可以关闭全部插件工具、允许主动插话时使用，或用 `disabled` 去掉某个工具。

## 示例

仓库 `samples/PluginInterop` 有一对可运行的示例：`Greeter` 提供 `greeter.greet` 服务并发布 `greeter.greeted` 事件，`GreeterClient` 调用服务、订阅事件，并演示提供方热更新时调用方不受影响。
