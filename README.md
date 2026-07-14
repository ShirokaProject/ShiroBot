<div align="center">

<a href="https://github.com/greepar/ShiroBot">
  <img src="./shirobana.webp" alt="ShiroBot" width="220" />
</a>

<p><strong><span style="font-size: 2.2em;">ShiroBot</span></strong></p>

<p><em>一个轻量的、基于 C# / .NET 10 实现的机器人框架。</em></p>

</div>

## 插件与适配器
这里收集了 ShiroBot 的插件与适配器列表，欢迎到此处提交 PR 添加你的插件或适配器。
- [awesome-shirobot](https://github.com/ShirokaProject/awesome-shirobot)

## 项目结构

- `ShiroBot`: 主程序，内置 Avalonia Headless 渲染集成
- `ShiroBot.SDK`: 插件与适配器基础 SDK
- `ShiroBot.AvaloniaSdk`: 可选的 Avalonia 控件渲染 SDK
- `ShiroBot.Model`: 共享模型

## 构建

```powershell
dotnet build .\ShiroBot.slnx
```

Avalonia、Skia 和 HarfBuzz 从 0.7.0 起属于统一宿主，不再提供 `lite` 构建。宿主明确保持 `PublishTrimmed=false`，因为插件发现、配置 schema 和程序集加载依赖反射与 metadata，当前不具备安全裁剪条件。

## 0.7.0 SDK 结构

- 普通插件与适配器只引用 `ShiroBot.SDK` 0.7.0。
- 使用 Avalonia 控件的插件额外引用 `ShiroBot.AvaloniaSdk` 0.7.0，命名空间保持 `ShiroBot.AvaloniaSdk`。
- `ShiroBot.AvaloniaSdk` 保持与 0.6 插件相同的程序集名和公共渲染契约。
- Host 统一内置 Avalonia，不再支持 `EnableAvalonia` 与 `full`/`lite` 发布矩阵。
- SDK 自动从插件输出移除宿主共享的 Avalonia、SkiaSharp、HarfBuzzSharp、MicroCom managed/native/runtime 资产和 `.deps.json`。
- Dashboard API 新增插件 actions 与插件市场；插件配置 PATCH 现在返回与 GET 相同的 `schema`。
- 新适配器应使用 `BotAdapterAttribute`，旧 `Metadata` 实现仍可加载。

未引用 `ShiroBot.AvaloniaSdk` 的插件不会获得 Avalonia 编译依赖，因此基础插件仍保持轻量。

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
详见 [LICENSE](/C:/Users/greep/RiderProjects/QB/QBotSharp/LICENSE)。
