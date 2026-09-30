# ShiroBot.Model.QQ

Protocol-neutral QQ contracts for ShiroBot plugins and adapters.

QQ contracts are distributed by the `ShiroBot.SDK` package and built into the official host. A
component that uses these contracts should declare the runtime package requirement:

```csharp
[assembly: RequiresShiroBotPackage("shirobot.model.qq", MinimumVersion = "0.9.3")]
```

Public contracts follow the API evolution rules documented by ShiroBot API 0.9: existing
constructors and members remain stable, enum values are fixed, and new data is added through
optional properties or capability interfaces.

## QQ official Markdown and buttons

The QQ official Open Platform adapter can expose `IQOfficialMessageApi`. Plugins should
probe the extension and the target before sending. Direct and group target IDs are
Open Platform openids, not numeric QQ account or group IDs. A Milky adapter need not
implement this interface.

`QOfficialMessage` offers one send entry point for plain text, Markdown with an optional
keyboard, and media with an optional text caption. The adapter selects the official message
type. Local media is uploaded while sending; the caller owns the stream and should keep it
open until the returned task completes.

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

## QQ official direct messages

An adapter can expose `IQOfficialDirectMessageApi` for C2C typing indicators and
streamed replies. Plugins request it through `context.GetAdapterExtension<IQOfficialDirectMessageApi>()`
and handle a missing capability. `BeginStream` returns `IQOfficialMessageStream`;
`AppendAsync` receives the complete text visible at that point, and `CompleteAsync`
finishes the response. These contracts belong to the host model; the adapter owns
the OpenAPI transport and protocol details.

Ark template messages and Embed cards are available through `IQOfficialMessageApi.SendArkAsync`
and `SendEmbedAsync`. The QQPlatform adapter routes rich messages to C2C, group, text-channel,
and channel-DM endpoints. A channel-DM target's `Id` is the official `guild_id`.

## QQ official media upload

An adapter that supports local media can expose `IQOfficialMediaApi`. Plugins pass a readable stream and a file name; the adapter handles chunking and the platform upload protocol.

```csharp
var mediaApi = context.GetAdapterExtension<IQOfficialMediaApi>();
var official = context.GetAdapterExtension<IQOfficialMessageApi>();
if (mediaApi is not null && official is not null)
{
    await using var image = File.OpenRead(imagePath);
    var messageId = await official.SendAsync(
        new QOfficialMessageTarget(QOfficialMessageScene.Direct, userOpenId),
        QOfficialMessage.Media(QOfficialMediaType.Image, image, Path.GetFileName(imagePath),
            "图片说明第一行\n图片说明第二行"));
}
```

`UploadAsync` returns a `QOfficialMedia` reference that can be wrapped with `QOfficialMessage.Media` and reused in a later reply. `IQOfficialMediaApi` also exposes lower-level upload methods. The QQPlatform adapter supports C2C and group uploads, with a 200 MB maximum file size.

`IQOfficialMessageApi` also includes `SendTextAsync` and `SendArkAsync`. Adapters compiled against an older contract may rely on default interface implementations that throw `NotSupportedException`; plugins should handle that exception. Direct-message typing and streamed replies require the adapter to implement `IQOfficialDirectMessageApi` separately.
