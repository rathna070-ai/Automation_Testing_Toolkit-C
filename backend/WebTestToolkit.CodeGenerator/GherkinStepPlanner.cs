using System.Text.RegularExpressions;
using WebTestToolkit.Contracts.Models;

namespace WebTestToolkit.CodeGenerator;

// One planned Gherkin step, derived from a captured TestStep: which page it belongs to,
// what keyword it displays under in the .feature file, the regex Reqnroll binds it to,
// and the method names generated for both the page object and the step binding.
public record StepPlan(
    TestStep Step,
    string PageName,
    string LocatorKey,
    string SectionKeyword,
    string DisplayKeyword,
    string GherkinLine,
    string BindingRegexPattern,
    string PageObjectMethodName,
    string BindingMethodName);

public static class GherkinStepPlanner
{
    public static List<StepPlan> Plan(TestFlow flow)
    {
        var plans = new List<StepPlan>();
        string? currentSection = null;

        // Two different elements can legitimately produce the exact same label text (e.g.
        // two differently-priced-looking items that both happen to show "$29.99", captured
        // with nothing more specific to name them by). Left alone, that makes two Gherkin
        // lines byte-identical — an ambiguous binding at runtime, invisible to the compiler
        // — and their derived method names byte-identical too, which *is* a compile error.
        // Disambiguate the same way StepLabeler.LocatorKeyFor already disambiguates locator
        // keys: append a running count to the label itself, so every derived name and the
        // Gherkin line/pattern all stay in agreement and unique together.
        var labelOccurrences = new Dictionary<(string Section, string Label), int>();

        foreach (var step in flow.Steps.OrderBy(s => s.Order))
        {
            var previousSection = currentSection;
            var targetSection = DetermineSection(step, previousSection);
            var displayKeyword = targetSection == previousSection ? "And" : targetSection;
            currentSection = targetSection;

            var pageName = string.IsNullOrWhiteSpace(step.PageName)
                ? Naming.ToPascalCaseIdentifier(flow.Name) + "Page"
                : step.PageName;
            var locatorKey = string.IsNullOrWhiteSpace(step.LocatorKey)
                ? Naming.ToPascalCaseIdentifier(step.Label)
                : step.LocatorKey;

            var dedupeKey = (targetSection, step.Label);
            var occurrence = labelOccurrences[dedupeKey] = labelOccurrences.GetValueOrDefault(dedupeKey) + 1;
            var effectiveLabel = occurrence == 1 ? step.Label : $"{step.Label} ({occurrence})";

            // Select carries its chosen option the same way Type carries its typed text, so
            // both bind with a "(.*)" capture group rather than baking the value into the
            // step text — otherwise re-recording with a different option would need a new
            // binding rather than a new Examples row.
            var isParameterizedType =
                step.ActionType is (ActionType.Type or ActionType.Select) && step.InputValue is not null;

            var gherkinLine = isParameterizedType
                ? $"{effectiveLabel} \"{step.InputValue}\""
                : effectiveLabel;

            var bindingRegexPattern = isParameterizedType
                ? $"{Regex.Escape(effectiveLabel)} \"(.*)\""
                : Regex.Escape(effectiveLabel);

            var pageObjectMethodName = step.ActionType == ActionType.Navigate
                ? "NavigateTo"
                : Naming.ToPascalCaseIdentifier(effectiveLabel);

            var bindingMethodName = targetSection + Naming.ToPascalCaseIdentifier(effectiveLabel);

            plans.Add(new StepPlan(step, pageName, locatorKey, targetSection, displayKeyword,
                gherkinLine, bindingRegexPattern, pageObjectMethodName, bindingMethodName));
        }

        return plans;
    }

    // Assertions always land under Then. The first non-assertion step is Given; every
    // non-assertion step after that is When (or stays in whatever section came before it).
    private static string DetermineSection(TestStep step, string? previousSection)
    {
        if (step.ActionType is ActionType.AssertText or ActionType.AssertVisible)
            return "Then";

        if (previousSection is null)
            return "Given";

        return previousSection == "Given" ? "When" : previousSection;
    }
}
