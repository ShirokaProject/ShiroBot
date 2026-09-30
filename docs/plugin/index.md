# 创建第一个插件

本章创建一个可以响应群聊和私聊消息的单 DLL 插件。后续按用途阅读 [接收消息与事件](/plugin/routes-events)、[调用 API](/plugin/apis)、[插件操作与控制台](/plugin/actions) 和 [Model](/plugin/models)。

## 环境要求

- .NET SDK 10
- 支持 `net10.0` 的 IDE，例如 Rider 或 Visual Studio
- 与目标宿主版本一致的 `ShiroBot.SDK` NuGet 包（由模板项目引用）

## 创建项目

```bash
dotnet new install ShiroBot.Templates
dotnet new shirobot-plugin -n HelloPlugin --creator "Your Name"
cd HelloPlugin
```

也可以参考示例仓库：[DemoPlugin](https://github.com/ShirokaProject/Shirobot.Plugin.DemoPlugin)、[AvaloniaDemo](https://github.com/ShirokaProject/Shirobot.Plugin.AvaloniaDemo)。

模板生成 `HelloPlugin.csproj`、`Plugin.cs` 和 `Directory.Packages.props`，后者指定 SDK 包版本。默认的 `Plugin.cs` 已包含一个回复 `pong` 的 `ping` 命令。`ShiroBot.SDK` 包已经包含 QQ、Discord 和 Telegram 的 Model，使用它们不需要再安装 NuGet 包。`--platform qq`、`--platform discord` 或 `--platform telegram` 会在生成的 `Plugin.cs` 中声明对应的运行时 Model 依赖；省略时仍可使用通用 SDK 类型。

::: warning 使用 NuGet 引用
自动 ILRepack 和 native 清单来自 SDK 包内的 `buildTransitive`。直接 `ProjectReference` 到 `ShiroBot.SDK.csproj` 不会导入已打包的自动化目标，最终分发测试必须使用 NuGet 包。
:::

## 编写插件

打开模板生成的 `Plugin.cs`，把默认的 `ping` 路由改成群聊和私聊命令：

```csharp
using ShiroBot.SDK.Abstractions;
using ShiroBot.SDK.Core;
using ShiroBot.SDK.Models;
using ShiroBot.SDK.Plugin;

[assembly: ShiroBotApiCompatibility("0.9.1", "0.9.1")]

namespace HelloPlugin;

[BotPlugin(
    "HelloPlugin",
    Name = "Hello Plugin",
    Version = "1.0.0",
    Description = "ShiroBot 插件示例",
    Author = "YourName",
    Category = PluginCategory.Utility,
    IsPluginSingleFile = true)]
public sealed class Plugin : PluginBase
{
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

### 本地调试

模板项目附带 `dev.sh`（macOS / Linux）与 `dev.ps1`（Windows）。脚本会编译插件，下载与 `ShiroBot.SDK` 版本一致的宿主到 `.shirobot-dev/`，安装编译结果后启动宿主：

```bash
sh dev.sh --no-console
```

```powershell
powershell -ExecutionPolicy Bypass -File dev.ps1
```

`.shirobot-dev/` 已被 `.gitignore` 忽略。升级 SDK 版本后只会替换宿主程序，配置、适配器和插件数据都会保留。修改插件后先停止宿主再重新运行脚本。

使用 `dotnet new shirobot-plugin` 生成的项目会带 `.github/workflows/release.yml`。推送代码或提交 PR 时自动构建；将 `Plugin.cs` 中的 `Version` 改为目标版本后推送同版本 tag（如 `v1.0.0`），Action 会把 Release 构建输出的 ZIP 和入口 DLL 上传到 GitHub Release。

## 上架插件市场

ShiroBot 的插件市场读取 [awesome-shirobot](https://github.com/ShirokaProject/awesome-shirobot) 生成的清单。插件发布到自己的 GitHub 仓库后，按以下步骤申请收录：

1. 确认插件仓库公开可访问，并发布一个非草稿、非预发布的 GitHub Release。模板工作流会上传 `HelloPlugin.zip` 和 `HelloPlugin.dll`；选择其中一个作为市场下载资产。
2. Fork `awesome-shirobot`，只在 [`list.json`](https://github.com/ShirokaProject/awesome-shirobot/blob/main/list.json) 的 `plugins` 数组中增加条目，不要手工修改 `dist/`。例如：

   ```json
   {
     "id": "hello-plugin",
     "kind": "plugin",
     "name": "HelloPlugin",
     "description": "回复群聊 ping 和私聊 hello 命令。",
     "category": "utility",
     "authors": [{ "name": "Your Name", "url": "https://github.com/YOUR_NAME" }],
     "repository": "https://github.com/YOUR_NAME/HelloPlugin",
     "license": "NOASSERTION",
     "compatibility": {
       "shirobot": ">=0.9.4 <1.0.0",
       "framework": "net10.0"
     },
     "release": { "required": true, "assetPattern": "HelloPlugin.zip" },
     "deprecated": false
   }
   ```

3. 把示例中的仓库、作者、兼容范围、许可证和资产名改成实际值。`id` 必须是唯一的小写 kebab-case；`assetPattern` 应在最新 Release 中只匹配一个已上传文件，推荐填写精确文件名。确认许可证时填写对应的 SPDX 标识；`NOASSERTION` 表示尚无法确认。
4. 在 `awesome-shirobot` 仓库根目录运行 `node scripts/build-market.mjs --validate-only`（需要 Node.js 24），然后向 `main` 提交 PR，在说明中写明插件用途、兼容版本、许可证和 Release 资产名。

PR 合并后，市场清单会自动刷新；宿主缓存约 24 小时，可在 Dashboard 中手动刷新市场。市场条目的 `id` 与 `BotPlugin` ID 可以不同；若希望市场准确识别已安装插件，建议在 `BotPlugin` 元数据中设置 `GithubRepo = "YOUR_NAME/HelloPlugin"`，与清单的仓库地址对应。完整规则见 [awesome-shirobot README](https://github.com/ShirokaProject/awesome-shirobot#贡献条目)。

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
