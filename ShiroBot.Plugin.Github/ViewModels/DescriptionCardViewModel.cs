using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;

namespace ShiroBot.AvaloniaDemoPlugin.ViewModels;

/// <summary>
/// GitHub 仓库卡片 ViewModel：纯 POCO，AXAML 通过 compiled binding 读取属性。
/// 无需 INotifyPropertyChanged，因为这是一次性渲染场景。
/// </summary>
public sealed class DescriptionCardViewModel
{
    public string Owner { get; init; } = "ShirokaProject";
    public string Repository { get; init; } = "ShiroBot";
    public string Description { get; init; } =
        "C# 实现的插件化机器人框架。";

    public string Contributors { get; init; } = "1";
    public string Issues { get; init; } = "0";
    public string Discussions { get; init; } = "-";
    public string Stars { get; init; } = "0";
    public string Forks { get; init; } = "0";
    public string PrimaryLanguage { get; init; } = "C#";
    public byte[]? AvatarBytes { get; init; } = LoadDefaultAvatarBytes();

    public GridLength Language1Width { get; init; } = new(308801, GridUnitType.Star);
    public GridLength Language2Width { get; init; } = new(4085, GridUnitType.Star);
    public GridLength Language3Width { get; init; } = new(1704, GridUnitType.Star);
    public GridLength Language4Width { get; init; } = new(0, GridUnitType.Star);
    public GridLength Language5Width { get; init; } = new(0, GridUnitType.Star);
    public Color Language1Color { get; init; } = Color.Parse("#178600");
    public Color Language2Color { get; init; } = Color.Parse("#012456");
    public Color Language3Color { get; init; } = Color.Parse("#89E051");
    public Color Language4Color { get; init; } = Colors.Transparent;
    public Color Language5Color { get; init; } = Colors.Transparent;

    public string OwnerDisplay => Owner + "/";
    public string OwnerInitial => string.IsNullOrWhiteSpace(Owner) ? "?" : Owner[..1].ToUpperInvariant();
    public Bitmap? Avatar => AvatarBytes is null ? null : new Bitmap(new MemoryStream(AvatarBytes));
    public bool HasAvatar => AvatarBytes is not null;
    public bool HasNoAvatar => AvatarBytes is null;

    public static byte[]? LoadDefaultAvatarBytes()
    {
        try
        {
            using var stream = AssetLoader.Open(new Uri("avares://ShiroBot.Plugin.Github/Assets/shiroka-project-avatar.jpg"));
            using var ms = new MemoryStream();
            stream.CopyTo(ms);
            return ms.ToArray();
        }
        catch
        {
            return null;
        }
    }
}
