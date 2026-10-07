using System.Text;

namespace ShiroBot.SDK.Models;

/// <summary>无网络副作用的确定性准备；能力检查与发送必须使用同一套规则。</summary>
public static class MessagePreparation
{
    public static MessageSendAssessment Assess(OutgoingMessage message, MessageCapabilities capabilities)
    {
        try
        {
            var prepared = Prepare(message, capabilities);
            return new() { IsSupported = true, Transformations = prepared.Transformations };
        }
        catch (Exception error) when (error is ArgumentException or NotSupportedException)
        {
            return new() { Issues = [error.Message] };
        }
    }

    public static PreparedMessage Prepare(OutgoingMessage message, MessageCapabilities capabilities)
    {
        ArgumentNullException.ThrowIfNull(message);
        ArgumentNullException.ThrowIfNull(capabilities);
        ArgumentNullException.ThrowIfNull(message.Segments);
        if (message.ReplyTo is not null && message.ReplyToInteraction is not null) throw new ArgumentException("Choose one reply source.");
        if ((message.AllowedFallbacks & ~(MessageFallbackOptions.MarkdownAsText | MessageFallbackOptions.LinkButtonsAsText | MessageFallbackOptions.CardAsMarkdown | MessageFallbackOptions.CardAsText)) != 0) throw new ArgumentException("Unknown fallback options.");
        var segments = new List<MessageSegment>();
        var changes = new List<MessageTransformation>();
        var native = capabilities.NativeFeatures;
        foreach (var segment in message.Segments)
        {
            ArgumentNullException.ThrowIfNull(segment);
            if (segment is CardSegment card)
            {
                ValidateCard(card);
                card = card with { Fields = card.Fields.ToArray() };
                if (!native.HasFlag(MessageFeatures.Card))
                {
                    if (message.AllowedFallbacks.HasFlag(MessageFallbackOptions.CardAsMarkdown) && native.HasFlag(MessageFeatures.Markdown))
                    {
                        segments.Add(new MarkdownSegment(RenderCard(card, true)));
                        changes.Add(new(MessageTransformationKind.CardToMarkdown, true));
                    }
                    else if (message.AllowedFallbacks.HasFlag(MessageFallbackOptions.CardAsText) && native.HasFlag(MessageFeatures.Text))
                    {
                        segments.Add(new TextSegment(RenderCard(card, false)));
                        changes.Add(new(MessageTransformationKind.CardToText, true));
                    }
                    else throw new NotSupportedException("Native cards are unavailable and the required card fallback was not allowed.");
                    continue;
                }
            }
            if (segment is MarkdownSegment markdown && !native.HasFlag(MessageFeatures.Markdown))
            {
                if (!message.AllowedFallbacks.HasFlag(MessageFallbackOptions.MarkdownAsText) || !native.HasFlag(MessageFeatures.Text))
                    throw new NotSupportedException("Native Markdown is unavailable; MarkdownAsText was not allowed.");
                segments.Add(new TextSegment(markdown.PlainTextFallback ?? markdown.Content));
                changes.Add(new(MessageTransformationKind.MarkdownToText, true));
            }
            else segments.Add(segment is CardSegment nativeCard ? nativeCard with { Fields = nativeCard.Fields.ToArray() } : segment);
        }
        if (message.ReplyTo is { } reply)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(reply.InstanceId);
            ArgumentException.ThrowIfNullOrWhiteSpace(reply.MessageId);
            if (segments.OfType<QuoteSegment>().Any()) throw new ArgumentException("Use ReplyTo or QuoteSegment, not both.");
            segments.Insert(0, new QuoteSegment(reply.MessageId));
        }
        if (message.ReplyToInteraction is { } interaction)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(interaction.InstanceId);
            ArgumentException.ThrowIfNullOrWhiteSpace(interaction.InteractionId);
            if (!native.HasFlag(MessageFeatures.InteractionReply)) throw new NotSupportedException("This target cannot reply to interactions.");
            if (segments.Any(x => x is not (TextSegment or MarkdownSegment or CardSegment))) throw new NotSupportedException("Interaction replies currently require text or Markdown.");
        }
        var buttons = SnapshotButtons(message.Buttons);
        if (buttons is not null)
        {
            var all = buttons.Rows.SelectMany(x => x.Buttons).ToArray();
            var unsupported = all.Any(x => !native.HasFlag(x.Action is OpenUrlAction ? MessageFeatures.LinkButtons : MessageFeatures.CallbackButtons));
            if (unsupported)
            {
                if (all.Any(x => x.Action is CallbackAction)) throw new NotSupportedException("Callback buttons cannot be downgraded or discarded.");
                if (!message.AllowedFallbacks.HasFlag(MessageFallbackOptions.LinkButtonsAsText)) throw new NotSupportedException("Native link buttons are unavailable; LinkButtonsAsText was not allowed.");
                var links = string.Join("\n", all.Select(x => x.Label + ": " + ((OpenUrlAction)x.Action).Url));
                if (segments.OfType<MarkdownSegment>().FirstOrDefault() is { } markdown && !capabilities.CanMixMarkdown)
                {
                    var index = segments.IndexOf(markdown);
                    segments[index] = markdown with { Content = markdown.Content + "\n\n" + string.Join("\n", all.Select(x => "[" + EscapeMarkdown(x.Label) + "](" + MarkdownUrl(((OpenUrlAction)x.Action).Url) + ")")) };
                }
                else segments.Add(new TextSegment(links));
                buttons = null;
                changes.Add(new(MessageTransformationKind.LinkButtonsToText, true));
            }
            else
            {
                if (capabilities.MaxButtonRows is { } rows && buttons.Rows.Count > rows) throw new NotSupportedException("Button row limit exceeded.");
                if (capabilities.MaxButtonsPerRow is { } columns && buttons.Rows.Any(x => x.Buttons.Count > columns)) throw new NotSupportedException("Buttons per row limit exceeded.");
                if (capabilities.MaxCallbackDataBytes is { } bytes && all.Any(x => x.Action is CallbackAction callback && Encoding.UTF8.GetByteCount(callback.Data) > bytes)) throw new NotSupportedException("Callback data limit exceeded.");
                if (segments.OfType<ResourceSegment>().Any() && !capabilities.CanCombineButtonsWithMedia) throw new NotSupportedException("Buttons cannot be combined with media on this target.");
                if (capabilities.ButtonsRequireMarkdown && !segments.OfType<MarkdownSegment>().Any())
                {
                    var body = segments.Where(x => x is not QuoteSegment).ToArray();
                    if (body.Any(x => x is not TextSegment)) throw new NotSupportedException("Buttons require a Markdown or plain text body on this target.");
                    segments.RemoveAll(x => x is TextSegment);
                    segments.Add(new MarkdownSegment(EscapeMarkdown(string.Concat(body.Cast<TextSegment>().Select(x => x.Text)))));
                    changes.Add(new(MessageTransformationKind.TextToMarkdown, false));
                }
            }
        }
        foreach (var segment in segments)
        {
            if (!native.HasFlag(FeatureOf(segment))) throw new NotSupportedException("Unsupported message segment: " + segment.GetType().Name);
            if (segment is TextSegment text && string.IsNullOrWhiteSpace(text.Text)) throw new ArgumentException("Text content must not be empty.");
            if (segment is MarkdownSegment markdown && string.IsNullOrWhiteSpace(markdown.Content)) throw new ArgumentException("Markdown content must not be empty.");
        }
        if (segments.OfType<QuoteSegment>().Count() > 1) throw new ArgumentException("Only one message quote is allowed.");
        var content = segments.Where(x => x is not QuoteSegment).ToArray();
        if (content.Length == 0) throw new ArgumentException("Message requires content.");
        if (!capabilities.CanMixMarkdown && content.Any(x => x is MarkdownSegment) && content.Length != 1) throw new NotSupportedException("Markdown must be the sole body on this target.");
        if (capabilities.MaxMediaSegments is { } media && segments.OfType<ResourceSegment>().Count() > media) throw new NotSupportedException("Media segment limit exceeded.");
        if (capabilities.MaxTextLength is { } length && segments.OfType<TextSegment>().Sum(x => x.Text.Length) > length) throw new NotSupportedException("Text length exceeds one-message limit; implicit splitting is disabled.");
        return new(message with { Segments = segments.ToArray(), Buttons = buttons, ReplyTo = null }, changes.ToArray());
    }

    private static MessageButtonLayout? SnapshotButtons(MessageButtonLayout? layout)
    {
        if (layout is null) return null;
        ArgumentNullException.ThrowIfNull(layout.Rows);
        if (layout.Rows.Count == 0) throw new ArgumentException("Button layout must contain rows.");
        var ids = new HashSet<string>(StringComparer.Ordinal);
        var rows = new List<MessageButtonRow>();
        foreach (var row in layout.Rows)
        {
            if (row?.Buttons is not { Count: > 0 }) throw new ArgumentException("Button rows must not be empty.");
            foreach (var button in row.Buttons)
            {
                ArgumentNullException.ThrowIfNull(button);
                ArgumentException.ThrowIfNullOrWhiteSpace(button.Id);
                ArgumentException.ThrowIfNullOrWhiteSpace(button.Label);
                if (!ids.Add(button.Id)) throw new ArgumentException("Duplicate button ID.");
                switch (button.Action)
                {
                    case OpenUrlAction url: ValidateUrl(url.Url); break;
                    case CallbackAction callback: ArgumentException.ThrowIfNullOrWhiteSpace(callback.Data); break;
                    default: throw new NotSupportedException("Unsupported button action.");
                }
            }
            rows.Add(new(row.Buttons.ToArray()));
        }
        return new(rows.ToArray());
    }
    private static void ValidateCard(CardSegment card)
    {
        ArgumentNullException.ThrowIfNull(card.Fields);
        if (card.Fields.Any(x => x is null || string.IsNullOrWhiteSpace(x.Name) || x.Value is null)) throw new ArgumentException("Card fields require names and values.");
        if (card.ImageUrl is not null) ValidateUrl(card.ImageUrl);
        if (card.Url is not null) ValidateUrl(card.Url);
        if (string.IsNullOrWhiteSpace(card.Title) && string.IsNullOrWhiteSpace(card.Description) && card.ImageUrl is null && card.Url is null && card.Fields.Count == 0) throw new ArgumentException("Card requires visible content.");
    }
    private static void ValidateUrl(string value)
    {
        if (!Uri.TryCreate(value, UriKind.Absolute, out var uri) || uri.Scheme is not ("http" or "https")) throw new ArgumentException("Links require an absolute HTTP(S) URL.");
    }
    public static string EscapeMarkdown(string value) => string.Concat(value.Select(c => "\\`*_{}[]()#+-.!><".Contains(c) ? "\\" + c : c.ToString()));
    private static string MarkdownUrl(string value) => new Uri(value).AbsoluteUri.Replace("(", "%28").Replace(")", "%29").Replace("<", "%3C").Replace(">", "%3E");
    private static string RenderCard(CardSegment card, bool markdown)
    {
        string Text(string text) => markdown ? EscapeMarkdown(text) : text;
        var parts = new List<string>();
        if (!string.IsNullOrEmpty(card.Title)) parts.Add(markdown ? "**" + Text(card.Title) + "**" : card.Title);
        if (!string.IsNullOrEmpty(card.Description)) parts.Add(Text(card.Description));
        foreach (var field in card.Fields) parts.Add(Text(field.Name) + ": " + Text(field.Value));
        if (card.ImageUrl is not null) parts.Add(markdown ? "![image](" + MarkdownUrl(card.ImageUrl) + ")" : card.ImageUrl);
        if (card.Url is not null) parts.Add(card.Url);
        return string.Join("\n\n", parts);
    }
    private static MessageFeatures FeatureOf(MessageSegment segment) => segment switch
    {
        TextSegment => MessageFeatures.Text, MarkdownSegment => MessageFeatures.Markdown, CardSegment => MessageFeatures.Card,
        ImageSegment => MessageFeatures.Image, AudioSegment => MessageFeatures.Audio, VideoSegment => MessageFeatures.Video,
        FileSegment => MessageFeatures.File, MentionSegment => MessageFeatures.Mention, MentionAllSegment => MessageFeatures.MentionAll,
        EmojiSegment => MessageFeatures.Emoji, QuoteSegment => MessageFeatures.Quote, RawSegment => MessageFeatures.Raw,
        _ => throw new NotSupportedException("Unknown message segment type.")
    };
}
