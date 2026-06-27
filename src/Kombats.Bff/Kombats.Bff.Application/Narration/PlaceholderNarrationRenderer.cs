using System.Text.RegularExpressions;

namespace Kombats.Bff.Application.Narration;

/// <summary>
/// Renders templates by replacing named {placeholder} tokens with values.
/// Missing placeholders are left as-is for graceful degradation.
/// </summary>
public sealed partial class PlaceholderNarrationRenderer : INarrationRenderer
{
    [GeneratedRegex(@"\{(\w+)\}")]
    private static partial Regex PlaceholderPattern();

    public string Render(string template, Dictionary<string, string> placeholders)
    {
        return PlaceholderPattern().Replace(template, match =>
        {
            var key = match.Groups[1].Value;
            return placeholders.TryGetValue(key, out var value) ? value : match.Value;
        });
    }
}
