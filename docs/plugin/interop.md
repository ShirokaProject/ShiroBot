# 插件之间互相调用

需要宿主 1.0.3（SDK ABI 1.4.0.0）。调用方和提供方都要引用 `ShiroBot.SDK` 1.0.3 或更新版本。

插件之间可以按**名称**提供和调用服务、发布和订阅事件。提供方在自己的插件里声明一份**契约**，调用方把提供方的插件 DLL 放进项目，SDK 在构建时生成契约代码，然后就能像调用普通方法一样调用对方。

运行时双方不共享任何程序集和对象，消息按 JSON 复制，所以任何一方都可以单独卸载、更新和重新加载，另一方不用跟着动。

## 快速上手

以一个天气服务为例：天气插件提供 `weather.query`，其他插件调用它。

### 1. 提供方声明契约

在天气插件里声明消息类型，再用一个 `[PluginContract]` 类列出对外的服务和事件：

```csharp
using System.ComponentModel;
using ShiroBot.SDK.Plugin;

namespace Weather;

public sealed record WeatherQuery([property: Description("城市名，例如 上海")] string City);
public sealed record WeatherReply(string Text, double Temperature);
public sealed record WeatherUpdated(string City);

[PluginContract]
public static partial class WeatherApi
{
    [PluginService("weather.query")]
    public static partial PluginService<WeatherQuery, WeatherReply> Query { get; }

    [PluginEvent("weather.updated")]
    public static partial PluginEvent<WeatherUpdated> Updated { get; }
}
```

属性的实现和 JSON 读写代码由 SDK 在编译时生成，不需要自己写任何序列化代码。

### 2. 提供方实现服务

```csharp
protected override Task LoadAsync()
{
    WeatherApi.Query.Provide(Context, async (query, ct) =>
    {
        var weather = await FetchAsync(query.City, ct);
        return new WeatherReply(weather.Summary, weather.Temperature);
    });
    return Task.CompletedTask;
}

// 天气变化时
await WeatherApi.Updated.EmitAsync(Context, new WeatherUpdated("上海"), ct);
```

服务随插件卸载自动注销，不需要在 `OnUnloadAsync` 里处理。

### 3. 调用方引用契约

把天气插件的 DLL 放进调用方项目的 `contracts` 目录：

```text
MyPlugin/
  contracts/
    WeatherPlugin.dll        ← 从天气插件的 Release 下载，或从它的构建输出复制
  MyPlugin.csproj
  Plugin.cs
```

也可以在 `.csproj` 里直接指向别处的 DLL：

```xml
<ItemGroup>
  <PluginContract Include="../Shirobot.Plugin.Weather/bin/Release/net10.0/WeatherPlugin.dll" />
</ItemGroup>
```

构建时 SDK 读取 DLL 里的 `[PluginContract]`，在 `obj/` 下生成一份契约代码（`WeatherApi`、`WeatherQuery`、`WeatherReply`、`WeatherUpdated`，命名空间不变），编进调用方自己的程序集。天气插件的 DLL **只在构建时读取**，不会被引用，也不会进入调用方的成品。

### 4. 调用方调用

```csharp
using Weather;

var reply = await WeatherApi.Query.TryCallAsync(Context, new WeatherQuery("北京"), ct);
if (reply is null)
{
    await ReplyAsync("天气插件没有加载。");
    return;
}
await ReplyAsync($"{reply.Text}，{reply.Temperature}°C");

// 订阅事件
WeatherApi.Updated.On(Context, (e, ct) => RefreshAsync(e.City, ct));
```

`TryCallAsync` 在没有插件提供这个服务时返回 `null`。插件的加载顺序不固定，提供方也可能随时被卸载或更新，所以调用方总要考虑对方不在的情况。

## 契约是怎么工作的

```text
构建时                                              运行时
──────────────────────────────────────────────     ────────────────────────────────────────
WeatherPlugin.dll ──读取 [PluginContract]──┐        WeatherPlugin            MyPlugin
                                          ↓         （自己的 WeatherApi）     （生成的 WeatherApi）
MyPlugin 编译 ← obj/PluginContracts/Weather.g.cs          │                       │
          ↓                                               └──── JSON（宿主转发）───┘
MyPlugin.dll（不含也不引用 WeatherPlugin.dll）
```

- **双方各有一份契约类型**：提供方用自己声明的，调用方用生成的副本。运行时它们是两个不相关的类型，宿主只转发 JSON，所以谁都可以单独卸载。
- **生成的代码是 `internal` 的**：不会成为调用方的公开 API，多个插件引用同一份契约也不会冲突。
- **序列化代码在各自的程序集里**：SDK 在编译时为契约类型生成 JSON 读写代码，不使用反射序列化。宿主的序列化器如果用反射处理插件里的类型，会缓存这些类型，插件卸载后程序集也无法释放，热更新会失效。
- **宿主内部只传 UTF-8 字节**：调用方把请求直接写成 JSON 字节，宿主复制后交给提供方，提供方直接读成自己的类型，中间不经过 `JsonNode`。
- **生成的文件可以直接看**：在 `obj/<配置>/<框架>/PluginContracts/` 下，跳转到 `WeatherApi` 的定义也会打开它。

::: warning 不要把契约当成共享程序集
不要把提供方的 DLL 加进 `SharedAssemblies`，也不要用 `<Reference>` 直接引用它。那会让调用方在运行时依赖提供方的程序集，提供方就无法单独卸载和更新。
:::

### 契约里能放什么

契约只描述消息格式，生成时只取 `[PluginContract]` 类用到的类型：

| 可以 | 说明 |
| --- | --- |
| `record` / 类的公开属性 | 生成同名的 record；只取属性，不取方法和逻辑 |
| 枚举 | 按名称序列化 |
| `string`、数值、`bool`、`DateTimeOffset`、`Guid` | |
| `List<T>`、数组、`Dictionary<string, T>` | |
| 可空类型、默认值 | 保留 |
| `[Description]`、`[JsonPropertyName]` | 保留，前者用于[生成参数格式](#描述和发现) |

不能放接口、委托、带逻辑的类型，也不能引用提供方插件里契约以外的类型；违反时提供方构建就会报错，指出是哪个类型。

字段名在线上统一用 snake_case，例如 `Temperature` 是 `temperature`。

### 契约变化时

调用方用的是构建时那一版契约。提供方升级后，调用方不重新构建也能继续工作，只要提供方遵守：

- **只加字段，不改字段**：旧调用方收不到新字段，新字段对它也无害；改名、改类型、删字段都是不兼容的修改。
- **不兼容的修改换服务名**：例如增加 `weather.query.v2`，一段时间内同时提供新旧两个服务。

调用方想用新字段时，换上新版的 DLL 重新构建即可。

## 描述和发现

提供方可以附带一段描述，其他插件按名称前缀找到当前可调用的服务。适合做“任何插件都可以来接入”的扩展点：调用方只认一个前缀，不写死某个插件的服务名。

例如天气聚合插件定义扩展点 `weather.source.*`，并在自己的契约里给出描述格式：

```csharp
// 天气聚合插件的契约
public sealed record SourceInfo(string Title, JsonObject Parameters);
public sealed record SourceQuery([property: Description("城市名")] string City, [property: Description("预报天数")] int Days = 1);

[PluginContract]
public static partial class WeatherSources
{
    public const string Prefix = "weather.source.";

    [PluginService("weather.source.*")]   // 一类服务：名称由提供方补全
    public static partial PluginServiceFamily<SourceQuery, WeatherReply, SourceInfo> Sources { get; }
}
```

```csharp
// 某个天气源插件：放入聚合插件的 DLL，提供其中一个
WeatherSources.Sources.Provide(Context, "hefeng", HandleAsync,
    new SourceInfo("和风天气", PluginJsonSchema.For<SourceQuery>()));

// 聚合插件：列出当前所有天气源并调用
foreach (var source in WeatherSources.Sources.Find(Context))
{
    BotLog.Info($"{source.Name}（{source.ProviderId}）：{source.Metadata?.Title}");
    var reply = await source.TryCallAsync(Context, new SourceQuery("北京"), ct);
}
```

- `Find` 按名称排序，返回服务名、提供方插件 ID 和描述。
- `PluginJsonSchema.For<T>()` 从 record 生成 JSON Schema：字段类型、必填字段、默认值和 `[Description]` 的说明，适合把参数格式告诉调用方，例如生成表单，或交给 AI 模型作为工具参数。
- 描述在提供时复制保存；要更新描述，先 `Dispose` 原来的注册，再重新提供。
- 正在卸载的提供方不会再被列出。
- 查找只是在宿主内存里筛选，开销很小，在需要时现查即可，不必自己缓存。

## 谁提供契约

**谁定义接口，谁提供契约**：

| 场景 | 契约由谁提供 | 另一方要做什么 |
| --- | --- | --- |
| 点对点调用（某插件调 `weather.query`） | 提供服务的插件 | 调用方放入提供方的 DLL |
| 扩展点（聚合插件发现所有 `weather.source.*`） | 定义扩展点的插件 | 每个接入的插件放入定义方的 DLL；定义方不需要知道有哪些插件接入 |

双方都不写对方插件的代码，也不在运行时引用对方。新增一个扩展点的实现时，定义方不用改任何代码。

## 接口参考

### 声明契约

| 特性 | 说明 |
| --- | --- |
| `[PluginContract]` | 标在 `static partial class` 上，它的属性就是对外的服务和事件 |
| `[PluginService("name")]` | 标在 `PluginService<TRequest, TResponse>` 属性上 |
| `[PluginService("prefix.*")]` | 标在 `PluginServiceFamily<TRequest, TResponse, TMetadata>` 属性上，表示一类服务 |
| `[PluginEvent("topic")]` | 标在 `PluginEvent<T>` 属性上 |
| `<PluginContract Include="…dll" />` | 调用方引用契约；`contracts/*.dll` 默认包含 |

### `PluginService<TRequest, TResponse>`

| 成员 | 说明 |
| --- | --- |
| `Provide(context, handler)` | 提供服务，返回的 `IDisposable` 可以提前注销 |
| `Provide(context, handler, metadata)` | 同上，附带描述，供其他插件发现 |
| `TryCallAsync(context, request, ct)` | 调用服务；没有提供方或提供方正在卸载时返回 `null` |
| `CallAsync(context, request, ct)` | 调用服务；没有提供方时抛出 `PluginServiceUnavailableException` |
| `IsAvailable(context)` | 当前是否有插件提供这个服务 |

不需要参数的服务用 `PluginService<TResponse>`。

### `PluginServiceFamily<TRequest, TResponse, TMetadata>`

| 成员 | 说明 |
| --- | --- |
| `Provide(context, name, handler, metadata)` | 提供这一类中的一个服务，完整名称为前缀加 `name` |
| `Find(context)` | 列出当前提供的所有服务，每一项可以直接 `TryCallAsync` / `CallAsync` |

### `PluginEvent<T>`

| 成员 | 说明 |
| --- | --- |
| `On(context, handler)` | 订阅，返回的 `IDisposable` 可以提前退订；随插件卸载自动退订 |
| `EmitAsync(context, payload, ct)` | 发布，等所有订阅者处理完，返回成功处理的数量 |

### `PluginServiceEntry`

`Find` 返回的每一项：`Name`（完整服务名）、`ShortName`（去掉前缀的名称）、`ProviderId`、`Metadata`，以及 `TryCallAsync` / `CallAsync`。

### JSON 工具

| 成员 | 说明 |
| --- | --- |
| `[PluginJson]` | 标在不出现在任何契约服务里的 record 上，为它生成 JSON 读写代码；用于下面几个方法的类型参数时会自动生成，不必标记 |
| `PluginJsonSchema.For<T>()` | 生成类型 `T` 的 JSON Schema |
| `PluginJson.As<T>(node)` | 把 `JsonNode` 读成 `T`，例如收到的工具参数 |
| `PluginJson.Serialize<T>` / `Deserialize<T>` / `ToNode<T>` | 在 `T` 和 UTF-8 JSON、`JsonNode` 之间转换 |
| `service.ToCallJson(request)` | 描述一次对 `service` 的调用：`{ "service": 服务名, "arguments": 请求 }`，用于让其他插件稍后代为调用 |

## 规则与行为

### 服务名

- 最长 200 个字符，不能有空白，区分大小写。
- 同一个名字同时只能由一个插件提供，重复提供会抛出 `InvalidOperationException`。
- 建议用“领域.动作”的形式，并以插件或领域名开头，例如 `meme.send`、`weather.query`，避免和其他插件撞名。

### 消息内容

- 请求和响应都按 JSON 复制一份再交给对方，双方不会持有对方创建的对象。
- 大文件、图片不要放进消息，传文件路径、ID 或 URL。
- 每次调用都会序列化和复制，适合普通大小的请求，不适合在热路径上高频传大对象。

### 失败和取消

| 情况 | `TryCallAsync` | `CallAsync` |
| --- | --- | --- |
| 没有插件提供这个服务 | 返回 `null` | 抛出 `PluginServiceUnavailableException` |
| 提供方正在卸载 | 返回 `null` | 抛出 `PluginServiceUnavailableException` |
| 提供方的处理函数抛出异常 | 抛出 `PluginServiceException` | 同左 |
| 调用方取消 `ct` | 抛出 `OperationCanceledException` | 同左 |

`PluginServiceException` 只带异常类型名 `RemoteType` 和消息 `RemoteMessage`，不带原始异常对象。提供方出错通常是 bug，所以 `TryCallAsync` 也不会把它吞掉。

提供方收到的 `ct` 同时连着调用方的取消和提供方自己的卸载：任何一方取消，处理函数都应尽快结束。宿主不设调用超时，需要时由调用方自己传带超时的 `ct`。

### 事件

- 每个订阅者拿到独立的副本，并发处理；`EmitAsync` 等所有订阅者处理完才返回。
- 订阅者处理失败只记在订阅者自己的日志里，不会抛给发布方，也不影响其他订阅者。
- 同一个主题可以有任意多个订阅者，也可以没有订阅者（返回 0）。

## 生命周期与热更新

- **注册时机**：在 `LoadAsync` 里提供服务和订阅事件，它们登记在插件的生命周期上，卸载时自动注销。
- **加载顺序**：提供方和调用方谁先加载都可以。调用方在需要时才调用，不要在加载阶段就要求对方存在。
- **卸载**：插件开始卸载时，宿主立即停止它接收新的调用和事件，等它正在处理的调用和事件结束，再执行它的 `OnUnloadAsync` 并释放程序集。完整顺序见[生命周期作用域](./context-config#生命周期作用域)。
- **更新**：提供方替换 DLL 后热重载，重新加载时再次提供。调用方不需要任何操作，下一次调用就会用到新版本。
- **不记依赖**：调用、查找和事件都不会让宿主认为调用方依赖提供方，所以提供方随时可以卸载。

### 要不要声明依赖

按名称调用的双方**不需要**在 `BotPlugin` 里声明依赖。依赖声明表示“没有对方就不能工作”，会让对方无法单独卸载。可选的功能（对方在就用，不在就跳过）不应该声明依赖。

## 不引用契约时

调用方也可以不放入提供方的 DLL，自己声明消息格式，或直接用宿主的 JSON 接口。适合只读一两个字段、或写转发和调试这类通用工具的场景：

```csharp
// 直接用 JSON 接口调用
var result = await Context.Services.CallAsync("weather.query", new JsonObject { ["city"] = "北京" }, ct);

// 只读一两个字段时，可以自己声明一个 record 来读结果，只写用到的字段
public sealed record WeatherReply(string Text);
var reply = result.As<WeatherReply>();
```

这几种写法在线上完全一样，可以混用。直接用 JSON 接口时只放字符串、数字、布尔、数组和 `JsonObject`，不要用 `JsonValue.Create(自定义对象)`，原因见[契约是怎么工作的](#契约是怎么工作的)。

## 示例

仓库 `samples/PluginInterop` 有一对可运行的示例：`Greeter` 声明契约、提供 `greeter.greet` 服务并发布 `greeter.greeted` 事件；`GreeterClient` 放入 `Greeter` 的 DLL 后调用服务、订阅事件，并演示提供方热更新时调用方不受影响。
