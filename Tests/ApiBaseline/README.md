# 公开 API 基线

从宿主根目录运行 `dotnet run --project Tests/ApiBaseline -c Release`，检查 SDK 与全部内置 Model 的公开签名。
有意修改契约时先评审 ABI，再使用 `-- --update` 更新基线。基线更新应和接口变更一起评审。
此验证另外要求所有 QQ 异步服务方法包含 CancellationToken。
旧组件的加载拒绝验证位于 Tests/Verification，固定 DLL 位于 Tests/AbiFixtures/frozen。
