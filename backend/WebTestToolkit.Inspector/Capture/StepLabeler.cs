using System.Text;
using System.Text.RegularExpressions;
using WebTestToolkit.Contracts.Models;

namespace WebTestToolkit.Inspector.Capture;

// Names things: which page a step belongs to, what to call its locator, and what the
// Gherkin sentence should say.
//
// This is the deterministic path. LLM skill #2 (P8) proposes nicer wording on top of it,
// but the toolkit has to produce a runnable flow with no API key configured, so everything
// here works standalone and is what the user edits in the UI.
//
// Stateful by design: locator keys must be unique within a page ("UsernameInput" twice on
// one page object would not compile), so an instance tracks what it has already handed out
// for the session it belongs to.
public sealed class StepLabeler
{
    private readonly Dictionary<string, HashSet<string>> _keysByPage = new(StringComparer.Ordinal);

    // Path segments that name a record rather than a page: /orders/48213/edit should be
    // "OrdersEditPage", not "48213Page".
    private static readonly Regex OpaqueSegment = new(
        @"^(\d+|[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}|[0-9a-f]{16,})$",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    public static string PageNameFromUrl(string url)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri))
            return "HomePage";

        var segments = uri.AbsolutePath
            .Split('/', StringSplitOptions.RemoveEmptyEntries)
            .Select(Uri.UnescapeDataString)
            .Select(s => Path.GetFileNameWithoutExtension(s))
            .Where(s => s.Length > 0 && !OpaqueSegment.IsMatch(s))
            .ToList();

        if (segments.Count == 0)
            return "HomePage";

        // Last two segments, so /admin/users reads as "AdminUsersPage" and doesn't collide
        // with /reports/users.
        var name = string.Concat(segments.TakeLast(2).Select(ToPascalCase));
        if (name.Length == 0)
            return "HomePage";

        return name.EndsWith("Page", StringComparison.Ordinal) ? name : name + "Page";
    }

    public string LocatorKeyFor(string pageName, CapturedElement element)
    {
        var baseName = ToPascalCase(DescriptiveName(element));
        if (baseName.Length == 0)
            baseName = ToPascalCase(element.TagName);

        var suffix = SuffixFor(element);
        var candidate = baseName.EndsWith(suffix, StringComparison.Ordinal) ? baseName : baseName + suffix;

        var used = _keysByPage.TryGetValue(pageName, out var existing)
            ? existing
            : _keysByPage[pageName] = new HashSet<string>(StringComparer.Ordinal);

        // Two "Submit" buttons on one page is entirely normal; the user renames them in the UI.
        var unique = candidate;
        for (var n = 2; !used.Add(unique); n++)
            unique = candidate + n;

        return unique;
    }

    public static string NavigateLabel(string pageName) =>
        $"I open the {Humanize(TrimPageSuffix(pageName))} page";

    public static string ActionLabel(ActionType actionType, CapturedElement? element, string? inputValue)
    {
        if (element is null)
            return actionType == ActionType.Navigate ? "I open the page" : "I perform an action";

        var subject = Humanize(ToPascalCase(DescriptiveName(element)));
        if (subject.Length == 0)
            subject = element.TagName;

        var type = (element.Type ?? "").ToLowerInvariant();

        return actionType switch
        {
            // Never echo the value: a password would end up in the step text as well as the
            // test data, and the generated binding already carries it as a parameter.
            ActionType.Type when type == "password" => "I enter the password",
            ActionType.Type => $"I enter the {subject}",
            ActionType.Select => $"I choose the {subject}",
            ActionType.Click when type is "checkbox" => $"I tick the {subject}",
            ActionType.Click when type is "radio" => $"I select the {subject}",
            ActionType.Click when element.TagName.Equals("a", StringComparison.OrdinalIgnoreCase) => $"I click the {subject} link",
            ActionType.Click when IsButton(element) => $"I click the {subject} button",
            ActionType.Click => $"I click the {subject}",
            ActionType.AssertText => $"I should see the {subject}",
            ActionType.AssertVisible => $"I should see the {subject}",
            _ => $"I interact with the {subject}"
        };
    }

    // ---------------------------------------------------------------- internals

    private static bool IsButton(CapturedElement element)
    {
        var type = (element.Type ?? "").ToLowerInvariant();
        return element.TagName.Equals("button", StringComparison.OrdinalIgnoreCase)
            || type is "submit" or "button" or "reset";
    }

    private static string SuffixFor(CapturedElement element)
    {
        var tag = element.TagName.ToLowerInvariant();
        var type = (element.Type ?? "").ToLowerInvariant();

        if (tag == "a") return "Link";
        if (IsButton(element)) return "Button";
        if (tag == "select") return "Dropdown";
        if (tag == "textarea") return "Input";
        if (tag == "input")
        {
            return type switch
            {
                "checkbox" => "Checkbox",
                "radio" => "Radio",
                _ => "Input"
            };
        }

        return "Element";
    }

    // Ordered by how well each source describes the element to a human reading the test.
    // A visible label beats an internal `name` attribute, which beats a raw id.
    private static string DescriptiveName(CapturedElement element)
    {
        var candidates = new[]
        {
            element.AssociatedLabelText,
            element.AriaLabel,
            element.Name,
            element.Placeholder,
            IsButton(element) || element.TagName.Equals("a", StringComparison.OrdinalIgnoreCase) ? element.VisibleText : null,
            element.Id,
            element.VisibleText
        };

        foreach (var candidate in candidates)
        {
            var cleaned = Clean(candidate);
            if (cleaned.Length > 0)
                return cleaned;
        }

        return "";
    }

    private static string Clean(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return "";

        // Trailing colons and required-field asterisks come from label markup, not meaning.
        var trimmed = text.Trim().TrimEnd(':', '*', ' ');
        // Long text is a paragraph, not a name.
        return trimmed.Length > 40 ? trimmed[..40] : trimmed;
    }

    private static string TrimPageSuffix(string pageName) =>
        pageName.EndsWith("Page", StringComparison.Ordinal) && pageName.Length > 4
            ? pageName[..^4]
            : pageName;

    // "UserName" / "user_name" / "user-name" -> "UserName"
    private static string ToPascalCase(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return "";

        var builder = new StringBuilder();
        foreach (Match match in Regex.Matches(text, "[A-Za-z0-9]+"))
        {
            var word = match.Value;
            builder.Append(char.ToUpperInvariant(word[0]));
            // Preserve existing internal casing ("userName" -> "UserName"), but normalise
            // shouty input ("USERNAME" -> "Username") so it reads as a name, not an acronym.
            builder.Append(word.Length > 1
                ? (word.All(char.IsUpper) ? word[1..].ToLowerInvariant() : word[1..])
                : "");
        }

        var identifier = builder.ToString();
        if (identifier.Length == 0)
            return "";

        return char.IsDigit(identifier[0]) ? "_" + identifier : identifier;
    }

    // "UserNameInput" -> "user name input" — Gherkin steps read as sentences, not identifiers.
    private static string Humanize(string pascalCase)
    {
        if (string.IsNullOrEmpty(pascalCase))
            return "";

        var spaced = Regex.Replace(pascalCase, "(?<=[a-z0-9])(?=[A-Z])", " ");
        return spaced.ToLowerInvariant().Trim();
    }
}
