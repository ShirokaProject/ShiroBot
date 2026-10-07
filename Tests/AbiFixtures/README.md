# 固定旧 ABI 组件样本

`frozen/LegacyFixture.dll` 仅在未调用的私有方法里引用 SDK 0.9.3.0，构造函数会写哨兵文件。
`frozen/indirect/LegacyIndirect.dll` 的 SDK 0.x 引用位于私有依赖 `LegacyHelper.dll` 内。
宿主验证必须在创建 ALC、实例化组件或执行构造函数之前拒绝这两种样本。
这些 DLL 固定用于兼容回归，不能随当前 SDK 重编译覆盖。

来源可通过 `dotnet build Tests/AbiFixtures/LegacyPlugin/LegacyPlugin.csproj -c Release`
和 `dotnet build Tests/AbiFixtures/LegacyIndirect/LegacyIndirect.csproj -c Release` 重现。
