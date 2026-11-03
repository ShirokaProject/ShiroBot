# QQ C# 接口参考

本页按插件调用方式列出 QQ Model 扩展接口的 C# 签名、参数、默认值与返回类型。
无需构造 HTTP 请求。插件通过 `Context.GetAdapterExtension<T>()` 获取当前实例的服务。

版本要求与兼容规则见 [ABI 兼容性与版本](./api-compatibility)。
所有用户、群、消息、申请 ID 均使用字符串。数值字符串与 OpenID 仍属于各自的来源实例。

## QQ 原生接口

适用于适配器暴露的 QQ 好友、群管理、文件与原生消息能力。

| 分类 | C# 接口 | 文档 |
| --- | --- | --- |
| 好友 | `IQFriendApi` | [好友](./qq/friend) |
| 群管理 | `IQGroupApi` | [群管理](./qq/group) |
| 入群自动审批策略 | `IQGroupApprovalStrategyApi` | [入群自动审批策略](./qq/approval) |
| 文件 | `IQFileApi` | [文件](./qq/file) |
| 账号与资料 | `IQSystemApi` | [账号与资料](./qq/system) |
| 原生消息 | `IQMessageApi` | [原生消息](./qq/message) |

## QQ 官方机器人

官方开放平台的消息、媒体、私聊、流式会话，以及 Markdown 与按钮示例，集中在 [QQ 官方机器人](./qq/official)。

## 模型与契约

- [完整 C# 类型：实体、消息、消息段、事件、按钮和策略](./qq/types)
- [QQ 契约语义](./qq-interface-review)

## 适配器支持

群管理通过 `IQGroupApi.Capabilities` 检查具体操作。其他扩展先检查服务是否为 null；
拿到服务后，未实现的方法仍可能抛出 `NotSupportedException`。
官方平台的入群自动审批策略使用独立的 `IQGroupApprovalStrategyApi`；平台权限由 QQ 管理。
