# API 兼容性与版本

当前的兼容级别是 ShiroBot API `0.9.2`（宿主/SDK v0.9.7 起）：它增加了按实例选择和查询适配器实例的接口（`UseInstance`、`InstanceId`、`AdapterInstance`、`GetAdapterInstances()`），并移除了 `UsePlatform`。`0.9.1` 增加了由宿主管理的组件配置（`IConfigurableComponent`、`PluginBase<TConfig>`）。没有声明 API 元数据的旧组件按要求 `0.8` 处理，但仍需满足 SDK / Model ABI 主版本要求；本轮旧 ABI 组件必须迁移并重新编译。

组件可以在元数据中声明支持的范围：

```csharp
[assembly: ShiroBotApiCompatibility("0.9.2", "0.9.2")]

[BotPlugin("example")]
public sealed class ExamplePlugin : PluginBase;
```

适配器使用同一个独立的兼容性特性。`MinimumVersion` 是硬性要求；`MaximumVersion` 记录组件测试过的最新宿主 API。宿主 API 只做增量演进，所以较新的宿主可以加载超出该测试版本的组件。较旧的宿主遇到最低版本更新的组件时，会在创建组件加载上下文之前拒绝加载。

## Dashboard 的“声明兼容”范围

插件目录的“声明兼容”来自目录作者的 `compatibility.shirobot` 元数据。保持向后兼容的插件只需声明最低宿主版本，例如 `>=0.9.1`，不应统一添加 `<1.0.0` 上限。该字段是目录声明，不是宿主安装或加载的硬性限制。

宿主实际加载检查 DLL 声明的最低 ShiroBot API 版本和共享程序集 ABI。组件的 MaximumVersion 表示测试过的 API 上界，不是硬性拒绝上界；较新宿主可加载较旧的兼容 DLL。宿主发布版本、API 版本和 AssemblyVersion/ABI 是不同的数字，不能仅凭目录的版本字符串判断某 DLL 一定兼容未来版本。

## 演进规则

- 同一 API 版本内，不删除、不重命名、不修改已有公开成员的签名。
- 不给已有的公开接口添加抽象成员；改为提供默认实现，或新增一个能力接口。
- 公开枚举成员显式指定数值；新成员只追加在后面，不调整已有成员的顺序。
- 不修改位置记录（positional record）构造函数的参数或顺序；契约需要扩展时，添加可选的 `init` 属性。
- 不给已有的公开类型添加 `required` 属性。
- 弃用公开成员时使用 `ObsoleteAttribute`，并至少保留一次主要 API 版本过渡。
- 加载到默认 ALC 的 SDK 与内置 Model 契约需要重启宿主才能更新；组件私有的实现程序集支持正常热重载。
- 只有在有意进行兼容性过渡时才修改 `ShiroBotApi.CurrentVersion`，并同步明确更新各组件声明的范围。

共享的 SDK 与 Model 程序集版本表示组件所需的最低宿主 ABI。兼容系列内，新宿主可满足相同或更旧 ABI；依赖更新 ABI 的组件需要更新宿主。

本次 QQ Model 直接升级到 **1.0.0.0**：数值 ID 改为字符串，官方群管理并入 `IQGroupApi`，旧接口删除。
SDK 与全部内置 Model 分别要求 ABI 主版本匹配；当前 SDK 为 1.1.0.0，QQ Model 为 1.0.0.0，引用旧 SDK 或 QQ Model 的组件需重新编译。
本轮宿主与 SDK 版本为 `0.9.8`，下面表格记录各产品版本的共享程序集 ABI。具体接口见[QQ 接口审阅](/plugin/qq-interface-review)。

## 版本对照

NuGet 包版本跟随宿主发布版本。ABI 版本只在对应程序集的公开契约变化时才升高，所以一个 ABI 可以跨越多个 SDK 版本。基于某个 SDK 包构建的插件，需要宿主满足所有引用的 ABI；SDK 与全部内置 Model 还要求各自主版本匹配。

| ShiroBot.SDK（NuGet） | ShiroBot API | SDK ABI | QQ Model ABI | Discord Model ABI | Telegram Model ABI |
| --- | --- | --- | --- | --- | --- |
| 0.9.8（已发布） | 0.9.2 | 1.0.0.0 | 1.0.0.0 | 0.9.0.0 | 0.9.0.0 |
| 0.9.7 | 0.9.2 | 0.9.3.0 | 0.9.2.0 | 0.9.0.0 | 0.9.0.0 |
| 0.9.6 | 0.9.1 | 0.9.2.0 | 0.9.2.0 | 0.9.0.0 | 0.9.0.0 |
| 0.9.5 | 0.9.1 | 0.9.2.0 | 0.9.2.0 | 0.9.0.0 | 0.9.0.0 |
| 0.9.4 | 0.9.1 | 0.9.2.0 | 0.9.2.0 | 0.9.0.0 | 0.9.0.0 |
| 0.9.3 | 0.9 | 0.9.1.0 | 0.9.1.0 | 0.9.0.0 | 0.9.0.0 |
| 0.9.2 | 0.9 | 0.9.0.0 | 0.9.0.0 | 0.9.0.0 | 0.9.0.0 |
| 0.9.1 | 0.9 | 0.9.0.0 | 0.9.0.0 | 0.9.0.0 | 0.9.0.0 |

## 多适配器下的后台任务

事件处理器自动使用产生该事件的适配器实例。定时任务、Dashboard Action 等后台任务在加载了多个适配器时，必须显式选择实例（v0.9.7 起）：

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

## 开发中的通用文件服务

新增可选 `IFileService`、文件上传请求/结果及 `SentMessage.UploadedFile`，SDK ABI 为 `1.1.0.0`。此次为兼容的增量：QQ Model ABI 不变，旧 SDK 1.0 组件可继续加载；引用 SDK 1.1 的组件需要更新宿主。产品版本尚未发布，不覆盖上表的已发布记录。
