# QQ 官方机器人富消息

`ShiroBot.Model.QQ` 提供 QQ 官方开放平台的 Markdown、消息按钮和按钮点击事件模型。它们是可选的平台能力：官方适配器实现 `IQOfficialMessageApi` 后，插件才能使用它实际支持的方法；Milky 等不实现该接口的适配器会在探测时返回 `null`。

普通 Markdown、卡片和基础按钮优先使用[通用富消息与互动](/plugin/rich-messages)；需要模板键盘、Ark 或 QQ 专属参数时，再使用本文的平台扩展。

插件和适配器应使用与宿主匹配的 `ShiroBot.Model.QQ` 程序集。发送目标的 `Id` 在单聊和群聊中是开放平台的 **openid**，不能传普通 QQ 号或群号。

## 类型化发送

QQ 官方消息可通过 `QOfficialMessage` 统一发送。它把正文建模为纯文本、Markdown 或媒体三种互斥类型；Markdown 可以附键盘，图片可附普通文本说明。发送目标和被动回复信息仍单独传入。

```csharp
var official = Context.GetAdapterExtension<IQOfficialMessageApi>();
if (official is not null)
{
    await official.SendAsync(target, QOfficialMessage.Text("普通文本"), reply);
    await official.SendAsync(target,
        QOfficialMessage.Markdown(new QCustomMarkdown("## 标题\n正文"), keyboard), reply);

    await using var image = File.OpenRead(imagePath);
    await official.SendAsync(target,
        QOfficialMessage.Media(QOfficialMediaType.Image, image, "cat.jpg", "图片说明\n第二行"), reply);
}
```

媒体源在发送期间读取；调用方负责保持流打开并在发送后释放。已上传的 `QOfficialMedia` 也可传给 `QOfficialMessage.Media`，在后续被动回复中复用。

## 发送 Markdown 和按钮

下面的示例展示群聊发送。先探测具体目标、Markdown 和按钮形式，再调用发送接口；探测失败时走普通文本消息。

```csharp
using ShiroBot.Model.QQ;

var target = new QOfficialMessageTarget(QOfficialMessageScene.Group, groupOpenId);
var markdown = new QCustomMarkdown("## 查询结果\n点击下方按钮继续");
var keyboard = new QInlineKeyboard([
    new QKeyboardRow([
        new QKeyboardButton
        {
            Id = "next-page",
            RenderData = new QKeyboardRenderData(
                "下一页", "下一页", QKeyboardButtonStyle.Blue),
            Action = new QKeyboardAction
            {
                Type = QKeyboardActionType.Command,
                Data = "/next",
                Permission = new QKeyboardPermission
                {
                    Type = QKeyboardPermissionType.Everyone
                },
                UnsupportTips = "请更新 QQ 客户端",
                Enter = true
            }
        }
    ])
]);

var official = Context.GetAdapterExtension<IQOfficialMessageApi>();
if (official?.CanSendMarkdown(target, markdown, keyboard) == true)
    await official.SendMarkdownAsync(target, markdown, keyboard);
else
    await Context.Message.SendGroupMessageAsync(groupOpenId, "查询结果：请发送 /next 继续");
```

`QCustomMarkdown` 对应 `markdown.content`。使用已申请的 Markdown 模板时，改用 `QTemplateMarkdown`，并提供 `QMarkdownParameter` 列表。按钮也有两种形式：`QKeyboardTemplate` 对应 `keyboard.id`，`QInlineKeyboard` 对应 `keyboard.content.rows`。

Markdown 正文可以直接包含远程图片语法，再接正文与按钮。需要发送本地图片时，使用 `QOfficialMessage.Media`，可附加普通文本说明；不要把 Markdown 对象和本地媒体放进同一消息，因为官方接口按消息类型选择正文字段。

Embed 卡片、Ark 模板和 Markdown/按钮都通过 `IQOfficialMessageApi` 发送。`SendEmbedAsync` 使用 QQ 官方 `msg_type=4`；Embed 的字段包含标题、提示、缩略图和字段名称。目标场景会路由到 C2C、群聊、文字子频道或频道私信接口；频道私信的目标 ID 是官方 `guild_id`。

接口还定义了 `SendTextAsync` 和 `SendArkAsync`，分别发送官方文本和 Ark 模板消息。未实现的方法默认会抛出 `NotSupportedException`；拿到 `IQOfficialMessageApi` 并不表示当前适配器支持它们，调用时应处理该异常。私聊输入状态和流式回复使用独立的 `IQOfficialDirectMessageApi`；只有适配器实现并暴露该扩展后才能使用。

| 模型 | 用途 |
| --- | --- |
| `QOfficialMessageTarget` | 目标场景与开放平台 ID；频道私信使用 `guild_id` |
| `QCustomMarkdown` / `QTemplateMarkdown` | 自定义文本或模板 Markdown |
| `QOfficialEmbed` | QQ 官方 Embed 卡片 |
| `QKeyboardTemplate` / `QInlineKeyboard` | 已申请的按钮模板或自定义按钮 |
| `QKeyboardAction` | 跳转、回调或指令按钮的动作与权限 |
| `QOfficialMessageReply` | 被动回复时的消息 ID、事件 ID 和回复序号 |

官方接口将 Markdown 作为 `msg_type=2` 的 `markdown` 对象，并在同一条消息中附带 `keyboard`。自定义按钮最多 5 行、每行最多 5 个。单聊与群聊的自定义 Markdown 已开放；频道 Markdown 和自定义按钮仍受开放平台权限限制。`CanSendMarkdown` 表示适配器认为当前形式可用，发送仍可能因平台权限或频控失败。

## 处理按钮点击

回调按钮（`QKeyboardActionType.Callback`）点击后，官方平台推送 `INTERACTION_CREATE`，其中 `type=11` 是消息按钮。官方适配器映射为通用 `InteractionEvent`，携带来源实例、会话、用户、按钮 ID 和回调数据；`Raw` 保留 `QOfficialButtonInteraction`。

```csharp
using ShiroBot.SDK.Models;

protected override void ConfigureRoutes()
{
    Events.Map<InteractionEvent>(async evt =>
    {
        await HandleButtonAsync(evt.Data, evt.User.Id);
    });
}
```

`Data` 可能为空，业务应先检查所需字段，再校验点击用户的权限。需要 QQ 专属目标信息时，可以读取 `evt.Raw` 中的 `QOfficialButtonInteraction`。平台处理器 `Events.MapPlatform(QEventKinds.OfficialButtonInteraction, ...)` 仍可使用，但不要与通用处理器重复执行业务。

官方适配器在分发事件前自动确认按钮点击。插件可以检查 `Acknowledgement` 与 `IsAutomaticallyAcknowledged`；需要统一确认入口时，使用可选 `IMessageInteractionService.AcknowledgeAsync(evt)`，重复调用复用同一次请求及其结果。不要另行重复发送原始确认请求。指令按钮会在客户端插入或发送指令，跳转按钮打开链接；这两类按钮不按回调按钮处理。

接口字段和权限要求以 QQ 官方文档为准：[Markdown 消息](https://bot.q.qq.com/wiki/develop/api-v2/server-inter/message/type/markdown.html)、[消息按钮](https://bot.q.qq.com/wiki/develop/api-v2/server-inter/message/trans/msg-btn.html)、[互动事件](https://bot.q.qq.com/wiki/develop/api-v2/autogen/event/interaction_create.html)、[互动事件响应](https://bot.q.qq.com/wiki/develop/api-v2/autogen/api/interactions_interaction_id.put.html)。
