# Telegram 机器人接口

仅机器人账号（BotFather 创建的机器人）实现；用户账号适配器上为 null。

插件通过 `Context.GetAdapterExtension<T>()` 探测；用 `ITelegramClientApi.AccountKind` 也可以先判断当前实例的账号类型。

| 能力 | C# 接口 | 文档 |
| --- | --- | --- |
| inline 模式 | `ITelegramInlineApi` | [inline 模式](./inline) |
| 支付 | `ITelegramPaymentApi` | [支付](./payment) |
| 命令菜单与资料 | `ITelegramBotProfileApi` | [命令菜单与资料](./bot-profile) |
| Bot API 原始调用 | `ITelegramBotApi` | [Bot API 原始调用](./bot-api) |
