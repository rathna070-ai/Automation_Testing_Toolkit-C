using System.Text;
using System.Text.RegularExpressions;

namespace WebTestToolkit.CodeGenerator;

internal static class Naming
{
    // "I enter username" -> "IEnterUsername"
    public static string ToPascalCaseIdentifier(string text)
    {
        var words = Regex.Matches(text, "[A-Za-z0-9]+").Select(m => m.Value);
        var sb = new StringBuilder();
        foreach (var word in words)
        {
            sb.Append(char.ToUpperInvariant(word[0]));
            if (word.Length > 1)
                sb.Append(word[1..]);
        }

        var identifier = sb.ToString();
        if (identifier.Length == 0)
            return "Step";

        return char.IsDigit(identifier[0]) ? "_" + identifier : identifier;
    }

    public static string ToCamelCase(string pascalCaseIdentifier) =>
        pascalCaseIdentifier.Length == 0
            ? pascalCaseIdentifier
            : char.ToLowerInvariant(pascalCaseIdentifier[0]) + pascalCaseIdentifier[1..];

    // For embedding a pattern inside a C# verbatim string literal (@"...").
    public static string EscapeForVerbatimString(string text) => text.Replace("\"", "\"\"");

    // For embedding text inside a regular C# string literal ("...").
    public static string EscapeForRegularString(string text) =>
        text.Replace("\\", "\\\\").Replace("\"", "\\\"");
}
