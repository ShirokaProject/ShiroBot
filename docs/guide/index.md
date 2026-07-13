# 认识 ShiroBot

ShiroBot 是一个基于 C# 和 .NET 10 的机器人宿主。宿主不直接绑定某一种协议，而是通过“适配器 + 插件”组合工作：

- **适配器**连接实际机器人协议，负责收发消息、调用协议 API 和上报事件。
- **插件**实现业务功能，只依赖统一 SDK，不需要了解底层使用 Milky、OneBot 或其他协议。
- **宿主**负责配置、插件发现、事件路由、API、热加载和程序集生命周期。

## 运行结构

```text
机器人协议 / 实现端
        │
        ▼
     适配器 DLL
        │  统一 SDK 服务与事件
        ▼
   ShiroBot 宿主进程
        │
        ├── 插件 ALC: PluginA
        ├── 插件 ALC: PluginB
        └── 插件 ALC: PluginC
```

每个插件使用独立、可回收的 `AssemblyLoadContext`，便于隔离程序集版本和热卸载。所有插件仍运行在同一个宿主进程内，因此共享进程级 GC 堆、JIT 和 native 内存。

## 两种宿主版本

发布产物通常包含两种功能版本：

| 版本 | 特点 | 适用场景 |
| --- | --- | --- |
| `full` | 包含 Avalonia、Skia 和 HarfBuzz Headless 渲染 | 需要插件生成图片、卡片或复杂排版 |
| `lite` | 不包含 Avalonia 渲染模块，体积和基础内存更小 | 纯文本、消息处理、API 类插件 |

两种版本的插件与适配器接口相同。使用 Avalonia 的插件在 `lite` 宿主中应检查 `Context.Render` 是否可用。

## 开发技术栈

- .NET SDK 10
- C# 14 / `net10.0`
- TOML 配置
- NuGet 插件 SDK
- 可选 Avalonia 12.1 Headless 渲染

接下来可以直接[安装 ShiroBot](/guide/installation)，或者进入[插件开发](/plugin/)与[适配器开发](/adapter/)。
