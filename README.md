<div align="center">

<a href="https://github.com/greepar/ShiroBot">
  <img src="./shirobana.webp" alt="ShiroBot" width="220" />
</a>

<p><strong><span style="font-size: 2.2em;">ShiroBot</span></strong></p>

<p><em>一个轻量的、基于 C# / .NET 10 实现的机器人框架。</em></p>

</div>

## 项目结构

- `ShiroBot`: 主程序
- `ShiroBot.SDK`: 插件与适配器开发 SDK
- `ShiroBot.Model`: 共享模型
- `ShiroBot.DemoPlugin`: 标准示例插件
- `templates/ShiroBot.PluginTemplate`: 可复制插件模板
- `ShiroBot.DemoAdapter`: 标准示例适配器
- `ShiroBot.AvaloniaIntegration`: 可选 Avalonia Headless 渲染集成模块
- `ShiroBot.AvaloniaSdk`: 插件侧使用的 Avalonia 渲染 SDK
- `ShiroBot.AvaloniaDemoPlugin`: Avalonia 渲染示例插件

## 构建

```powershell
dotnet build .\ShiroBot.slnx
```

默认构建会启用 Avalonia 渲染集成，并把 Avalonia 运行时、`ShiroBot.AvaloniaIntegration`、`ShiroBot.AvaloniaSdk` 和示例插件一起放入宿主输出目录。

如需构建不包含 Avalonia 的轻量版本：

```powershell
dotnet build .\ShiroBot.slnx -p:EnableAvalonia=false
```

禁用后宿主不会引用 Avalonia/Skia/HarfBuzz，也不会复制 `ShiroBot.AvaloniaDemoPlugin`。

## 快速创建插件

基于模版插件(DemoPlugin)生成：

```bash
./scripts/new-plugin.sh HelloPlugin 你好插件
```

执行后会自动生成一个新的插件目录，并替换项目名、类名、配置类名、命名空间和元数据占位符。

如果不想跑脚本，也可以手动复制 `templates/ShiroBot.PluginTemplate` 并全局替换 `__PLUGIN_NAME__` 和 `__PLUGIN_DISPLAY_NAME__`。

## 许可证

本项目使用 GNU General Public License v3.0。
详见 [LICENSE](/C:/Users/greep/RiderProjects/QB/QBotSharp/LICENSE)。
