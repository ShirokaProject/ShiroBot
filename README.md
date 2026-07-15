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

- `ShiroBot`: 主程序，内置 Avalonia Headless 渲染集成
- `ShiroBot.SDK`: 插件与适配器基础 SDK
- `ShiroBot.AvaloniaSdk`: 可选的 Avalonia 控件渲染 SDK
- `ShiroBot.Model`: 共享模型

## 构建

```powershell
dotnet build .\ShiroBot.slnx
```

Avalonia、Skia 和 HarfBuzz 从 0.7.0 起属于统一宿主，不再提供 `lite` 构建。宿主明确保持 `PublishTrimmed=false`，因为插件发现、配置 schema 和程序集加载依赖反射与 metadata，当前不具备安全裁剪条件。

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
