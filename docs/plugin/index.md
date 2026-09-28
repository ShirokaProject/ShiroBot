# 创建第一个插件

本章创建一个可以响应群聊和私聊消息的单 DLL 插件。后续按用途阅读 [接收消息与事件](/plugin/routes-events)、[调用 API](/plugin/apis)、[插件操作与控制台](/plugin/actions) 和 [Model](/plugin/models)。

## 环境要求

- .NET SDK 10
- 支持 `net10.0` 的 IDE，例如 Rider 或 Visual Studio
- 与目标宿主版本一致的 `ShiroBot.SDK` NuGet 包

## 创建项目

```bash
dotnet new classlib -n HelloPlugin -f net10.0
cd HelloPlugin
dotnet add package ShiroBot.SDK --version 0.9.3
```

项目文件可以保持精简：

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
    <ImplicitUsings>enable</ImplicitUsings>
    <Nullable>enable</Nullable>
  </PropertyGroup>

  <ItemGroup>
    <PackageReference Include="ShiroBot.SDK" Version="0.9.3" />
  </ItemGroup>
</Project>
```

::: warning 使用 NuGet 引用
自动 ILRepack 和 native 清单来自 SDK 包内的 `buildTransitive`。直接 `ProjectReference` 到 `ShiroBot.SDK.csproj` 不会导入已打包的自动化目标，最终分发测试必须使用 NuGet 包。
:::

## 编写插件

删除默认的 `Class1.cs`，创建 `HelloPlugin.cs`：

```csharp
using ShiroBot.SDK.Abstractions;
using ShiroBot.SDK.Core;
using ShiroBot.SDK.Models;
using ShiroBot.SDK.Plugin;

namespace HelloPlugin;

[BotPlugin(
    "HelloPlugin",
    Name = "Hello Plugin",
    Version = "1.0.0",
    Description = "ShiroBot 插件示例",
    Author = "YourName",
    Category = PluginCategory.Utility,
    IsPluginSingleFile = true)]
public sealed class Main : PluginBase
{
    public override string Name => "HelloPlugin";

    protected override void ConfigureRoutes()
    {
        GroupCommands.MapExact("#ping", HandleGroupPingAsync);
        DirectCommands.MapPrefix("#hello", HandleDirectHelloAsync);

        BotLog.Info("HelloPlugin 路由注册完成");
    }

    private Task HandleGroupPingAsync(MessageEvent message) =>
        Context.Message.ReplyAsync(message, "pong");

    private Task HandleDirectHelloAsync(MessageEvent message) =>
        Context.Message.ReplyAsync(message, $"你好，{message.Sender.Id}");
}
```

`BotPlugin` 的第一个参数是稳定插件 ID，宿主使用它去重、热卸载、匹配群路由和创建数据目录。发布新版本时不要随意修改 ID。

## 生命周期

大部分插件只需要 `ConfigureRoutes()`。宿主为插件设置 `Context` 后同步调用它，然后才读取并注册路由。

只有确实存在异步初始化时才重写 `LoadAsync()`：

```csharp
protected override async Task LoadAsync()
{
    await InitializeDatabaseAsync();
}
```

需要清理资源时重写 `OnUnloadAsync()`：

```csharp
protected override Task OnUnloadAsync()
{
    _timer?.Dispose();
    _cancellationTokenSource?.Cancel();
    _configWatcher?.Dispose();
    return Task.CompletedTask;
}
```

卸载完成后 `PluginBase` 会自动清空消息路由、事件路由和 `Context`。

`OnUnloadAsync()` 只保证在插件热卸载或更新时调用。宿主进程退出不会逐个卸载插件；需要持久化的
状态应在运行过程中及时保存，不要依赖进程退出时的插件回调。

## 构建和安装

```bash
dotnet build -c Release
```

SDK 会识别带有 `BotPluginAttribute` 的程序集并自动处理依赖。将以下文件复制到宿主：

```text
bin/Release/net10.0/HelloPlugin.dll
    ↓
ShiroBot/plugins/HelloPlugin.dll
```

启动宿主，或者在运行中的控制台执行：

```text
load HelloPlugin
```

然后发送 `#ping` 或 `#hello ShiroBot` 验证插件。

使用 `dotnet new shirobot-plugin` 生成的项目会带 `.github/workflows/release.yml`。推送代码或提交 PR 时自动构建；将 `Plugin.cs` 中的 `Version` 改为目标版本后推送同版本 tag（如 `v1.0.0`），Action 会把 Release 构建输出的 ZIP 和入口 DLL 上传到 GitHub Release。

## 元数据字段

| 字段 | 用途 |
| --- | --- |
| `id` | 稳定唯一 ID，必填 |
| `Name` | 显示名称 |
| `Version` | 插件版本 |
| `Description` | 插件说明 |
| `Author` | 作者 |
| `Category` | 插件分类 |
| `GithubRepo` | GitHub 仓库，例如 `owner/repo` |
| `IsPluginSingleFile` | 告诉宿主该 DLL 可以直接在插件根目录加载 |

## 插件操作

需要向控制台和 Dashboard 暴露插件操作时，实现 `IPluginActionProvider`。接口、示例和确认行为见[插件操作与控制台](/plugin/actions)。
