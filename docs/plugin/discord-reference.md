# Discord C# 接口参考

本页按插件调用方式列出 Discord Model 扩展接口的 C# 签名、参数、默认值与返回类型。
插件通过 `Context.GetAdapterExtension<T>()` 获取当前实例的服务，不需要引用适配器程序集。

机器人适配器和用户账号适配器共用这套 Model，平台 ID 都是 `discord`；各自只实现自己账号能用的接口，其他接口返回 `null`。版本要求与兼容规则见 [ABI 兼容性与版本](./api-compatibility)。

## 通用接口

两种账号都实现。

| 能力 | C# 接口 | 文档 |
| --- | --- | --- |
| 账号信息 | `IDiscordClientApi` | [账号信息](./discord/client) |
| 服务器管理 | `IDiscordGuildApi` | [服务器管理](./discord/guild) |
| 原生消息 | `IDiscordMessageApi` | [原生消息](./discord/message) |
| 原始调用 | `IDiscordRawApi` | [原始调用](./discord/raw) |

## 按账号类型

- [机器人接口](./discord/bot)：仅机器人账号（Developer Portal 创建的机器人）实现；用户账号适配器上为 null。
- [用户账号接口](./discord/user)：仅用户账号实现；机器人适配器上为 null。Discord 服务条款禁止自动化用户账号（self-bot），账号可能被封禁，是否使用由部署者自行承担。

## 模型与示例

- [完整 C# 类型](./discord/types)
- [Discord 特有能力与示例](./discord-model)
