# Avalonia 图片渲染

ShiroBot 0.7.0 在宿主默认加载上下文中统一提供 Avalonia 12.1、SkiaSharp 和 HarfBuzzSharp。插件通过独立的 `ShiroBot.AvaloniaSdk` 获取控件渲染契约，不应携带另一套运行时副本。

## 引用包

```xml
<ItemGroup>
  <PackageReference Include="ShiroBot.SDK" Version="0.7.0" />
  <PackageReference Include="ShiroBot.AvaloniaSdk" Version="0.7.0" />
</ItemGroup>
```

`ShiroBot.AvaloniaSdk` 会传递 Avalonia 编译类型和 AXAML build targets；运行时程序集由宿主共享。SDK 自动打包会移除 `ShiroBot.AvaloniaSdk`、`Avalonia*`、`SkiaSharp*`、`HarfBuzzSharp*`、`MicroCom*` 及相关 runtime/native 资产。

0.6 插件使用的程序集名、`ShiroBot.AvaloniaSdk` 命名空间和公共渲染接口保持不变。重新编译时只需把两个 ShiroBot 包更新到 0.7.0。

## 创建控件

`Views/HelloCard.axaml`：

```xml
<UserControl
    xmlns="https://github.com/avaloniaui"
    xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
    x:Class="HelloPlugin.Views.HelloCard"
    Width="720"
    Background="#17151F">
  <Border Padding="32">
    <StackPanel Spacing="12">
      <TextBlock Text="{Binding Title}"
                 FontSize="32"
                 Foreground="White" />
      <TextBlock Text="{Binding Description}"
                 FontSize="18"
                 Foreground="#D0C8DD"
                 TextWrapping="Wrap" />
    </StackPanel>
  </Border>
</UserControl>
```

`Views/HelloCard.axaml.cs`：

```csharp
using Avalonia.Controls;
using Avalonia.Markup.Xaml;

namespace HelloPlugin.Views;

public partial class HelloCard : UserControl
{
    public HelloCard()
    {
        AvaloniaXamlLoader.Load(this);
    }
}
```

## 渲染并发送

```csharp
using HelloPlugin.Views;
using ShiroBot.AvaloniaSdk;
using ShiroBot.Model.Common;

private async Task HandleCardAsync(GroupIncomingMessage message)
{
    if (Context.Render is null)
    {
        await Context.Message.ReplyAsync(
            message,
            "宿主 Avalonia 渲染初始化失败。");
        return;
    }

    var viewModel = new
    {
        Title = "ShiroBot",
        Description = "由 Avalonia Headless 渲染的插件卡片"
    };

    var png = await Context.RenderControlPngAsync<HelloCard>(viewModel);
    var image = new ImageOutgoingSegment(
        "base64://" + Convert.ToBase64String(png));

    await Context.Message.ReplyAsync(message, image);
}
```

`RenderControlPngAsync<TView>` 会在 Avalonia UI 线程创建控件并设置 `DataContext`。不要在插件工作线程直接创建或操作 Avalonia 控件。

## 主题与尺寸

宿主核心配置控制默认主题：

```toml
avalonia_theme = "Dark"
```

支持 `Light`、`Dark` 和 `Auto`。插件也可以通过渲染选项指定尺寸、DPI 或主题；没有显式指定时应保证控件本身具有可测量的宽高或内容约束。

## 常见问题

### `Context.Render` 是 `null`

Avalonia 初始化失败。插件必须提供降级提示，不能假设渲染服务一定可用。

### 出现类型无法转换

检查插件输出中是否携带了自己的 Avalonia、SkiaSharp 或 HarfBuzzSharp DLL。宿主和插件各加载一份会导致类型身份不同。部署时只复制插件主 DLL。

### AXAML 没有编译

确保项目引用 `ShiroBot.AvaloniaSdk`，并且文件 Build Action 是 `AvaloniaResource`。可以在项目中显式添加：

```xml
<ItemGroup>
  <AvaloniaResource Include="Assets\**" />
</ItemGroup>
```
