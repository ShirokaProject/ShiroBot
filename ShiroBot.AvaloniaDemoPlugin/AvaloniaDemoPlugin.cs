using ShiroBot.AvaloniaDemoPlugin.ViewModels;
using ShiroBot.AvaloniaDemoPlugin.Views;
using ShiroBot.AvaloniaSdk;
using ShiroBot.Model.Common;
using ShiroBot.SDK.Abstractions;
using ShiroBot.SDK.Core;
using ShiroBot.SDK.Plugin;

namespace ShiroBot.AvaloniaDemoPlugin;

/// <summary>
/// 演示如何用独立 .axaml + UserControl 渲染图片。
/// 适合需要 IDE 智能提示、AXAML 调试预览、复杂控件树场景。
///
/// 命令：
/// - #render：好友/群聊里发起一次截图
/// </summary>
public sealed class AvaloniaDemoPlugin : PluginBase
{
    public override string Name => "AvaloniaDemoPlugin";

    public override BotComponentMetadata Metadata { get; } = new()
    {
        Name = "Avalonia 渲染示例",
        Version = "1.0.0",
        Description = "演示通过独立 .axaml UserControl + AvaloniaIntegration 渲染图片。",
        IsPluginSingleFile = false
    };

    protected override Task LoadAsync()
    {
        FriendCommands.MapExact("#render", HandleFriendRenderAsync);
        GroupCommands.MapExact("#render", HandleGroupRenderAsync);

        BotLog.Info("[AvaloniaDemoPlugin] 已加载，使用 #render 触发截图。");
        return Task.CompletedTask;
    }

    private async Task HandleFriendRenderAsync(FriendIncomingMessage message)
    {
        var segment = await RenderAsync(message.SenderId.ToString()).ConfigureAwait(false);
        if (segment is null)
        {
            await Context.Message.ReplyAsync(message, "宿主未启用 Avalonia 渲染（EnableAvalonia=false），无法渲染图片。");
            return;
        }

        await Context.Message.ReplyAsync(message, segment);
    }

    private async Task HandleGroupRenderAsync(GroupIncomingMessage message)
    {
        var segment = await RenderAsync(message.SenderId.ToString()).ConfigureAwait(false);
        if (segment is null)
        {
            await Context.Message.ReplyAsync(message, "宿主未启用 Avalonia 渲染（EnableAvalonia=false），无法渲染图片。");
            return;
        }

        await Context.Message.ReplyAsync(message, segment);
    }

    private async Task<ImageOutgoingSegment?> RenderAsync(string requestedBy)
    {
        if (Context.Render is null)
        {
            return null;
        }

        var avalonia = Context.Render.AsAvalonia();
        var vm = new ScreenshotViewModel
        {
            Title = "ShiroBot Screenshot",
            RequestedBy = requestedBy,
            Timestamp = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"),
            Footer = $"AvaloniaDemoPlugin v{Metadata.Version}"
        };

        var png = await avalonia.RenderControlPngAsync(
            () => new ScreenshotView { DataContext = vm },
            new ControlRenderOptions(540, 280)).ConfigureAwait(false);

        return new ImageOutgoingSegment("base64://" + Convert.ToBase64String(png));
    }
}
