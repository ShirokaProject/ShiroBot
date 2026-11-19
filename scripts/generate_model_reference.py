#!/usr/bin/env python3
"""Generates the Telegram and Discord Model reference pages from the C# sources.

Interface members, parameters and types come from the declarations and their XML doc comments, so the pages
stay in step with the code. Run from the repository root after changing a Model:

    python3 scripts/generate_model_reference.py
"""

import re
from pathlib import Path

ROOT = Path(__file__).resolve().parent.parent
DOCS = ROOT / "docs" / "plugin"

COMMON_PARAMS = {
    "cancellationToken": "取消令牌；默认不主动取消。",
    "chatId": "会话：Bot API 格式的数字 ID、`@username` 或 SDK 频道 ID（`chatId:threadId`）。",
    "userId": "用户 ID，与 SDK 的 `User.Id` 相同。",
    "messageId": "消息 ID，与 SDK 的 `MessageId` 相同。",
    "messageIds": "消息 ID 列表。",
    "channel": "目标 SDK 频道；私聊为 `Channel.Direct(用户 ID)`。",
    "target": "目标 SDK 频道。",
    "source": "来源 SDK 频道。",
    "threadId": "话题 ID（SDK 频道 ID `chatId:threadId` 中的 threadId）。",
    "reason": "写入服务器审计日志的原因；null 不写。",
    "guildId": "服务器 ID。",
    "channelId": "频道 ID。",
    "threadId_discord": "子区 ID。",
    "roleId": "角色 ID。",
    "roleOrUserId": "角色或成员 ID，由 isRole 区分。",
    "isRole": "true 表示角色覆盖，false 表示成员覆盖。",
    "interactionId": "交互 ID，即 SDK `InteractionEvent.InteractionId`。",
    "content": "要发送的内容。",
    "options": "可选设置；null 使用默认值。",
    "languageCode": "两位 ISO 639-1 语言代码；null 表示所有未单独设置的语言。",
    "scope": "命令生效范围；null 为默认范围。",
    "permissions": "权限设置。",
    "rights": "管理员权限；全部为 false 时撤销管理员。",
    "until": "结束时间；null 表示永久或解除。",
    "title": "标题。",
    "name": "名称；null 表示不修改。",
    "description": "介绍文字；null 或空字符串清除。",
    "link": "邀请链接。",
    "queryId": "查询 ID，来自对应事件负载。",
    "results": "结果列表。",
    "ok": "true 表示确认，false 表示拒绝。",
    "errorMessage": "拒绝时显示给用户的原因。",
    "answer": "回答内容。",
    "action": "要显示的动作。",
    "text": "文本内容。",
    "caption": "说明文字；null 清除。",
    "parseMode": "正文格式。",
    "replyMarkup": "内联键盘；null 移除。",
    "inlineMessageId": "inline 消息 ID，来自 `TelegramChosenInlineResult.InlineMessageId`。",
    "disableNotification": "静默发送，不推送通知。",
    "imageUri": "图片地址：HTTP(S) 或适配器支持的本地资源 URI。",
    "iconColor": "RGB 图标颜色；null 使用默认值。",
    "iconCustomEmojiId": "自定义表情图标 ID；null 不设置。",
    "revokeMessages": "同时删除该成员的消息。",
    "onlyIfBanned": "只在已封禁时解封；false 时会移出仍在群内的成员。",
    "method": "方法名或 HTTP 方法。",
    "parameters": "snake_case 参数。",
    "files": "随请求上传的文件；提供时使用 multipart。",
    "path": "REST 路由，如 `guilds/123/audit-logs`。",
    "body": "请求 JSON；有文件时作为 payload_json。",
    "op": "Gateway opcode。",
    "data": "Gateway 指令的 d 字段。",
    "commands": "命令列表。",
    "command": "命令定义。",
    "commandId": "已注册命令的 ID。",
    "limit": "返回数量上限。",
    "query": "搜索文本。",
    "nickname": "昵称；null 清除。",
    "emoji": "表情：Unicode 字符或自定义表情的 name:id。",
    "answerId": "投票选项 ID。",
    "deleteMessages": "同时删除该成员在此时长内的消息，最长 7 天。",
    "code": "邀请码。",
    "inviteCode": "邀请码。",
    "inviteLinkOrUsername": "邀请链接（`t.me/+…`）或公开用户名。",
    "maxMessageId": "读到的最后一条消息 ID；null 为最新。",
    "callbackData": "按钮的回调数据。",
    "presence": "在线状态与活动。",
    "username": "新用户名；null 不修改。",
    "avatarUri": "新头像地址；null 不修改。",
    "modal": "弹窗定义。",
    "choices": "自动补全选项，最多 25 个。",
    "ephemeral": "只对发起者可见。",
    "updateMessage": "组件交互时更新组件所在的消息，而不是发送新消息。",
    "autoArchive": "自动归档时间：1 小时、1 天、3 天或 7 天。",
    "isPrivate": "创建私密子区。",
    "allow": "允许的权限。",
    "deny": "拒绝的权限。",
    "withCounts": "同时返回近似成员数和在线数。",
    "telegramPaymentChargeId": "成功付款服务消息中的 `TelegramPaymentChargeId`。",
}

PLATFORMS = [
    {
        "key": "telegram", "title": "Telegram", "namespace": "ShiroBot.Model.Telegram", "dir": "Models/Telegram", "abi": "1.1.0.0",
        "interfaces": "ITelegramExtensions.cs",
        "groups": [
            ("common", "接口参考", "两种账号都实现。", [
                ("ITelegramClientApi", "client", "账号信息"),
                ("ITelegramChatApi", "chat", "会话管理"),
                ("ITelegramMessageApi", "message", "原生消息"),
            ]),
            ("bot", "机器人接口", "仅机器人账号（BotFather 创建的机器人）实现；用户账号适配器上为 null。", [
                ("ITelegramInlineApi", "inline", "inline 模式"),
                ("ITelegramPaymentApi", "payment", "支付"),
                ("ITelegramBotProfileApi", "bot-profile", "命令菜单与资料"),
                ("ITelegramBotApi", "bot-api", "Bot API 原始调用"),
            ]),
            ("user", "用户账号接口", "仅用户账号（MTProto 登录）实现；机器人适配器上为 null。当前为预留契约，等待用户账号适配器实现。", [
                ("ITelegramAccountApi", "account", "账号操作"),
            ]),
        ],
        "types": [
            ("TelegramIdentity.cs", "账号、传输与会话 ID"),
            ("TelegramEntities.cs", "实体与枚举"),
            ("TelegramMessages.cs", "消息内容、键盘与入站内容"),
            ("TelegramEvents.cs", "事件与事件 Kind"),
            ("ITelegramExtensions.cs", "接口参数类型"),
        ],
    },
    {
        "key": "discord", "title": "Discord", "namespace": "ShiroBot.Model.Discord", "dir": "Models/Discord", "abi": "1.1.0.0",
        "interfaces": "IDiscordExtensions.cs",
        "groups": [
            ("common", "接口参考", "两种账号都实现。", [
                ("IDiscordClientApi", "client", "账号信息"),
                ("IDiscordGuildApi", "guild", "服务器管理"),
                ("IDiscordMessageApi", "message", "原生消息"),
                ("IDiscordRawApi", "raw", "原始调用"),
            ]),
            ("bot", "机器人接口", "仅机器人账号（Developer Portal 创建的机器人）实现；用户账号适配器上为 null。", [
                ("IDiscordInteractionApi", "interaction", "交互与应用命令"),
            ]),
            ("user", "用户账号接口", "仅用户账号实现；机器人适配器上为 null。当前为预留契约。"
                                   "Discord 服务条款禁止自动化用户账号（self-bot），账号可能被封禁，是否使用由部署者自行承担。", [
                ("IDiscordAccountApi", "account", "账号操作"),
            ]),
        ],
        "types": [
            ("DiscordEntities.cs", "实体、权限与枚举"),
            ("DiscordMessages.cs", "消息内容、组件与入站内容"),
            ("DiscordInteractions.cs", "交互、应用命令与弹窗"),
            ("DiscordEvents.cs", "事件与事件 Kind"),
            ("IDiscordExtensions.cs", "账号与接口参数类型"),
        ],
    },
]


def closing(text, index, open_char="{", close_char="}"):
    depth = 0
    for position in range(index, len(text)):
        if text[position] == open_char:
            depth += 1
        elif text[position] == close_char:
            depth -= 1
            if depth == 0:
                return position
    raise ValueError(index)


def split_params(text):
    result, start, depth = [], 0, 0
    for index, char in enumerate(text):
        if char in "<([":
            depth += 1
        elif char in ">)]":
            depth -= 1
        elif char == "," and depth == 0:
            result.append(text[start:index].strip())
            start = index + 1
    if text[start:].strip():
        result.append(text[start:].strip())
    return result


def doc_to_markdown(xml):
    xml = re.sub(r'<see cref="(?:[A-Z]:)?([^"]+)"\s*/>', lambda m: "`" + m.group(1).split("(")[0] + "`", xml)
    xml = re.sub(r'<(?:paramref|typeparamref) name="([^"]+)"\s*/>', r"`\1`", xml)
    xml = re.sub(r"<c>(.*?)</c>", r"`\1`", xml)
    xml = re.sub(r"<br\s*/>", " ", xml)
    xml = re.sub(r"<[^>]+>", "", xml)
    return " ".join(xml.replace("&lt;", "<").replace("&gt;", ">").replace("&amp;", "&").split())


def parse_doc(lines):
    text = " ".join(lines)
    summary = re.search(r"<summary>(.*?)</summary>", text, re.S)
    params = {m.group(1): doc_to_markdown(m.group(2)) for m in re.finditer(r'<param name="([^"]+)">(.*?)</param>', text, re.S)}
    return doc_to_markdown(summary.group(1)) if summary else "", params


def interface_members(source, interface):
    match = re.search(r"((?:^[ \t]*///.*\n)*)public interface " + interface + r"\b[^{]*\{", source, re.M)
    if match is None:
        raise SystemExit(f"interface {interface} not found")
    header, _ = parse_doc([line.strip()[3:] for line in match.group(1).splitlines() if line.strip()])
    body = source[match.end():closing(source, match.end() - 1)]
    members, position, comments = [], 0, []
    while position < len(body):
        while position < len(body) and body[position].isspace():
            position += 1
        if position >= len(body):
            break
        if body.startswith("///", position):
            end = body.find("\n", position)
            comments.append(body[position + 3:end].strip())
            position = end + 1
            continue
        end, depth = position, 0
        while end < len(body):
            char = body[end]
            if char == "(":
                depth += 1
            elif char == ")":
                depth -= 1
            elif depth == 0 and (char in "{;" or body.startswith("=>", end)):
                break
            end += 1
        signature = " ".join(body[position:end].split())
        default = None
        if body.startswith("=>", end):
            stop = body.find(";", end)
            default = " ".join(body[end + 2:stop].split())
            position = stop + 1
        elif end < len(body) and body[end] == "{":
            position = closing(body, end) + 1
        else:
            position = end + 1
        summary, params = parse_doc(comments)
        comments = []
        if signature:
            members.append((signature, summary, params, default))
    return header, members


def member_section(interface, signature, summary, params, default, platform_key):
    lines = []
    if signature.startswith("event "):
        name = signature.split()[-1]
        lines += [f"## {name}", "", summary or "事件。", "", "```csharp", signature + ";", "```", ""]
        return lines
    if "(" not in signature:
        name = signature.split()[-1]
        lines += [f"## {name}", "", summary or "属性。", "", "```csharp", signature + " { get; }", "```", ""]
        if default is not None:
            lines += [f"接口默认值：`{default}`；适配器按实际情况返回。", ""]
        return lines
    method = re.search(r"\b(\w+)\(", signature)
    name = method.group(1)
    returns = signature[:method.start()].strip()
    arguments = signature[method.end():signature.rfind(")")]
    code = signature + ";" if len(signature) <= 100 else returns + " " + name + "(\n    " + ",\n    ".join(split_params(arguments)) + "\n);"
    lines += [f"## {name}", "", summary or f"调用 `{interface}.{name}`。", "", "```csharp", code, "```", ""]
    if arguments.strip():
        lines += ["### 参数", "", "| 参数 | C# 类型 | 默认值 | 说明 |", "| --- | --- | --- | --- |"]
        for parameter in split_params(arguments):
            declaration, _, value = parameter.partition(" = ")
            parameter_type, parameter_name = declaration.rsplit(" ", 1)
            explanation = params.get(parameter_name)
            if explanation is None and platform_key == "discord" and parameter_name == "threadId":
                explanation = COMMON_PARAMS["threadId_discord"]
            if explanation is None and platform_key == "discord" and parameter_name == "channel":
                explanation = "目标 SDK 频道；私聊为 `Channel.Direct(用户 ID)`，适配器自动打开私聊。"
            explanation = explanation or COMMON_PARAMS.get(parameter_name, "")
            lines.append(f"| `{parameter_name}` | `{parameter_type}` | `{value or '必传'}` | {explanation} |")
        lines.append("")
    if returns in ("Task", "ValueTask"):
        result = "完成后无返回值。"
    elif returns.startswith(("Task<", "ValueTask<")):
        result = "异步返回 `" + returns[returns.index("<") + 1:-1] + "`。"
    else:
        result = "同步返回结果。"
    lines += ["### 返回", "", f"`{returns}`。{result}", ""]
    return lines


def type_declarations(source):
    pattern = re.compile(r"((?:^[ \t]*///.*\n|^\[.*\]\n)*)^public (?:(?:abstract|sealed|static|readonly) )*(?:record(?: struct)?|enum|class|struct) (\w+)", re.M)
    for match in pattern.finditer(source):
        start = match.start() + len(match.group(1))
        index, depth = match.end(), 0
        while index < len(source):
            char = source[index]
            if char == "(":
                depth += 1
            elif char == ")":
                depth -= 1
            elif depth == 0 and char in "{;":
                break
            index += 1
        end = closing(source, index) + 1 if source[index] == "{" else index + 1
        attributes = "".join(line + "\n" for line in match.group(1).splitlines() if line.startswith("["))
        summary, _ = parse_doc([line.strip()[3:] for line in match.group(1).splitlines() if line.strip().startswith("///")])
        yield match.group(2), summary, attributes + source[start:end]


def generate(platform):
    key, title, namespace, abi = platform["key"], platform["title"], platform["namespace"], platform["abi"]
    model_dir = ROOT / platform["dir"]
    out = DOCS / key
    out.mkdir(exist_ok=True)
    source = (model_dir / platform["interfaces"]).read_text(encoding="utf-8")
    pages, methods = 0, 0
    group_pages = {}
    for group_key, group_title, availability, interfaces in platform["groups"]:
        rows = []
        for interface, slug, label in interfaces:
            header, members = interface_members(source, interface)
            page = [f"# {label}（{interface}）", "",
                    f"命名空间：`{namespace}`。{title} Model ABI：`{abi}`。{availability}", ""]
            if header:
                page += [header, ""]
            page += ["```csharp", f"var api = Context.GetAdapterExtension<{interface}>();", "if (api is null) return;", "```", "",
                     "取得接口不代表每个方法都已实现；未实现的方法抛出 `NotSupportedException`，调用还受平台权限和参数约束。", ""]
            for signature, summary, params, default in members:
                page += member_section(interface, signature, summary, params, default, key)
                methods += 1
            page += ["## 相关类型", "", "见 [C# 类型参考](./types)。`required` 属性创建对象时必须填写；`?` 表示可空。", ""]
            (out / f"{slug}.md").write_text("\n".join(page), encoding="utf-8")
            rows.append((label, interface, slug))
            pages += 1
        group_pages[group_key] = (group_title, availability, rows)

    # 分组入口页。
    for group_key in ("bot", "user"):
        group_title, availability, rows = group_pages[group_key]
        page = [f"# {title} {group_title}", "", availability, "",
                "插件通过 `Context.GetAdapterExtension<T>()` 探测；用 `ClientApi.AccountKind` 也可以先判断当前实例的账号类型。", "",
                "| 能力 | C# 接口 | 文档 |", "| --- | --- | --- |",
                *[f"| {label} | `{interface}` | [{label}](./{slug}) |" for label, interface, slug in rows], ""]
        (out / f"{group_key}.md").write_text("\n".join(page).replace("ClientApi", "I" + title + "ClientApi"), encoding="utf-8")

    _, common_availability, common_rows = group_pages["common"]
    index = [f"# {title} C# 接口参考", "",
             f"本页按插件调用方式列出 {title} Model 扩展接口的 C# 签名、参数、默认值与返回类型。",
             "插件通过 `Context.GetAdapterExtension<T>()` 获取当前实例的服务，不需要引用适配器程序集。", "",
             f"机器人适配器和用户账号适配器共用这套 Model，平台 ID 都是 `{key}`；各自只实现自己账号能用的接口，"
             "其他接口返回 `null`。版本要求与兼容规则见 [ABI 兼容性与版本](./api-compatibility)。", "",
             "## 通用接口", "", common_availability, "", "| 能力 | C# 接口 | 文档 |", "| --- | --- | --- |",
             *[f"| {label} | `{interface}` | [{label}](./{key}/{slug}) |" for label, interface, slug in common_rows], "",
             "## 按账号类型", "",
             f"- [机器人接口](./{key}/bot)：{group_pages['bot'][1]}",
             f"- [用户账号接口](./{key}/user)：{group_pages['user'][1]}", "",
             "## 模型与示例", "",
             f"- [完整 C# 类型](./{key}/types)",
             f"- [{title} 特有能力与示例](./{key}-model)", ""]
    (DOCS / f"{key}-reference.md").write_text("\n".join(index), encoding="utf-8")

    page = [f"# {title} C# 类型参考", "", f"命名空间：`{namespace}`。以下完整声明对应 {title} Model ABI `{abi}`。", "",
            "`required` 属性必须初始化；`init` 属性在创建对象时设置；位置式 record 使用所列构造参数。继承类型同时拥有基类属性。", ""]
    seen = set()
    for file_name, label in platform["types"]:
        declarations = [d for d in type_declarations((model_dir / file_name).read_text(encoding="utf-8")) if d[0] not in seen]
        if not declarations:
            continue
        page += [f"## {label}", ""]
        for name, summary, code in declarations:
            seen.add(name)
            page += [f"### {name}", ""]
            if summary:
                page += [summary, ""]
            page += ["```csharp", code, "```", ""]
    (out / "types.md").write_text("\n".join(page), encoding="utf-8")
    print(f"{title}: {pages} interface pages, {methods} members, {len(seen)} types.")


for item in PLATFORMS:
    generate(item)
