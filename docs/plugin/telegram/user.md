# Telegram 用户账号接口

仅用户账号（MTProto 登录）实现；机器人适配器上为 null。当前为预留契约，等待用户账号适配器实现。

插件通过 `Context.GetAdapterExtension<T>()` 探测；用 `ITelegramClientApi.AccountKind` 也可以先判断当前实例的账号类型。

| 能力 | C# 接口 | 文档 |
| --- | --- | --- |
| 账号操作 | `ITelegramAccountApi` | [账号操作](./account) |
