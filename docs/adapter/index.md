# 创建适配器

适配器把平台 API 与事件映射到 `ShiroBot.SDK.Models` 的通用模型。插件优先使用通用接口；需要 QQ 等平台特有操作时，再通过可选扩展接口访问。[适配不同 Model](/adapter/models)说明了两层模型如何配合。

## 创建项目

推荐从当前适配器模板开始，并选择平台：

```bash
dotnet new install ShiroBot.Templates
dotnet new shirobot-adapter -n MyQqAdapter --platform qq --creator "Your Name"
```

`--platform` 可选 `generic`、`qq`、`discord`、`telegram`。模板生成 `IBotAdapter` 骨架、SDK 引用和对应的内置 Model 依赖声明。若手动创建项目，需引用与宿主版本匹配的 `ShiroBot.SDK`，并在适配器类上标注 `BotAdapterAttribute`。示例仓库见 [DemoAdapter](https://github.com/ShirokaProject/Shirobot.Adapter.DemoAdapter)。

模板附带 `dev.sh` / `dev.ps1`：编译后把适配器安装为已启用的 Adapter，并启动与 SDK 版本一致的本地宿主（位于 `.shirobot-dev/`），可以直接在 Dashboard 中修改它的配置。用法与[插件的本地调试](/plugin/#本地调试)相同。

## 必须提供的成员

`IBotAdapter` 的接口形态如下：

| 成员 | 要求 | 用途 |
| --- | --- | --- |
| `Config`、`Logger` | 必须有可写属性 | 宿主在启动前注入配置和日志 |
| `Platform` | 必须返回稳定的平台 ID | 与上报事件的 `BotEvent.Platform` 一致，如 `qq` |
| `Message`、`Channel`、`User` | 必须提供非空服务对象 | 分别实现 `IMessageService`、`IChannelService`、`IUserService` |
| `Event` | 必须提供非空 `IEventService` | 通过 `EventReceived` 上报 `BotEvent` |
| `GetExtension<TService>()` | 可选重写 | 暴露 QQ 等平台特有扩展；默认返回适配器自身实现的接口 |
| `StartAsync()`、`StopAsync()` | 可选重写，由宿主调用 | 初始化连接和停止后台任务；默认实现为空操作 |

服务对象可以先使用空类，因为通用服务方法有默认实现，未实现的方法会抛 `NotSupportedException`。实际要提供的功能和方法列表见[实现服务接口](/adapter/services)。

```csharp
using ShiroBot.SDK.Adapter;
using ShiroBot.SDK.Config;
using ShiroBot.SDK.Core;
using ShiroBot.SDK.Plugin;

[BotAdapter("MyQqAdapter", Name = "My QQ Adapter", Version = "1.0.0")]
public sealed class MyQqAdapter : IBotAdapter
{
    private readonly MyEventService _events = new();

    public IConfigContext Config { get; set; } = null!;
    public IConsoleLogger Logger { get; set; } = null!;
    public string Platform => "qq";
    public IMessageService Message { get; } = new MyMessageService();
    public IChannelService Channel { get; } = new MyChannelService();
    public IUserService User { get; } = new MyUserService();
    public IEventService Event => _events;

    public Task StartAsync() => Task.CompletedTask;
    public Task StopAsync() => Task.CompletedTask;
}

internal sealed class MyMessageService : IMessageService;
internal sealed class MyChannelService : IChannelService;
internal sealed class MyUserService : IUserService;
```

`MyEventService` 的实现见[上报事件](/adapter/events)。上述空服务仅用于搭好结构；至少实现消息发送、机器人身份查询，以及协议端实际能上报的事件后，再用于真实连接。

## 启动顺序

宿主读取 `BotAdapterAttribute` 和 Model 依赖，实例化适配器，注入 `Config`、`Logger`，订阅 `Event.EventReceived`，然后调用 `StartAsync()`。因此可以在 `StartAsync()` 建立连接并开始接收事件。连接、鉴权或必要的初始化失败时应抛出异常；后台循环需要在 `StopAsync()` 中结束。

`StartAsync()` 和 `StopAsync()` 是宿主管理的生命周期入口。适配器作者按需**重写实现**它们，插件或其他业务代码通常不应主动调用，否则可能重复建立连接或绕过宿主的加载与卸载顺序。插件自己的初始化入口是 `PluginBase.LoadAsync()`，不是适配器的 `StartAsync()`。

配置文件与发布目录见[配置与部署](/adapter/deployment)。
