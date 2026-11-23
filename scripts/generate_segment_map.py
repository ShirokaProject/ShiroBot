#!/usr/bin/env python3
"""Generates the SDK message segment comparison page and links platform Model types back to it.

docs/plugin/segments.md lists, for every SDK segment, the matching QQ, Telegram and Discord Model types and
how each adapter handles the segment. Each listed type then gets an "对应主消息段" line under its heading in
the platform's C# type reference, linking back. The script is idempotent; the Model reference generators call
it at the end, so regenerating a type page keeps the links. It can also be run on its own:

    python3 scripts/generate_segment_map.py
"""

from pathlib import Path

ROOT = Path(__file__).resolve().parent.parent
DOCS = ROOT / "docs" / "plugin"
PLATFORMS = [("qq", "QQ"), ("telegram", "Telegram"), ("discord", "Discord")]
MARKER = "对应主消息段："

# 每个主消息段：(SDK 类型, 说明, {平台: ([(Model 类型, 说明)], 适配器处理)})。
# Model 类型为 None 表示没有专门的类型，只写说明。
SEGMENTS = [
    ("TextSegment", "纯文本。", {
        "qq": ([("QIncomingText", "入站"), ("QOutgoingText", "出站")], "收发"),
        "telegram": ([("TelegramTextContent", "原生发送"), ("TelegramFormattedText", "入站格式实体，随文本段一起以 `RawSegment` 上报")], "收发"),
        "discord": ([("DiscordMessageContent", "`Content` 字段")], "收发"),
    }),
    ("MarkdownSegment", "Markdown 正文，各平台语法不同。", {
        "qq": ([("QIncomingMarkdown", "入站"), ("QOfficialMarkdown", "官方机器人 Markdown，经 `IQOfficialMessageApi` 发送"),
                ("QCustomMarkdown", "官方机器人自定义 Markdown")], "取决于 QQ 适配器；官方机器人走 `IQOfficialMessageApi`"),
        "telegram": ([("TelegramFormattedText", "入站格式实体"), ("TelegramTextContent", "`ParseMode` 指定出站格式")], "原生 MarkdownV2，语法遵循 Telegram"),
        "discord": ([("DiscordMessageContent", "`Content` 直接使用 Discord Markdown")], "原生 Discord Markdown"),
    }),
    ("MentionSegment", "提及用户。", {
        "qq": ([("QIncomingMention", "入站"), ("QOutgoingMention", "出站")], "收发"),
        "telegram": ([(None, "没有专门类型")], "数字 ID 发成 `tg://user?id` 链接，`@username` 入站解析为用户 ID；是否通知受平台规则限制"),
        "discord": ([("DiscordAllowedMentions", "控制哪些提及会通知")], "原生 `<@id>`"),
    }),
    ("MentionAllSegment", "提及全体成员。", {
        "qq": ([("QIncomingMentionAll", "入站"), ("QOutgoingMentionAll", "出站")], "收发"),
        "telegram": ([(None, "Telegram 没有全员提及")], "不支持"),
        "discord": ([("DiscordAllowedMentions", "`Everyone` 控制是否通知")], "入站 @everyone / @here；出站 @everyone"),
    }),
    ("QuoteSegment", "引用（回复）一条消息。", {
        "qq": ([("QIncomingReply", "入站，可带被引用内容"), ("QOutgoingReply", "出站")], "收发"),
        "telegram": ([("TelegramSendOptions", "`ReplyToMessageId`")], "`reply_parameters` 引用回复"),
        "discord": ([("DiscordMessageContent", "`ReplyToMessageId`")], "引用回复"),
    }),
    ("EmojiSegment", "平台表情 ID，可带降级文本。", {
        "qq": ([("QIncomingFace", "入站 QQ 表情"), ("QOutgoingFace", "出站 QQ 表情")], "收发"),
        "telegram": ([("TelegramStickerContent", "贴纸")], "Unicode 表情或贴纸 file_id；`tgfile://` 贴纸优先原生发送"),
        "discord": ([("DiscordEmoji", "服务器自定义表情"), ("DiscordSticker", "贴纸，另以 `RawSegment` 上报")], "Unicode 或自定义表情标记"),
    }),
    ("ImageSegment", "图片。", {
        "qq": ([("QIncomingImage", "入站"), ("QOutgoingImage", "出站")], "收发"),
        "telegram": ([("TelegramMediaItem", "`Kind = Photo`，用于相册")], "入站映射，出站真实媒体上传"),
        "discord": ([("DiscordAttachment", "附件")], "入站映射，出站真实附件上传"),
    }),
    ("AudioSegment", "音频与语音。", {
        "qq": ([("QIncomingRecord", "入站语音"), ("QOutgoingRecord", "出站语音")], "收发"),
        "telegram": ([("TelegramMediaItem", "`Kind = Audio`，用于相册")], "入站映射，出站真实媒体上传"),
        "discord": ([("DiscordAttachment", "附件")], "入站映射，出站真实附件上传"),
    }),
    ("VideoSegment", "视频。", {
        "qq": ([("QIncomingVideo", "入站"), ("QOutgoingVideo", "出站")], "收发"),
        "telegram": ([("TelegramMediaItem", "`Kind = Video`，用于相册")], "入站映射，出站真实媒体上传"),
        "discord": ([("DiscordAttachment", "附件")], "入站映射，出站真实附件上传"),
    }),
    ("FileSegment", "文件。", {
        "qq": ([("QIncomingFile", "入站；QQ Model 没有出站文件段")], "取决于 QQ 适配器"),
        "telegram": ([("TelegramMediaItem", "`Kind = Document`，用于相册")], "入站映射，出站真实文件上传"),
        "discord": ([("DiscordAttachment", "附件")], "入站映射，出站真实附件上传"),
    }),
    ("CardSegment", "标题、说明、图片、链接和字段组成的卡片。", {
        "qq": ([(None, "没有专门类型，图文卡片直接使用 `CardSegment`")], "取决于 QQ 适配器"),
        "telegram": ([(None, "没有原生卡片")], "显式允许 `CardAsMarkdown` / `CardAsText` 时由 SDK 降级"),
        "discord": ([("DiscordEmbed", "Embed")], "原生 Embed"),
    }),
    ("MessageButton", "按钮（不是消息段，放在 `OutgoingMessage.Buttons`），点击产生 `InteractionEvent`。", {
        "qq": ([("QInlineKeyboard", "官方机器人内联键盘"), ("QOfficialKeyboard", "官方机器人键盘")], "取决于 QQ 适配器"),
        "telegram": ([("TelegramInlineKeyboard", "内联键盘"), ("TelegramInlineButton", "单个按钮")], "原生内联键盘，点击为 callback_query"),
        "discord": ([("DiscordActionRow", "按钮行"), ("DiscordButton", "单个按钮")], "原生组件，点击为交互"),
    }),
]

# RawSegment：通用模型不能表示的平台内容，按平台列出 Kind 与负载类型。
RAW = {
    "qq": ("QQ Model 的这些消息段通过 `IQMessageApi` 收发；它们在 SDK 消息里是否以 `RawSegment` 出现取决于 QQ 适配器。", [
        ("合并转发", [("QIncomingForward", "入站"), ("QOutgoingForward", "出站"), ("QForwardedMessage", "转发中的一条消息")]),
        ("商城表情", [("QIncomingMarketFace", "入站")]),
        ("小程序与卡片", [("QIncomingLightApp", "入站"), ("QOutgoingLightApp", "出站")]),
        ("XML 卡片", [("QIncomingXml", "入站")]),
    ]),
    "telegram": ("`Platform` 为 `telegram`，`Kind` 使用 `TelegramSegmentKinds`；出站使用 `ITelegramMessageApi.SendAsync` 和对应的内容类型。", [
        ("`location`", [("TelegramLocation", "入站负载"), ("TelegramLocationContent", "出站")]),
        ("`venue`", [("TelegramVenue", "入站负载"), ("TelegramVenueContent", "出站")]),
        ("`contact`", [("TelegramContact", "入站负载"), ("TelegramContactContent", "出站")]),
        ("`dice`", [("TelegramDice", "入站负载"), ("TelegramDiceContent", "出站")]),
        ("`poll`", [("TelegramPoll", "入站负载"), ("TelegramPollContent", "出站")]),
        ("`successful_payment`", [("TelegramSuccessfulPayment", "入站负载"), ("TelegramInvoiceContent", "出站发票")]),
        ("`formatted_text`", [("TelegramFormattedText", "入站负载，与文本段一同出现")]),
    ]),
    "discord": ("`Platform` 为 `discord`，`Kind` 使用 `DiscordSegmentKinds`；Embed 和贴纸也能以 `RawSegment` 直接发送。", [
        ("`embed`", [("DiscordEmbed", "负载；发送时同样接受")]),
        ("`sticker`", [("DiscordSticker", "负载")]),
        ("`poll`", [("DiscordPoll", "负载")]),
        ("`components`", [("DiscordComponents", "负载，原始 JSON")]),
    ]),
}


def anchor(name):
    return name.lower()


def type_link(platform, name, prefix="./"):
    return f"[`{name}`]({prefix}{platform}/types#{anchor(name)})"


def describe(platform, entries):
    parts = []
    for name, note in entries:
        parts.append(note if name is None else f"{type_link(platform, name)}：{note}")
    return "<br>".join(parts)


def build_page():
    page = ["# 消息段对照", "",
            "插件收发消息只用 SDK 的消息段，换平台不用改代码。本页列出每个 SDK 主消息段在 QQ、Telegram、Discord Model 里",
            "对应的类型，以及适配器如何处理它；点击类型跳到该平台的 C# 类型参考，那里的“对应主消息段”会链接回本页。", "",
            "Model 类型只在插件主动调用平台扩展接口（如 `IQMessageApi`、`ITelegramMessageApi`）时用到；",
            "平台独有、SDK 表达不了的内容见 [RawSegment](#rawsegment)。通用消息段的字段见 [通用 Model](./models#消息段)。", "",
            "| 主消息段 | 说明 |", "| --- | --- |"]
    page += [f"| [`{name}`](#{anchor(name)}) | {summary} |" for name, summary, _ in SEGMENTS]
    page += ["| [`RawSegment`](#rawsegment) | 平台独有内容，含 `Platform`、`Kind`、`Payload` |", ""]
    for name, summary, platforms in SEGMENTS:
        page += [f"## {name}", "", summary, "", "| 平台 | Model 类型 | 适配器处理 |", "| --- | --- | --- |"]
        for key, title in PLATFORMS:
            entries, handling = platforms[key]
            page.append(f"| {title} | {describe(key, entries)} | {handling} |")
        page.append("")
    page += ["## RawSegment", "",
             "通用模型不能表示的平台原生内容。插件先检查 `Platform` 和 `Kind`，再把 `Payload` 转成对应类型。", ""]
    for key, title in PLATFORMS:
        intro, rows = RAW[key]
        page += [f"### {title}", "", intro, "", "| 内容 | Model 类型 |", "| --- | --- |"]
        page += [f"| {kind} | {describe(key, entries)} |" for kind, entries in rows]
        page.append("")
    (DOCS / "segments.md").write_text("\n".join(page), encoding="utf-8")


def backlinks():
    links = {key: {} for key, _ in PLATFORMS}
    for name, _, platforms in SEGMENTS:
        for key, (entries, _) in platforms.items():
            for type_name, _ in entries:
                if type_name:
                    links[key].setdefault(type_name, []).append((name, anchor(name)))
    for key, (_, rows) in RAW.items():
        for _, entries in rows:
            for type_name, _ in entries:
                links[key].setdefault(type_name, []).append(("RawSegment", "rawsegment"))
    return links


def apply_backlinks():
    for key, title in PLATFORMS:
        path = DOCS / key / "types.md"
        lines = []
        for line in path.read_text(encoding="utf-8").split("\n"):
            if line.startswith(MARKER):
                if lines and lines[-1] == "":
                    lines.pop()  # 去掉上次插入的空行，保持幂等。
                continue
            lines.append(line)
        headings = {line[4:].strip(): index for index, line in enumerate(lines) if line.startswith("### ")}
        targets = backlinks()[key]
        missing = sorted(set(targets) - set(headings))
        if missing:
            raise SystemExit(f"{title} types.md has no heading for: {', '.join(missing)}")
        for type_name, index in sorted(((t, headings[t]) for t in targets), key=lambda item: -item[1]):
            unique = list(dict.fromkeys(targets[type_name]))
            text = MARKER + "、".join(f"[`{segment}`](../segments#{slug})" for segment, slug in unique)
            # 标题后插入：### 名称 / 空行 / 对应主消息段 / 空行。
            lines[index + 1:index + 1] = ["", text]
        path.write_text("\n".join(lines), encoding="utf-8")
        print(f"{title}: {len(targets)} types linked to their main segments.")


build_page()
apply_backlinks()
