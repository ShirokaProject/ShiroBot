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

- `ShiroBot`: 主程序
- `ShiroBot.SDK`: 插件与适配器开发 SDK
- `ShiroBot.Model`: 共享模型
- `ShiroBot.AvaloniaIntegration`: 可选 Avalonia Headless 渲染集成模块
- `ShiroBot.AvaloniaSdk`: 插件侧使用的 Avalonia 渲染 SDK

## 构建

```powershell
dotnet build .\ShiroBot.slnx
```

默认构建会启用 Avalonia 渲染集成，如需构建不包含 Avalonia 的轻量版本：

```powershell
dotnet build .\ShiroBot.slnx -p:EnableAvalonia=false
```

禁用后宿主不会引用 Avalonia/Skia/HarfBuzz。

## 插件模板

插件模板请使用独立示例仓库作为起点：

- [Shirobot.Plugin.DemoPlugin](https://github.com/ShirokaProject/Shirobot.Plugin.DemoPlugin)
- [Shirobot.Plugin.AvaloniaDemo](https://github.com/ShirokaProject/Shirobot.Plugin.AvaloniaDemo)

适配器模板:

- [Shirobot.Adapter.DemoAdapter](https://github.com/ShirokaProject/Shirobot.Adapter.DemoAdapter)

## 许可证

本项目使用 GNU General Public License v3.0。
详见 [LICENSE](/C:/Users/greep/RiderProjects/QB/QBotSharp/LICENSE)。
