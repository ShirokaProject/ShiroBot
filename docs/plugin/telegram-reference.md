# Telegram C# 接口参考

本页按插件调用方式列出 Telegram Model 扩展接口的 C# 签名、参数、默认值与返回类型。
插件通过 `Context.GetAdapterExtension<T>()` 获取当前实例的服务，不需要引用适配器程序集。

机器人适配器和用户账号适配器共用这套 Model，平台 ID 都是 `telegram`；各自只实现自己账号能用的接口，其他接口返回 `null`。版本要求与兼容规则见 [ABI 兼容性与版本](./api-compatibility)。

## 通用接口

两种账号都实现。

| 能力 | C# 接口 | 文档 |
| --- | --- | --- |
| 账号信息 | `ITelegramClientApi` | [账号信息](./telegram/client) |
| 会话管理 | `ITelegramChatApi` | [会话管理](./telegram/chat) |
| 原生消息 | `ITelegramMessageApi` | [原生消息](./telegram/message) |

## 按账号类型

- [机器人接口](./telegram/bot)：仅机器人账号（BotFather 创建的机器人）实现；用户账号适配器上为 null。
- [用户账号接口](./telegram/user)：仅用户账号（MTProto 登录）实现；机器人适配器上为 null。当前为预留契约，等待用户账号适配器实现。

## 模型与示例

- [完整 C# 类型](./telegram/types)
- [Telegram 特有能力与示例](./telegram-model)
