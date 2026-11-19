# Discord 用户账号接口

仅用户账号实现；机器人适配器上为 null。当前为预留契约。Discord 服务条款禁止自动化用户账号（self-bot），账号可能被封禁，是否使用由部署者自行承担。

插件通过 `Context.GetAdapterExtension<T>()` 探测；用 `IDiscordClientApi.AccountKind` 也可以先判断当前实例的账号类型。

| 能力 | C# 接口 | 文档 |
| --- | --- | --- |
| 账号操作 | `IDiscordAccountApi` | [账号操作](./account) |
