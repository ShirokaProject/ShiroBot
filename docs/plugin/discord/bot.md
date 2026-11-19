# Discord 机器人接口

仅机器人账号（Developer Portal 创建的机器人）实现；用户账号适配器上为 null。

插件通过 `Context.GetAdapterExtension<T>()` 探测；用 `IDiscordClientApi.AccountKind` 也可以先判断当前实例的账号类型。

| 能力 | C# 接口 | 文档 |
| --- | --- | --- |
| 交互与应用命令 | `IDiscordInteractionApi` | [交互与应用命令](./interaction) |
