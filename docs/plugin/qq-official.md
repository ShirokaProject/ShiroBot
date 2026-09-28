# QQ 官方机器人 Markdown 与按钮

`ShiroBot.Model.QQ` 提供 QQ 官方开放平台的 Markdown、消息按钮和按钮点击事件模型。它们是可选的平台能力：官方适配器实现 `IQOfficialMessageApi` 后，插件才能发送；Milky 等不实现该接口的适配器会在探测时返回 `null`。

插件和适配器应使用与宿主匹配的 `ShiroBot.Model.QQ` 程序集。发送目标的 `Id` 在单聊和群聊中是开放平台的 **openid**，不能传普通 QQ 号或群号。

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

| 模型 | 用途 |
| --- | --- |
| `QOfficialMessageTarget` | 目标场景与开放平台 ID；支持单聊、群聊、子频道和频道私信 |
| `QCustomMarkdown` / `QTemplateMarkdown` | 自定义文本或模板 Markdown |
| `QKeyboardTemplate` / `QInlineKeyboard` | 已申请的按钮模板或自定义按钮 |
| `QKeyboardAction` | 跳转、回调或指令按钮的动作与权限 |
| `QOfficialMessageReply` | 被动回复时的消息 ID、事件 ID 和回复序号 |

官方接口将 Markdown 作为 `msg_type=2` 的 `markdown` 对象，并在同一条消息中附带 `keyboard`。自定义按钮最多 5 行、每行最多 5 个。单聊与群聊的自定义 Markdown 已开放；频道 Markdown 和自定义按钮仍受开放平台权限限制。`CanSendMarkdown` 表示适配器认为当前形式可用，发送仍可能因平台权限或频控失败。

## 处理按钮点击

回调按钮（`QKeyboardActionType.Callback`）点击后，官方平台推送 `INTERACTION_CREATE`，其中 `type=11` 是消息按钮。官方适配器应将它映射成 `PlatformEvent`：

```csharp
new PlatformEvent
{
    Platform = "qq",
    Kind = QEventKinds.OfficialButtonInteraction,
    Channel = channel,
    Raw = new QOfficialButtonInteraction
    {
        InteractionId = interactionId,
        ButtonData = buttonData,
        ButtonId = buttonId,
        Target = target,
        UserId = userId
    }
};
```

插件可按 `Kind` 订阅，并读取按钮的 `action.data`：

```csharp
protected override void ConfigureRoutes()
{
    Events.MapPlatform(QEventKinds.OfficialButtonInteraction, async evt =>
    {
        if (evt.Raw is not QOfficialButtonInteraction click) return;
        await HandleButtonAsync(click.ButtonData, click.UserId);
    });
}
```

适配器需要及时调用 `IQOfficialMessageApi.AcknowledgeInteractionAsync(interactionId)` 回应按钮点击，避免客户端持续显示加载。同一个互动 ID 只能回应一次；适配器应明确由哪一层负责回应，避免插件与适配器重复调用。指令按钮会在客户端插入或发送指令，跳转按钮打开链接；这两类按钮不按回调按钮处理。

接口字段和权限要求以 QQ 官方文档为准：[Markdown 消息](https://bot.q.qq.com/wiki/develop/api-v2/server-inter/message/type/markdown.html)、[消息按钮](https://bot.q.qq.com/wiki/develop/api-v2/server-inter/message/trans/msg-btn.html)、[互动事件](https://bot.q.qq.com/wiki/develop/api-v2/autogen/event/interaction_create.html)、[互动事件响应](https://bot.q.qq.com/wiki/develop/api-v2/autogen/api/interactions_interaction_id.put.html)。
