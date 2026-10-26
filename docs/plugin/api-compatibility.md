# ABI 兼容性与版本

ShiroBot **1.0.0 已正式发布**。当前 SDK ABI 为 `1.3.0.0`（1.0.2 新增路由描述；1.0.0、1.0.1 为 `1.2.0.0`），QQ、Discord 和 Telegram Model ABI 均为 `1.0.0.0`。新插件和适配器使用 NuGet `ShiroBot.SDK` 1.0.0。

宿主按 DLL 实际引用的 SDK / Model 程序集 ABI 判断加载兼容性：主版本必须相同，宿主提供的 ABI 版本不能低于组件引用的版本。新宿主可加载引用较旧 ABI 的组件，使用新接口的组件需要相应的新 ABI。


```csharp
[BotPlugin("example")]
public sealed class ExamplePlugin : PluginBase;
```

## 插件市场兼容要求

新版 Dashboard 展示发布 DLL 的 SDK / Model ABI 与目标框架；静态读取到元数据不代表已经运行测试。

提交市场条目时无需填写兼容要求，生成脚本自动读取最新发布 DLL 的 SDK / Model 引用版本和目标框架。ABI 不能换算成宿主产品版本范围，加载兼容性以实际 DLL 引用检查为准。

## 演进规则

- 同一 ABI 主版本内，不删除、不重命名、不修改已有公开成员的签名。
- 不给已有的公开接口添加抽象成员；改为提供默认实现，或新增一个能力接口。
- 公开枚举成员显式指定数值；新成员只追加在后面，不调整已有成员的顺序。
- 不修改位置记录（positional record）构造函数的参数或顺序；契约需要扩展时，添加可选的 `init` 属性。
- 不给已有的公开类型添加 `required` 属性。
- 弃用公开成员时使用 `ObsoleteAttribute`，并至少保留到下一个 ABI 主版本。
- 加载到默认 ALC 的 SDK 与内置 Model 契约需要重启宿主才能更新；组件私有的实现程序集支持正常热重载。

共享的 SDK 与 Model 程序集版本表示组件所需的最低宿主 ABI。兼容系列内，新宿主可满足相同或更旧 ABI；依赖更新 ABI 的组件需要更新宿主。

QQ Model **1.0.0.0** 使用当前统一契约：数值 ID 改为字符串，官方群管理并入 `IQGroupApi`，旧接口删除。
SDK 与全部内置 Model 分别要求 ABI 主版本匹配；当前 SDK 为 1.3.0.0，QQ Model 为 1.0.0.0。SDK 1.0 组件可继续加载；引用 SDK 或 QQ Model 0.x 的组件需重新编译。
当前宿主与 SDK 版本为 `1.0.0`，下面表格记录各产品版本的共享程序集 ABI。具体接口见[QQ 接口审阅](/plugin/qq-interface-review)。

## 版本对照

NuGet 包版本跟随宿主发布版本。ABI 版本只在对应程序集的公开契约变化时才升高，所以一个 ABI 可以跨越多个 SDK 版本。基于某个 SDK 包构建的插件，需要宿主满足所有引用的 ABI；SDK 与全部内置 Model 还要求各自主版本匹配。

| ShiroBot.SDK（NuGet） | SDK ABI | QQ Model ABI | Discord Model ABI | Telegram Model ABI |
| --- | --- | --- | --- | --- |
| 1.0.2 | 1.3.0.0 | 1.0.0.0 | 1.0.0.0 | 1.0.0.0 |
| 1.0.1 | 1.2.0.0 | 1.0.0.0 | 1.0.0.0 | 1.0.0.0 |
| 1.0.0 | 1.2.0.0 | 1.0.0.0 | 1.0.0.0 | 1.0.0.0 |

::: details 1.0 之前的版本记录（仅供迁移核对）

| ShiroBot.SDK（NuGet） | SDK ABI | QQ Model ABI | Discord Model ABI | Telegram Model ABI |
| --- | --- | --- | --- | --- |
| 0.9.9 | 1.1.0.0 | 1.0.0.0 | 0.9.0.0 | 0.9.0.0 |
| 0.9.8 | 1.0.0.0 | 1.0.0.0 | 0.9.0.0 | 0.9.0.0 |
| 0.9.7 | 0.9.3.0 | 0.9.2.0 | 0.9.0.0 | 0.9.0.0 |
| 0.9.6 | 0.9.2.0 | 0.9.2.0 | 0.9.0.0 | 0.9.0.0 |
| 0.9.5 | 0.9.2.0 | 0.9.2.0 | 0.9.0.0 | 0.9.0.0 |
| 0.9.4 | 0.9.2.0 | 0.9.2.0 | 0.9.0.0 | 0.9.0.0 |
| 0.9.3 | 0.9.1.0 | 0.9.1.0 | 0.9.0.0 | 0.9.0.0 |
| 0.9.2 | 0.9.0.0 | 0.9.0.0 | 0.9.0.0 | 0.9.0.0 |
| 0.9.1 | 0.9.0.0 | 0.9.0.0 | 0.9.0.0 | 0.9.0.0 |

:::

## 多适配器下的后台任务

事件处理器自动使用产生该事件的适配器实例。定时任务、Dashboard Action 等后台任务要向指定账号发送时，应显式选择实例；未选择时使用宿主默认适配器：

```csharp
using (Context.UseInstance("discord-work"))
{
    await Context.Message.SendMessageAsync(channel, segments);
}
```

该作用域跨异步调用传递，Dispose 后恢复原选择。`UsePlatform` 已删除，改用 `UseInstance`。
旧 SDK 0.x 组件会在加载前的程序集引用检查中被拒绝，不再允许等到执行时才发现缺失的方法。
宿主检查入口和可解析私有依赖的静态引用；运行后动态加载或反射产生的依赖仍受加载时 ABI 检查约束。
公开签名基线位于 `Tests/ApiBaseline`，固定旧组件 DLL 位于 `Tests/AbiFixtures/frozen`。
详见[适配器实例与后台发送](/plugin/apis#适配器实例与后台发送)。

## 通用文件服务

可选 `IFileService` 提供文件上传请求和结果，`SentMessage.UploadedFile` 表示随消息发送的上传结果。文件 ID 与消息 ID 不可混用；发布与上传的区别见[通用文件上传与发送](/plugin/apis#通用文件上传与发送)。这些接口已包含在 SDK 1.0.0 中。

## 1.0.0 临时目录服务

新增 `IBotContext.CreateTempDirectory(TimeSpan)` 与
`TemporaryDirectory(Path, ExpiresAt)`。新接口要求 SDK ABI 1.2 与宿主 1.0.0；目录保留时长、定时清理和重启清理语义见[宿主上下文](/plugin/context-config)。
QQ Model ABI 保持 1.0，Discord/Telegram Model 进入 1.0 主版本，其旧 0.x ABI 组件需要按 SDK 1.0.0 重新构建。
