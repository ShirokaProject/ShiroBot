using System.Globalization;
using System.Net.Http.Headers;
using System.Text.Json;
using Avalonia.Controls;
using Avalonia.Media;
using ShiroBot.AvaloniaDemoPlugin.ViewModels;

namespace ShiroBot.AvaloniaDemoPlugin.Service;

internal sealed class GitHubRepositoryClient
{
    private static readonly HttpClient Http = CreateHttpClient();
    private static readonly HttpClient ImageHttp = CreateImageHttpClient();

    public async Task<DescriptionCardViewModel> GetRepositoryCardAsync(
        string owner,
        string repository,
        CancellationToken ct = default)
    {
        using var response = await Http.GetAsync($"repos/{owner}/{repository}", ct).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();

        await using var stream = await response.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);
        using var document = await JsonDocument.ParseAsync(stream, cancellationToken: ct).ConfigureAwait(false);
        var root = document.RootElement;

        var contributors = await GetContributorCountAsync(owner, repository, ct).ConfigureAwait(false);
        var avatarBytes = await GetAvatarBytesAsync(GetString(root, "owner", "avatar_url"), ct).ConfigureAwait(false);
        var languages = await GetLanguagesOrDefaultAsync(owner, repository, ct).ConfigureAwait(false);

        return new DescriptionCardViewModel
        {
            Owner = GetString(root, "owner", "login") ?? owner,
            Repository = GetString(root, "name") ?? repository,
            Description = GetString(root, "description") ?? "No description provided.",
            Contributors = FormatCount(contributors),
            Issues = FormatCount(GetInt(root, "open_issues_count")),
            Discussions = "-",
            Stars = FormatCount(GetInt(root, "stargazers_count")),
            Forks = FormatCount(GetInt(root, "forks_count")),
            PrimaryLanguage = languages.PrimaryLanguage,
            AvatarBytes = avatarBytes ?? (string.Equals(owner, "ShirokaProject", StringComparison.OrdinalIgnoreCase)
                && string.Equals(repository, "ShiroBot", StringComparison.OrdinalIgnoreCase)
                    ? DescriptionCardViewModel.LoadDefaultAvatarBytes()
                    : null),
            Language1Width = languages[0].Width,
            Language2Width = languages[1].Width,
            Language3Width = languages[2].Width,
            Language4Width = languages[3].Width,
            Language5Width = languages[4].Width,
            Language1Color = languages[0].Color,
            Language2Color = languages[1].Color,
            Language3Color = languages[2].Color,
            Language4Color = languages[3].Color,
            Language5Color = languages[4].Color,
        };
    }

    private static async Task<byte[]?> GetAvatarBytesAsync(string? avatarUrl, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(avatarUrl)
            || !Uri.TryCreate(avatarUrl, UriKind.Absolute, out var uri))
        {
            return null;
        }

        try
        {
            return await ImageHttp.GetByteArrayAsync(uri, ct).ConfigureAwait(false);
        }
        catch
        {
            return null;
        }
    }

    private static async Task<LanguageBar> GetLanguagesAsync(string owner, string repository, CancellationToken ct)
    {
        using var response = await Http.GetAsync($"repos/{owner}/{repository}/languages", ct).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();

        await using var stream = await response.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);
        using var document = await JsonDocument.ParseAsync(stream, cancellationToken: ct).ConfigureAwait(false);
        var languages = document.RootElement.EnumerateObject()
            .Select(property => new
            {
                property.Name,
                Bytes = property.Value.TryGetInt64(out var value) ? value : 0
            })
            .Where(language => language.Bytes > 0)
            .OrderByDescending(language => language.Bytes)
            .Take(5)
            .ToArray();

        var total = languages.Sum(language => language.Bytes);
        var result = new LanguageBarSegment[5];
        for (var i = 0; i < result.Length; i++)
        {
            if (i < languages.Length && total > 0)
            {
                result[i] = new LanguageBarSegment(
                    new GridLength(languages[i].Bytes, GridUnitType.Star),
                    GetLanguageColor(languages[i].Name));
            }
            else
            {
                result[i] = new LanguageBarSegment(new GridLength(0, GridUnitType.Star), Colors.Transparent);
            }
        }

        var primaryLanguage = languages.Length > 0 && total > 0
            ? languages[0].Name
            : "Unknown";

        return new LanguageBar(result, primaryLanguage);
    }

    private static async Task<LanguageBar> GetLanguagesOrDefaultAsync(
        string owner,
        string repository,
        CancellationToken ct)
    {
        try
        {
            return await GetLanguagesAsync(owner, repository, ct).ConfigureAwait(false);
        }
        catch
        {
            return new LanguageBar([
                new LanguageBarSegment(new GridLength(1, GridUnitType.Star), Color.Parse("#8C959F")),
                new LanguageBarSegment(new GridLength(0, GridUnitType.Star), Colors.Transparent),
                new LanguageBarSegment(new GridLength(0, GridUnitType.Star), Colors.Transparent),
                new LanguageBarSegment(new GridLength(0, GridUnitType.Star), Colors.Transparent),
                new LanguageBarSegment(new GridLength(0, GridUnitType.Star), Colors.Transparent)
            ], "Unknown");
        }
    }

    private static async Task<int> GetContributorCountAsync(string owner, string repository, CancellationToken ct)
    {
        using var response = await Http.GetAsync($"repos/{owner}/{repository}/contributors?per_page=1&anon=true", ct)
            .ConfigureAwait(false);

        response.EnsureSuccessStatusCode();

        if (response.Headers.TryGetValues("Link", out var values))
        {
            var count = TryReadLastPage(values);
            if (count is not null)
            {
                return count.Value;
            }
        }

        await using var stream = await response.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);
        using var document = await JsonDocument.ParseAsync(stream, cancellationToken: ct).ConfigureAwait(false);
        return document.RootElement.ValueKind == JsonValueKind.Array
            ? document.RootElement.GetArrayLength()
            : 0;
    }

    private static int? TryReadLastPage(IEnumerable<string> links)
    {
        foreach (var part in string.Join(',', links).Split(','))
        {
            if (!part.Contains("rel=\"last\"", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var pageIndex = part.IndexOf("page=", StringComparison.OrdinalIgnoreCase);
            if (pageIndex < 0)
            {
                continue;
            }

            pageIndex += "page=".Length;
            var endIndex = pageIndex;
            while (endIndex < part.Length && char.IsDigit(part[endIndex]))
            {
                endIndex++;
            }

            if (int.TryParse(part[pageIndex..endIndex], NumberStyles.None, CultureInfo.InvariantCulture, out var page))
            {
                return page;
            }
        }

        return null;
    }

    private static string? GetString(JsonElement root, string propertyName)
    {
        return root.TryGetProperty(propertyName, out var property) && property.ValueKind == JsonValueKind.String
            ? property.GetString()
            : null;
    }

    private static string? GetString(JsonElement root, string objectName, string propertyName)
    {
        return root.TryGetProperty(objectName, out var obj)
               && obj.ValueKind == JsonValueKind.Object
               && obj.TryGetProperty(propertyName, out var property)
               && property.ValueKind == JsonValueKind.String
            ? property.GetString()
            : null;
    }

    private static int GetInt(JsonElement root, string propertyName)
    {
        return root.TryGetProperty(propertyName, out var property) && property.TryGetInt32(out var value)
            ? value
            : 0;
    }

    private static string FormatCount(int count)
    {
        return count switch
        {
            >= 1_000_000 => (count / 1_000_000d).ToString("0.#M", CultureInfo.InvariantCulture),
            >= 1_000 => (count / 1_000d).ToString("0.#k", CultureInfo.InvariantCulture),
            _ => count.ToString(CultureInfo.InvariantCulture)
        };
    }

    private static Color GetLanguageColor(string language)
    {
        return language switch
        {
            "C#" => Color.Parse("#178600"),
            "F#" => Color.Parse("#b845fc"),
            "Visual Basic .NET" => Color.Parse("#945db7"),
            "JavaScript" => Color.Parse("#f1e05a"),
            "TypeScript" => Color.Parse("#3178c6"),
            "HTML" => Color.Parse("#e34c26"),
            "CSS" => Color.Parse("#663399"),
            "Python" => Color.Parse("#3572A5"),
            "Java" => Color.Parse("#b07219"),
            "Kotlin" => Color.Parse("#A97BFF"),
            "C" => Color.Parse("#555555"),
            "C++" => Color.Parse("#f34b7d"),
            "Rust" => Color.Parse("#dea584"),
            "Go" => Color.Parse("#00ADD8"),
            "Swift" => Color.Parse("#F05138"),
            "Shell" => Color.Parse("#89e051"),
            "Dockerfile" => Color.Parse("#384d54"),
            "PowerShell" => Color.Parse("#012456"),
            "Vue" => Color.Parse("#41b883"),
            "Svelte" => Color.Parse("#ff3e00"),
            "Dart" => Color.Parse("#00B4AB"),
            "Ruby" => Color.Parse("#701516"),
            "PHP" => Color.Parse("#4F5D95"),
            "Lua" => Color.Parse("#000080"),
            "Jupyter Notebook" => Color.Parse("#DA5B0B"),
            _ => Color.Parse("#8C959F")
        };
    }

    private static HttpClient CreateHttpClient()
    {
        var http = new HttpClient
        {
            BaseAddress = new Uri("https://api.github.com/")
        };

        http.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("ShiroBot-AvaloniaDemoPlugin", "1.0"));
        http.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
        http.DefaultRequestHeaders.Add("X-GitHub-Api-Version", "2022-11-28");
        return http;
    }

    private static HttpClient CreateImageHttpClient()
    {
        var http = new HttpClient();
        http.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("ShiroBot-AvaloniaDemoPlugin", "1.0"));
        http.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("image/*"));
        return http;
    }

    private sealed record LanguageBar(LanguageBarSegment[] Segments, string PrimaryLanguage)
    {
        public LanguageBarSegment this[int index] => Segments[index];
    }

    private sealed record LanguageBarSegment(GridLength Width, Color Color);
}
