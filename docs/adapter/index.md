# 创建适配器

适配器把某个机器人协议的 API 与事件转换为 ShiroBot 统一接口。插件只依赖这些统一接口，因此同一插件可以运行在不同适配器上。

## 创建项目

```bash
dotnet new classlib -n ExampleAdapter -f net10.0
cd ExampleAdapter
dotnet add package ShiroBot.SDK --version 0.7.0
```

SDK 会通过 `BotAdapterAttribute` 自动识别适配器，并生成与插件一致的单 DLL 产物。宿主会读取嵌入的 native 依赖清单、校验 NuGet 包并按当前 RID 准备 native 文件，因此不需要手写 ILRepack：

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
    <ImplicitUsings>enable</ImplicitUsings>
    <Nullable>enable</Nullable>
    <ShiroBotPluginPackagingEnabled>true</ShiroBotPluginPackagingEnabled>
  </PropertyGroup>

  <ItemGroup>
    <PackageReference Include="ShiroBot.SDK" Version="0.7.0" />
  </ItemGroup>
</Project>
```

## 实现 IBotAdapter

```csharp
using ShiroBot.SDK.Adapter;
using ShiroBot.SDK.Config;
using ShiroBot.SDK.Core;
using ShiroBot.SDK.Plugin;

namespace ExampleAdapter;

[BotAdapter(
    "ExampleAdapter",
    Name = "Example Adapter",
    Version = "1.0.0",
    Description = "Example 协议适配器",
    Author = "YourName",
    GithubRepo = "owner/example-adapter",
    Protocol = "example",
    ProtocolVersionRange = ">=1.0 <2.0",
    IsSingleFile = true)]
public sealed class ExampleAdapter : IBotAdapter
{
    private readonly ExampleEventService _events = new();

    public string Name => "ExampleAdapter";

    // 由宿主在 StartAsync 前注入。
    public IConfigContext Config { get; set; } = null!;
    public IConsoleLogger Logger { get; set; } = null!;

    public IMessageService Message { get; } = new ExampleMessageService();
    public IGroupService Group { get; } = new ExampleGroupService();
    public IFriendService Friend { get; } = new ExampleFriendService();
    public IFileService File { get; } = new ExampleFileService();
    public ISystemService System { get; } = new ExampleSystemService();
    public IEventService Event => _events;

    public async Task StartAsync()
    {
        var config = Config.Load<ExampleAdapterConfig>();
        Config.Save(config);

        Logger.Info($"正在连接 {config.BaseUrl}");

        await ProtocolClient.ConnectAsync(
            config.BaseUrl,
            config.AccessToken,
            _events.PublishAsync);

        Logger.Success("协议连接成功");
    }
}
```

宿主的加载顺序是：

1. 创建适配器程序集加载上下文。
2. 查找并实例化第一个非抽象 `IBotAdapter` 实现。
3. 注入 `Config` 和 `Logger`。
4. 优先读取 `BotAdapterAttribute`；没有 attribute 时回退旧 `Metadata` 实现。
5. 等待 `StartAsync()` 完成。
6. 创建 Bot 上下文并桥接 `Event` 中的事件。
7. 开始加载插件。

因此 `StartAsync()` 返回前应完成必要的鉴权和基础连接验证。持续事件循环可以在后台运行，但必须把异常记录清楚并实现重连策略。

`IBotAdapter.Metadata` 从 0.7.0 起具有默认实现并标记为 obsolete。旧 Milky adapter 等已经自行实现 `Metadata` 的二进制仍可加载，新适配器应只声明 `BotAdapterAttribute`。

## 配置类型

```csharp
public sealed class ExampleAdapterConfig
{
    public string BaseUrl { get; set; } = "http://127.0.0.1:3000";
    public string AccessToken { get; set; } = string.Empty;
    public string Transport { get; set; } = "websocket";
}
```

不要在源代码中硬编码令牌。配置文件位置和部署方式见[配置与部署](/adapter/deployment)。

## 参考实现

- [Shirobot.Adapter.DemoAdapter](https://github.com/ShirokaProject/Shirobot.Adapter.DemoAdapter)
- 仓库中的 `Shirobot.MilkyAdapter` 实现
