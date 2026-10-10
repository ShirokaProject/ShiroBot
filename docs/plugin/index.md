# 创建第一个插件

本章创建一个可以响应群聊和私聊消息的单 DLL 插件。后续按用途阅读 [接收消息与事件](/plugin/routes-events)、[调用 API](/plugin/apis)、[插件操作与控制台](/plugin/actions) 和 [Model](/plugin/models)。

## 环境要求

- .NET SDK 10
- 支持 `net10.0` 的 IDE，例如 Rider 或 Visual Studio
- 与目标宿主版本一致的 `ShiroBot.SDK` NuGet 包（由模板项目引用）

## 创建项目

```bash
dotnet new install ShiroBot.Templates
dotnet new shirobot-plugin -n HelloPlugin --creator "Your Name" --allow-scripts yes
cd HelloPlugin
```

生成项目的结构如下，命令均从项目根目录执行：

```text
HelloPlugin/
├── src/Plugin.cs
├── scripts/dev.sh
├── scripts/dev.ps1
├── Properties/launchSettings.json
├── HelloPlugin.csproj
├── Directory.Packages.props
└── Directory.Build.targets
```

也可以参考示例仓库：[DemoPlugin](https://github.com/ShirokaProject/Shirobot.Plugin.DemoPlugin)、[AvaloniaDemo](https://github.com/ShirokaProject/Shirobot.Plugin.AvaloniaDemo)。

模板生成 `HelloPlugin.csproj`、`src/Plugin.cs` 和 `Directory.Packages.props`，后者指定 SDK 包版本。默认的 `src/Plugin.cs` 已包含一个回复 `pong` 的 `ping` 命令。`ShiroBot.SDK` 包已经包含 QQ、Discord 和 Telegram 的 Model，使用它们不需要再安装 NuGet 包。`--platform qq`、`--platform discord` 或 `--platform telegram` 会在生成的 `src/Plugin.cs` 中声明对应的运行时 Model 依赖；省略时仍可使用通用 SDK 类型。

::: warning 使用 NuGet 引用
自动 ILRepack 和 native 清单来自 SDK 包内的 `buildTransitive`。直接 `ProjectReference` 到 `ShiroBot.SDK.csproj` 不会导入已打包的自动化目标，最终分发测试必须使用 NuGet 包。
:::

## 编写插件

打开模板生成的 `src/Plugin.cs`，把默认的 `ping` 路由改成群聊和私聊命令：

```csharp
using ShiroBot.SDK.Abstractions;
using ShiroBot.SDK.Core;
using ShiroBot.SDK.Models;
using ShiroBot.SDK.Plugin;

[assembly: ShiroBotApiCompatibility("0.9.2", "0.9.2")]

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

#### Rider：直接 Run / Debug

1. 打开生成的项目或解决方案。通过 Rider 的新建项目窗口选择 **ShiroBot Plugin** 也可以；是否将解决方案和项目放在同一目录不影响运行。
2. 将构建配置设为 **Debug**，保留运行配置中的“启动前构建”步骤。
3. 首次使用先构建项目。如果创建项目时没有执行准备脚本，Debug 构建会自动下载与 `Directory.Packages.props` 中 SDK 版本匹配的宿主，并复制插件及调试符号。
4. 选择唯一的 **ShiroBot** 运行配置，点击三角形运行，或点击小虫子调试。该配置直接启动 .NET 宿主，Run 与 Debug 共用，不需要再选择单独的 Debug 配置。
5. 在 `src/Plugin.cs` 的 `ping` 处理方法中设置断点。到开发宿主的 Dashboard 添加并启用适配器实例，连接机器人后发送 `#ping`；插件会回复 `pong`，调试时会命中断点。

创建项目时自动选择当前操作系统，宿主架构由当前 .NET SDK 的 RID 决定，例如 `osx-arm64`。`Properties/launchSettings.json` 定义启动入口；开发宿主位于项目目录下的 `.shirobot-dev/host/<RID>/`。

新开发宿主的 Dashboard 地址是 `http://127.0.0.1:7002/dashboard/`，登录 Token 首次启动时自动生成，保存在该开发宿主的 `config.toml` 的 `[api].token` 中。没有适配器连接时，宿主能加载插件，但不会产生聊天消息供插件响应。

Debug 构建使用独立 DLL 和 portable PDB，方便断点调试；Release 构建使用分发打包规则。C# Hot Reload 是否能应用取决于 IDE 和修改类型，不能应用时停止宿主、重新构建并启动。Release 包不包含 PDB。

#### 命令行启动

模板项目附带 `scripts/dev.sh`（macOS / Linux）与 `scripts/dev.ps1`（Windows）。在项目目录运行：

```bash
sh scripts/dev.sh
```

```powershell
powershell -ExecutionPolicy Bypass -File scripts/dev.ps1
```

Run、Debug 和脚本启动默认允许控制台交互；需要关闭输入时才手动传入 `--no-console`。

脚本会 Debug 构建、准备宿主、复制插件并启动。只准备、不启动时加 `--prepare`。`.shirobot-dev/` 已被 `.gitignore` 忽略。升级 SDK 后会更新宿主程序，保留配置、适配器和插件数据。常规开发时修改代码后停止宿主，再重新构建并运行。

#### 常见启动问题

| 现象 | 处理 |
| --- | --- |
| Rider 显示“无效的可执行文件路径” | 先执行一次 Debug 构建，确认 `.shirobot-dev/host/<RID>/ShiroBot`（Windows 为 `ShiroBot.exe`）存在，再启动。 |
| 下载宿主返回 404 | 核对 SDK 版本是否已有同版本宿主 Release；未发布的开发版需要自行准备本地宿主，不能从 Release 自动下载。 |
| 点击 Debug 后仍使用旧插件 | 检查启动前 Build；Rider 启用 ReSharper Build 时可能跳过自定义构建步骤，可在 Toolset and Build 关闭 Use ReSharper Build，让 MSBuild 执行构建与复制。 |
| 断点没有绑定 | 确认是 Debug 构建、通过小虫子启动，并且复制了对应 DLL/PDB。等待插件加载，再触发对应消息。 |
| 宿主启动但 `#ping` 没有响应 | 检查适配器实例是否已连接、插件是否启用，以及路由是否允许该会话。 |
| 提示端口被占用 | 停止另一个开发宿主，或修改开发宿主 `config.toml` 中的 `[api].listen_urls`。 |

#### 测试尚未发布的宿主 / SDK

这只适用于开发版本测试，正式发布的版本不需要手动准备：

1. 从同一份源码构建 SDK 和 Templates NuGet 包，将模板包用 `dotnet new install <模板包路径>` 安装；给生成项目配置本地 NuGet 源，确保 SDK 从该目录还原。已有同版本本地 SDK 缓存时，确认没有继续使用旧构建。
2. 从对应源码发布当前系统/架构的宿主，将发布目录中的文件放入项目的 `.shirobot-dev/host/<RID>/`，保留可执行权限。
3. 在该目录创建 `.host-version` 文本文件，内容为 `Directory.Packages.props` 中的 SDK 产品版本，例如 `1.0.0`。脚本仅在可执行文件存在且版本标记匹配时跳过自动下载。
4. 再执行 Debug 构建并使用同一个 **ShiroBot** 配置调试。不要用旧版本宿主冒充新的 SDK 版本。

使用 `dotnet new shirobot-plugin` 生成的项目会带 `.github/workflows/release.yml`。推送代码或提交 PR 时自动构建；将 `src/Plugin.cs` 中的 `Version` 改为目标版本后推送同版本 tag（如 `v1.0.0`），Action 会把 Release 构建输出的 ZIP 和入口 DLL 上传到 GitHub Release。

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
       "shirobot": ">=0.9.4",
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
