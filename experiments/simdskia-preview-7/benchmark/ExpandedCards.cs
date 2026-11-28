using System.Reflection;
using Avalonia.Controls;
using Avalonia.Media.Imaging;
using MyParser.Provider.BiliBili.Views;
using MyParser.Provider.Douyin.Views;
using MyParser.Provider.NetEaseCloudMusic.Views;
using MyParser.Provider.WeixinChannels.Views;
using MyParser.Provider.X.Views;
using ShiroBot.AvaloniaSdk;
using ShiroBot.JmParser.Views;
using ShiroBot.Plugin.Github.Views;
using ShiroBot.Plugin.GithubView.Views;
using Shirobot.Plugin.RssSubscriber.Presentation;
using Rss = Shirobot.Plugin.RssSubscriber.Presentation.Cards;

internal static class ExpandedCards
{
    public static PreviewCardViewModel AddCases(
        List<(string Name, Func<ControlRenderOptions, Task<byte[]>> Render)> cases,
        IAvaloniaRenderContext renderer, Bitmap cover, Bitmap avatar, string output)
    {
        const string title = "周末观测记录 · 山间晚霞与星空 😀";
        const string description = "沿着山谷向上走，看看天边的颜色变化。";
        const string author = "山海观测站";
        const string date = "2026-11-28 18:30";
        const string count = "1.2 万";

        void Add<T>(string name, object model) where T : Control, new() =>
            cases.Add((name, options => renderer.RenderControlPngAsync<T>(model, options)));

        using var avatarPng = new MemoryStream();
        avatar.Save(avatarPng, PngBitmapEncoderOptions.Default);
        Add<DescriptionCard>("github-description", new DescriptionCardViewModel
        {
            Description = description, AvatarBytes = avatarPng.ToArray(), Stars = "128", Forks = "12"
        });

        var coverPath = Path.Combine(output, "fixture-cover.png");
        using (var stream = File.Create(coverPath))
            cover.Save(stream, PngBitmapEncoderOptions.Default);
        var preview = CreatePreview(coverPath, title);
        File.Delete(coverPath);
        Add<PreviewCard>("jm-preview", preview);

        var myParser = typeof(BiliCard).Assembly;
        var mediaType = myParser.GetType("Shirobot.Plugin.MyParser.Views.MediaCard", throwOnError: true)!;
        var mediaModelType = myParser.GetType("Shirobot.Plugin.MyParser.Views.MediaCardViewModel", throwOnError: true)!;
        var mediaModel = Activator.CreateInstance(mediaModelType, nonPublic: true)!;
        foreach (var (property, value) in new (string, object)[]
                 {
                     ("Cover", cover), ("Title", title), ("Author", author), ("Description", description),
                     ("ProviderName", "ShiroBot"), ("KindName", "视频"), ("Metadata", date)
                 })
            mediaModelType.GetProperty(property)!.SetValue(mediaModel, value);
        var renderMedia = typeof(IAvaloniaRenderContext)
            .GetMethod(nameof(IAvaloniaRenderContext.RenderControlPngAsync))!.MakeGenericMethod(mediaType);
        cases.Add(("media", options =>
            (Task<byte[]>)renderMedia.Invoke(renderer, [mediaModel, options, CancellationToken.None])!));

        Add<BiliArticleDocument>("bili-article", new BiliArticleDocumentViewModel
        {
            Avatar = avatar, Title = title, AuthorName = author, MetaText = date, StatsText = count,
            CanvasHeight = 856,
            Blocks =
            [
                new() { Text = "路线与观测记录", IsHeading = true, FontSize = 24, FontWeight = "Bold", Foreground = "#899FB7" },
                new() { Text = description, Foreground = "#899FB7" },
                new() { IsImage = true, Image = cover, Height = 255, Caption = "图 1 · 山间晚霞" }
            ]
        });
        var bili = new BiliCardViewModel
        {
            Cover = cover, Avatar = avatar, Title = title, AuthorName = author,
            Description = description, Tags = ["观测", "晚霞"], HasMultiPages = true
        };
        Add<BiliCard>("bili-video", bili);
        Add<BiliBangumiCard>("bili-bangumi", bili);

        Add<DouyinCard>("douyin-video", new DouyinCardViewModel
        {
            Cover = cover, Avatar = avatar, Title = title, AuthorName = author, Description = description,
            DurationText = "02:34", PublishTimeText = date, HasPublishTime = true, LikeCount = count,
            CollectCount = "386", CommentCount = "42", ShareCount = "12", MusicText = "山间晚霞",
            TagsText = "观测 晚霞"
        });
        Add<DouyinCommentCard>("douyin-comments", new DouyinCommentCardViewModel
        {
            CanvasHeight = 760, SourceTitle = title, CommentCountText = "42 评论",
            Comments =
            [
                new()
                {
                    Avatar = avatar, UserName = author, Message = description, LikeText = count,
                    TimeLocationText = date, HasReplies = true, ReplyCountText = "12 回复", IsAuthor = true
                },
                new()
                {
                    Avatar = avatar, UserName = "观测台", Message = "图 1 · 山间晚霞",
                    CommentImage = cover, HasImage = true, EstimatedHeight = 280, TimeLocationText = date
                }
            ]
        });
        Add<NetEaseMusicCard>("netease-music", new NetEaseMusicCardViewModel
        {
            Cover = cover, Avatar = avatar, Title = "山间晚霞", Artists = author, Album = "观测记录",
            QualityText = "无损", SizeText = "28 MB", BitrateText = "1000 kbps", SongIdText = "123456"
        });
        Add<WeixinChannelsCard>("weixin-video", new WeixinChannelsCardViewModel
        {
            Cover = cover, Avatar = avatar, Title = title, AuthorName = author, Description = description,
            DurationText = "02:34", PublishText = date, LikeCount = count, CommentCount = "42",
            FavoriteCount = "386", ForwardCount = "12"
        });
        Add<XCard>("x-post", new XCardViewModel
        {
            Cover = cover, Avatar = avatar, DisplayName = author, ScreenName = "@MountainDesk",
            Description = description, KindText = "图文", PublishTime = date, HasCover = true,
            HasAvatar = true, Verified = true, ReplyCount = "42", RetweetCount = "12",
            LikeCount = "386", ViewCount = count
        });

        var rss = new CardViewModel
        {
            Cover = cover, Avatar = avatar, Title = title, Description = description, SourceName = "RSS",
            SitePlatformName = "Bilibili", FeedName = author, AuthorName = author, PublishedText = date,
            PublishedDateText = date, TagsText = "观测 · 晚霞", DurationText = "02:34",
            ViewCountText = count, DanmakuCountText = "42", LikeCountText = "386", CoinCountText = "28",
            FavoriteCountText = "93", ShareCountText = "12", CategoryText = "观察",
            GitEventTypeText = "Release", GitStatusText = "Published", GitRepositoryText = "ShirokaProject/ShiroBot",
            GitReferenceText = "v1.0.5"
        };
        Add<Rss.Bilibili.ArticleCard>("rss-bili-article", rss);
        Add<Rss.Bilibili.DynamicCard>("rss-bili-dynamic", rss);
        Add<Rss.Bilibili.VideoCard>("rss-bili-video", rss);
        Add<Rss.Generic.ArticleCard>("rss-article", rss);
        Add<Rss.Git.GitEventCard>("rss-git", rss);
        Add<Rss.WordPress.WordPressArticleCard>("rss-wordpress", rss);

        return preview;
    }

    private static PreviewCardViewModel CreatePreview(string coverPath, string title)
    {
        var previewType = typeof(PreviewCardViewModel);
        var resultType = previewType.Assembly.GetType("ShiroBot.JmParser.Service.PdfBuildResult", throwOnError: true)!;
        var result = Activator.CreateInstance(resultType, "", "JM123456.pdf", coverPath, title, 128, "13K", "1K")!;
        var constructor = previewType.GetConstructor(BindingFlags.Instance | BindingFlags.NonPublic,
            binder: null, [resultType, typeof(double)], modifiers: null)
            ?? throw new MissingMethodException(previewType.FullName, ".ctor(PdfBuildResult, double)");
        return (PreviewCardViewModel)constructor.Invoke([result, 12d]);
    }
}
