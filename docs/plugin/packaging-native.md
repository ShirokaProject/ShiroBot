# 单 DLL 与 native 依赖

`ShiroBot.SDK` NuGet 包内置 `buildTransitive` 构建目标。插件不需要在自己的项目中复制 ILRepack 配置或生成 `.deps.json`。

## 自动处理流程

当项目满足以下条件时启用：

1. 通过 `PackageReference` 引入发布后的 `ShiroBot.SDK`。
2. 输出程序集包含 `BotPluginAttribute`。
3. 项目显式设置 `<ShiroBotPluginPackagingEnabled>true</ShiroBotPluginPackagingEnabled>`。

构建过程会：

1. 收集插件的 managed NuGet 和项目依赖。
2. 排除宿主共享程序集。
3. 使用 ILRepack 合并剩余 managed DLL，并将类型 internalize。
4. 收集 NuGet 包提供的 native runtime 资产。
5. 把包 ID、精确版本、SHA512、RID 和包内路径写入嵌入资源。
6. 从插件输出中移除已合并 DLL 和延迟下载的 native 文件。
7. 删除插件 `.deps.json`。

因此部署时以插件主 DLL 为准。SDK、Model、Avalonia 等共享程序集由宿主提供，不会被合并进插件。

## managed 依赖示例

```xml
<ItemGroup>
  <PackageReference Include="ShiroBot.SDK" Version="0.9.7" />
  <PackageReference Include="SixLabors.ImageSharp" Version="3.1.11" />
</ItemGroup>
```

`SixLabors.ImageSharp.dll` 会被合并到插件 DLL。默认排除前缀包括：

```text
ShiroBot.SDK
ShiroBot.Model
Avalonia
SkiaSharp
HarfBuzzSharp
MicroCom
System.
Microsoft.
```

如果宿主额外提供共享程序集，可以扩展配置：

```xml
<PropertyGroup>
  <ShiroBotSharedAssemblyPrefixes>
    $(ShiroBotSharedAssemblyPrefixes);MyCompany.Shared
  </ShiroBotSharedAssemblyPrefixes>
</PropertyGroup>
```

## native NuGet 依赖

native 文件必须来自标准 NuGet runtime 资产，例如：

```text
runtimes/win-x64/native/example.dll
runtimes/linux-x64/native/libexample.so
runtimes/osx-arm64/native/libexample.dylib
```

插件项目只需要正常引用包：

```xml
<PackageReference Include="SQLitePCLRaw.bundle_e_sqlite3" Version="2.1.11" />
```

构建产物不会包含所有平台 native 文件。插件 DLL 内只嵌入类似以下信息：

```json
{
  "schemaVersion": 1,
  "source": "https://api.nuget.org/v3-flatcontainer",
  "packages": [
    {
      "id": "SQLitePCLRaw.lib.e_sqlite3",
      "version": "2.1.11",
      "sha512": "...",
      "assets": [
        { "rid": "win-x64", "path": "runtimes/win-x64/native/e_sqlite3.dll" },
        { "rid": "linux-x64", "path": "runtimes/linux-x64/native/libe_sqlite3.so" }
      ]
    }
  ]
}
```

运行时宿主会：

- 优先匹配完整 RID，例如 `osx-arm64`。
- 再尝试系统级 RID，例如 `osx`、`linux`、`linux-musl` 或 `win`。
- 最后尝试 `any`。
- 使用 HTTPS 下载 `.nupkg` 并校验 SHA512。
- 只释放匹配平台的文件。
- 删除临时 `.nupkg` 并缓存释放结果。

## 八个平台的兼容要求

ShiroBot 发布以下 8 个运行平台。插件若声明支持全部宿主平台，其直接和传递引用的 native NuGet 依赖也必须兼容全部 8 个 RID：

| 平台 | 架构 | RID | 标准 native 资产目录 |
| --- | --- | --- | --- |
| Windows | x64 | `win-x64` | `runtimes/win-x64/native/` |
| Windows | ARM64 | `win-arm64` | `runtimes/win-arm64/native/` |
| Linux（glibc） | x64 | `linux-x64` | `runtimes/linux-x64/native/` |
| Linux（glibc） | ARM64 | `linux-arm64` | `runtimes/linux-arm64/native/` |
| Alpine / Linux（musl） | x64 | `linux-musl-x64` | `runtimes/linux-musl-x64/native/` |
| Alpine / Linux（musl） | ARM64 | `linux-musl-arm64` | `runtimes/linux-musl-arm64/native/` |
| macOS | Intel x64 | `osx-x64` | `runtimes/osx-x64/native/` |
| macOS | Apple Silicon ARM64 | `osx-arm64` | `runtimes/osx-arm64/native/` |

自动打包和下载只负责收集、选择及加载包内已有的资产，不会重新编译原生库，也不会把 x64 库转换成 ARM64 库。单份插件 DLL 可以包含这 8 个平台的依赖清单；运行时仅下载当前平台的资产，无需为此分发 8 份插件 DLL。纯 managed 依赖也应确认没有隐藏的原生依赖或平台专用 API。

### Alpine 与普通 Linux

当前官方 Docker 镜像使用 Alpine，分别需要 `linux-musl-x64` 和 `linux-musl-arm64` 的原生资产。普通 Linux 的 `linux-x64` / `linux-arm64` 资产通常面向 glibc，不能视为兼容 musl；安装 .NET 或常用系统库也不会自动解决两种 libc 的二进制兼容问题。

宿主对 musl 的选择顺序为完整 RID → `linux-musl` → `any`，不会回退到 `linux-x64`、`linux-arm64` 或 `linux`。系统级 RID 和 `any` 仅适用于资产确实兼容对应环境及架构的情况，不能用来给不兼容的原生二进制兜底。

选用 native NuGet 包前，请核对其直接及传递依赖的资产目录、CPU 架构、libc 和系统库要求，并在目标平台验证实际调用；编译成功、下载成功或只在一个平台加载成功，都不能证明支持全部 8 个平台。额外系统库需由部署环境提供，宿主不会自动安装。

若某个依赖无法覆盖全部平台，应更换依赖、提供对应原生资产或实现可用的 managed 替代方案；仅支持部分平台的插件须在 README 和 Release 中明确列出支持的 RID，尤其说明是否支持 Alpine Docker。本文中的 SQLite 引用及清单仅演示机制，不代表所示版本已经验证覆盖全部 8 个平台。

## 限制

::: warning 手动 native 文件
直接用 `<None CopyToOutputDirectory>` 放进项目的 native DLL 没有 NuGet 包 ID、版本和 SHA512，当前不会生成可下载清单。需要把 native 文件制作成规范 NuGet 包。
:::

::: warning 首次联网
包含延迟 native 依赖的插件首次加载需要访问包源。离线部署前应在目标 RID 上预热缓存，或提供可访问的内部 NuGet flat-container 源。
:::

## 私有包源

```xml
<PropertyGroup>
  <ShiroBotNativePackageSource>
    https://packages.example.com/v3-flatcontainer
  </ShiroBotNativePackageSource>
</PropertyGroup>
```

当前地址必须是 HTTPS，并且需要提供 NuGet v3 flat-container 风格路径。需要鉴权的私有源目前不能依赖交互登录，建议通过受控反向代理提供只读下载。

## 排除宿主 native 包

Avalonia、SkiaSharp、HarfBuzzSharp 和 MicroCom 的 managed/native/runtime 资源由统一宿主提供，不会写入插件下载清单，也不会留在插件发布目录。没有使用 Avalonia API 的插件程序集不会产生 Avalonia `AssemblyRef`，无需对宿主启用 `PublishTrimmed`；宿主的反射加载路径目前明确禁止 trimming。扩展列表：

```xml
<PropertyGroup>
  <ShiroBotSharedNativePackagePrefixes>
    $(ShiroBotSharedNativePackagePrefixes);MyCompany.Native.Host
  </ShiroBotSharedNativePackagePrefixes>
</PropertyGroup>
```

## 关闭自动打包

库项目或适配器引用 SDK 时应关闭插件打包：

```xml
<PropertyGroup>
  <ShiroBotPluginPackagingEnabled>false</ShiroBotPluginPackagingEnabled>
</PropertyGroup>
```
