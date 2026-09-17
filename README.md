<div align="center">

<a href="https://github.com/ShirokaProject/ShiroBot">
  <img src="./shirobana.webp" alt="ShiroBot" width="220" />
</a>

<p><strong><span style="font-size: 2.2em;">ShiroBot</span></strong></p>

<p><em>一个轻量的、基于 C# / .NET 10 实现的机器人框架。</em></p>

</div>

## 插件与适配器
这里收集了 ShiroBot 的插件与适配器列表，欢迎到此处提交 PR 添加你的插件或适配器。
- [awesome-shirobot](https://github.com/ShirokaProject/awesome-shirobot)

## 项目结构

- `Core`: ShiroBot 宿主本体，内置 Avalonia Headless 渲染集成
- Dashboard: 宿主 Web 面板前端。独立仓库 [Shirobot.Dashboard](https://github.com/ShirokaProject/Shirobot.Dashboard)，构建时按 `ShirobotDashboardVersion` 拉取其 Release 成品 `dist` 并嵌入宿主程序集
- `Models`: Discord、QQ、Telegram 平台契约
- `SDK`: 插件与适配器 SDK，内置 Avalonia 控件渲染契约和编译支持
- `Templates`: `dotnet new` Plugin 与 Adapter 项目模板
- `Tests`: 主仓库验证与共享契约探针

NuGet 包版本由根目录 [`Directory.Packages.props`](./Directory.Packages.props) 统一管理。项目文件只声明包引用，不单独指定版本。

插件和适配器是独立开发仓库，分别位于主仓库同级的 `../plugins` 和 `../adapters`，不纳入 ShiroBot 主仓库。

Discord、QQ 和 Telegram Model 随宿主内置。宿主不创建或扫描独立的 `models/` 目录，也不支持运行时安装第三方 Model；第三方平台能力与业务扩展应实现为标准 Plugin，并放入运行时 `plugins/` 目录加载。

## 构建

```powershell
dotnet build .\ShiroBot.slnx
```

构建宿主机时会自动从 Shirobot.Dashboard 仓的 Release 下载 `ShirobotDashboardVersion` 对应的前端成品并嵌入程序集，因此不需要 Node.js 或子模块。离线或未发布对应版本时构建仍会成功，但 `/dashboard` 不可用；发布流程会以 `-p:RequireDashboard=true` 强制校验资源存在。本地联调未发布的前端改动可用 `-p:DashboardDistPath=<本地 dist 目录>`。

Avalonia、Skia 和 HarfBuzz 从 0.7.0 起属于统一宿主，不再提供 `lite` 构建。宿主明确保持 `PublishTrimmed=false`，因为插件发现、配置 schema 和程序集加载依赖反射与 metadata，当前不具备安全裁剪条件。

## 项目模板

安装模板包：

```bash
dotnet new install ShiroBot.Templates
```

创建 Plugin：

```bash
dotnet new shirobot-plugin -n MyPlugin --creator "Your Name"
```

创建 Discord Adapter：

```bash
dotnet new shirobot-adapter -n MyDiscordAdapter --platform discord --creator "Your Name"
```

`--platform` 支持 `generic`、`qq`、`discord` 和 `telegram`。使用 `--help` 查看版本等其他参数。

## 文档

安装使用、插件开发和适配器开发文档位于 [`docs`](./docs)。本地启动 VitePress：

```bash
cd docs
npm install
npm run dev
```

构建静态文档：

```bash
cd docs
npm run build
```

## 插件模板

插件模板请使用独立示例仓库作为起点：

- [Shirobot.Plugin.DemoPlugin](https://github.com/ShirokaProject/Shirobot.Plugin.DemoPlugin)
- [Shirobot.Plugin.AvaloniaDemo](https://github.com/ShirokaProject/Shirobot.Plugin.AvaloniaDemo)

适配器模板:

- [Shirobot.Adapter.DemoAdapter](https://github.com/ShirokaProject/Shirobot.Adapter.DemoAdapter)

## 许可证

本项目使用 GNU General Public License v3.0。
详见 [LICENSE](./LICENSE)。
