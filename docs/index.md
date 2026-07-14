---
layout: home

hero:
  name: ShiroBot
  text: 轻量、可扩展的 .NET 机器人框架
  tagline: 使用 C# 与 .NET 10 构建插件和协议适配器，支持事件路由、热加载、自动单 DLL 打包与跨平台 native 依赖。
  actions:
    - theme: brand
      text: 安装 ShiroBot
      link: /guide/installation
    - theme: alt
      text: 开发插件
      link: /plugin/
    - theme: alt
      text: 开发适配器
      link: /adapter/

features:
  - icon: 🧩
    title: 插件化
    details: 插件拥有独立、可回收的程序集加载上下文，支持命令路由、事件订阅与热加载。
    link: /plugin/
    linkText: 开始开发插件
  - icon: 📦
    title: 自动打包
    details: 引入 SDK NuGet 包后自动使用 ILRepack 合并 managed 依赖，并把 native NuGet 依赖清单嵌入插件 DLL。
    link: /plugin/packaging-native
    linkText: 了解打包流程
  - icon: 🔌
    title: 协议无关
    details: 通过适配器将不同机器人协议映射为统一的消息、群组、好友、文件和事件接口。
    link: /adapter/
    linkText: 开发适配器
  - icon: 🎨
    title: Avalonia 渲染
    details: 统一宿主内置 Avalonia 12.1 Headless 渲染，可让插件将 AXAML 控件输出为 PNG。
    link: /plugin/avalonia
    linkText: 渲染图片
---

## 从这里开始

- 第一次使用：阅读[安装与启动](/guide/installation)。
- 已经运行起来：继续配置[适配器、管理员、API 和插件路由](/guide/configuration)。
- 编写功能扩展：从[第一个插件](/plugin/)开始。
- 接入新的机器人协议：阅读[适配器开发](/adapter/)。
