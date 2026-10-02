# API 兼容性与版本

当前的兼容级别是 ShiroBot API `0.9.2`（宿主/SDK v0.9.7 起）：它增加了按实例选择和查询适配器实例的接口（`UseInstance`、`InstanceId`、`AdapterInstance`、`GetAdapterInstances()`），并移除了 `UsePlatform`。`0.9.1` 增加了由宿主管理的组件配置（`IConfigurableComponent`、`PluginBase<TConfig>`）。没有声明 API 元数据的旧组件按要求 `0.8` 处理，因此较新的宿主无需重新编译即可继续加载它们。

组件可以在元数据中声明支持的范围：

```csharp
[assembly: ShiroBotApiCompatibility("0.9.2", "0.9.2")]

[BotPlugin("example")]
public sealed class ExamplePlugin : PluginBase;
```

适配器使用同一个独立的兼容性特性。`MinimumVersion` 是硬性要求；`MaximumVersion` 记录组件测试过的最新宿主 API。宿主 API 只做增量演进，所以较新的宿主可以加载超出该测试版本的组件。较旧的宿主遇到最低版本更新的组件时，会在创建组件加载上下文之前拒绝加载。

## Dashboard 的“声明兼容”范围

插件目录显示的 `>=0.9.1 <1.0.0` 来自目录作者的 compatibility.shirobot 元数据，描述其声明/验证过的宿主版本范围；这不是宿主限制 1.0 以上安装的开关。

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

共享的 SDK 与 Model 程序集版本表示组件所需的最低宿主 ABI。宿主可以满足对相同或更旧 ABI 的引用，因此较新的宿主能继续加载旧插件；宿主不会满足对更新 ABI 的引用，因此依赖新增契约编译的插件会正确地要求该宿主版本或更新版本。ABI 变更必须保持增量：不能因为 `AssemblyVersion` 升高就删除或修改已有的公开契约。

## 版本对照

NuGet 包版本跟随宿主发布版本。ABI 版本只在对应程序集的公开契约变化时才升高，所以一个 ABI 可以跨越多个 SDK 版本。基于某个 SDK 包构建的插件，可以在所有被引用程序集的 ABI 都相同或更新的宿主上加载。

| ShiroBot.SDK（NuGet） | ShiroBot API | SDK ABI | QQ Model ABI | Discord Model ABI | Telegram Model ABI |
| --- | --- | --- | --- | --- | --- |
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

该作用域会跨异步调用传递，Dispose 后恢复到之前的实例。实例只能按实例 ID 选择：v0.9.7 移除了 `UsePlatform`，用 SDK v0.9.6 及更早版本构建、调用了它的插件，在新宿主上执行到该调用时会失败，需要改用 `UseInstance` 后重新构建。详见[适配器实例与后台发送](/plugin/apis#适配器实例与后台发送)。
