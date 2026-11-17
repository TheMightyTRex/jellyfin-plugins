using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Xunit;

namespace Jellyfin.Plugin.AudioDescription.Tests;

/// <summary>
/// The words people read follow the design system (CONTENT-DESIGN.md 2): no banned words on the settings page (its
/// text, the attributes people hear and the script's sentences), in the messages in the code, README.md and SYNC.md;
/// no em dashes or contractions; Active and Inactive.
/// </summary>
public class WordingTests
{
    private static readonly Regex Banned = new(
        @"\b(see|sees|seen|seeing|look|looks|looked|looking|click|clicks|enable|enabled|disable|disabled|"
        + @"turn (it )?(on|off)|turned (on|off)|switched (on|off)|toggle|typed|above|below|please|just|simply|easy|"
        + @"easily|advanced|drive|drives)\b|\btype (a|an|the|one|your|here|it in)\b|\b(on|off) by default\b",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    private static string Wording(string name) => File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "wording", name));

    private static IEnumerable<string> PageTexts()
    {
        var html = Regex.Replace(Wording("configPage.html"), "<style.*?</style>", string.Empty, RegexOptions.Singleline);
        var scripts = Regex.Matches(html, "<script[^>]*>(.*?)</script>", RegexOptions.Singleline).Select(m => m.Groups[1].Value).ToList();
        html = Regex.Replace(html, "<script.*?</script>", string.Empty, RegexOptions.Singleline);
        foreach (Match m in Regex.Matches(html, "\\b(?:aria-label|title|alt|placeholder)=\"([^\"]*)\""))
        {
            yield return m.Groups[1].Value;
        }

        foreach (var line in Regex.Replace(html, "<[^>]+>", "\n").Split('\n'))
        {
            yield return line;
        }

        foreach (var script in scripts)
        {
            var code = Regex.Replace(script, @"^\s*//.*$", string.Empty, RegexOptions.Multiline);
            foreach (Match m in Regex.Matches(code, @"'((?:[^'\\\n]|\\.)*)'"))
            {
                if (m.Groups[1].Value.Contains(' ', StringComparison.Ordinal))
                {
                    yield return m.Groups[1].Value;
                }
            }
        }
    }

    [Fact]
    public void TheSettingsPageHasNoBannedWords()
    {
        Assert.Empty(PageTexts().Where(t => Banned.IsMatch(t)).Select(t => t.Trim()));
    }

    [Theory]
    [InlineData("README.md")]
    [InlineData("SYNC.md")]
    public void TheHelpHasNoBannedWords(string name)
    {
        Assert.Empty(Wording(name).Split('\n').Select((l, i) => $"{name}:{i + 1}: {l}").Where(l => Banned.IsMatch(l)));
    }

    [Fact]
    public void TheMessagesInTheCodeHaveNoBannedWords()
    {
        var code = Path.Combine(AppContext.BaseDirectory, "wording", "code");
        var files = Directory.GetFiles(code, "*.cs", SearchOption.AllDirectories);
        Assert.NotEmpty(files);
        var hits = new List<string>();
        foreach (var file in files)
        {
            var lines = File.ReadAllLines(file);
            for (var i = 0; i < lines.Length; i++)
            {
                if (lines[i].TrimStart().StartsWith("//", StringComparison.Ordinal))
                {
                    continue;
                }

                foreach (Match m in Regex.Matches(lines[i], @"""((?:[^""\\]|\\.)*)"""))
                {
                    var text = m.Groups[1].Value;
                    if (text.Contains(' ', StringComparison.Ordinal) && Banned.IsMatch(text))
                    {
                        hits.Add($"{Path.GetFileName(file)}:{i + 1}: {text}");
                    }
                }
            }
        }

        Assert.Empty(hits);
    }

    [Theory]
    [InlineData("configPage.html")]
    [InlineData("README.md")]
    [InlineData("SYNC.md")]
    [InlineData("CHANGELOG.md")]
    public void NoEmDashesOrContractions(string name)
    {
        var text = Wording(name);
        Assert.DoesNotContain("\u2014", text, StringComparison.Ordinal);
        Assert.Empty(Regex.Matches(text, @"\b\w+n't\b|\b(?:it|that|there|you|we)'(?:s|re|ll|ve|d)\b", RegexOptions.IgnoreCase).Select(m => m.Value));
    }

    [Fact]
    public void TheWordsOfTheDesignSystemAreUsed()
    {
        var page = Wording("configPage.html");
        Assert.Contains("Active by default", page, StringComparison.Ordinal);
        Assert.Contains("Inactive until you choose a person", page, StringComparison.Ordinal);
        Assert.Contains("Not every Jellyfin app accepts this", page, StringComparison.Ordinal);
        Assert.Contains("Not every Jellyfin app accepts", Wording("README.md"), StringComparison.Ordinal);
    }
}
