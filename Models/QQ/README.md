# ShiroBot.Model.QQ

Protocol-neutral QQ contracts for ShiroBot plugins and adapters.

QQ contracts are distributed by the `ShiroBot.SDK` package and built into the official host. A
component that uses these contracts should declare the runtime package requirement:

```csharp
[assembly: RequiresShiroBotPackage("shirobot.model.qq", MinimumVersion = "0.9.2")]
```

Public contracts follow the API evolution rules documented by ShiroBot API 0.9: existing
constructors and members remain stable, enum values are fixed, and new data is added through
optional properties or capability interfaces.

## QQ official Markdown and buttons

The QQ official Open Platform adapter can expose `IQOfficialMessageApi`. Plugins should
probe the extension and the target before sending. Direct and group target IDs are
Open Platform openids, not numeric QQ account or group IDs. A Milky adapter need not
implement this interface.

```csharp
var target = new QOfficialMessageTarget(QOfficialMessageScene.Group, groupOpenId);
var markdown = new QCustomMarkdown("## 结果");
var keyboard = new QInlineKeyboard([
    new QKeyboardRow([
        new QKeyboardButton
        {
            Id = "next",
            RenderData = new QKeyboardRenderData("下一页", "下一页", QKeyboardButtonStyle.Blue),
            Action = new QKeyboardAction
            {
                Type = QKeyboardActionType.Command,
                Data = "/next",
                Permission = new QKeyboardPermission { Type = QKeyboardPermissionType.Everyone },
                UnsupportTips = "请更新 QQ 客户端",
                Enter = true
            }
        }
    ])
]);

var official = context.GetAdapterExtension<IQOfficialMessageApi>();
if (official?.CanSendMarkdown(target, markdown, keyboard) == true)
    await official.SendMarkdownAsync(target, markdown, keyboard);
else
    await context.Message.SendGroupMessageAsync(groupOpenId, "结果");
```

`QCustomMarkdown` sends custom text; `QTemplateMarkdown` sends a template ID and
parameters. `QKeyboardTemplate` uses an approved keyboard template ID, while
`QInlineKeyboard` describes custom rows and buttons. The official API allows at most
five rows and five buttons in each row. Custom Markdown is available to all bots in
QQ direct and group conversations; channel access and custom buttons depend on
Open Platform approval. Callback buttons also require the adapter to handle
`INTERACTION_CREATE` and acknowledge the interaction.

An official adapter should publish a button click as `PlatformEvent` with
`Kind = QEventKinds.OfficialButtonInteraction` and `Raw` set to
`QOfficialButtonInteraction`. A plugin can register a handler like this:

```csharp
Events.MapPlatform(QEventKinds.OfficialButtonInteraction, async evt =>
{
    if (evt.Raw is not QOfficialButtonInteraction click) return;
    await HandleButtonAsync(click.ButtonData, click.UserId);
});
```

The adapter should acknowledge the interaction promptly with
`IQOfficialMessageApi.AcknowledgeInteractionAsync(click.InteractionId)` before
dispatching slow plugin work. QQ accepts only one response for each interaction ID.
