using System.Globalization;
using System.Text;

namespace VoiceConcierge.Api;

public static class TextNormalization
{
    public static string Normalize(string? input)
    {
        if (string.IsNullOrWhiteSpace(input)) return string.Empty;
        var sb = new StringBuilder(input.Length);
        var lastWasSpace = false;
        foreach (var ch in input.Trim().ToLower(CultureInfo.InvariantCulture))
        {
            if (char.IsLetterOrDigit(ch))
            {
                sb.Append(ch);
                lastWasSpace = false;
            }
            else if (char.IsWhiteSpace(ch) || char.IsPunctuation(ch) || char.IsSymbol(ch))
            {
                if (!lastWasSpace && sb.Length > 0)
                {
                    sb.Append(' ');
                    lastWasSpace = true;
                }
            }
        }
        return sb.ToString().Trim();
    }
}

public static class TextSanitization
{
    public static string Sanitize(string? input)
    {
        if (string.IsNullOrEmpty(input)) return string.Empty;
        var sb = new StringBuilder(input.Length);
        foreach (var ch in input)
            if (!char.IsControl(ch) || ch is '\n' or '\t') sb.Append(ch);
        return sb.ToString().Trim();
    }
}
