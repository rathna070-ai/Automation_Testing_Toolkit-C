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

        foreach (var step in flow.Steps.OrderBy(s => s.Order))
        {
            var previousSection = currentSection;
            var targetSection = DetermineSection(step, previousSection);
            var displayKeyword = targetSection == previousSection ? "And" : targetSection;
            currentSection = targetSection;

            var pageName = string.IsNullOrWhiteSpace(step.PageName) ? $"{flow.Name}Page" : step.PageName;
            var locatorKey = string.IsNullOrWhiteSpace(step.LocatorKey)
                ? Naming.ToPascalCaseIdentifier(step.Label)
                : step.LocatorKey;

            var isParameterizedType = step.ActionType == ActionType.Type && step.InputValue is not null;

            var gherkinLine = isParameterizedType
                ? $"{step.Label} \"{step.InputValue}\""
                : step.Label;

            var bindingRegexPattern = isParameterizedType
                ? $"{Regex.Escape(step.Label)} \"(.*)\""
                : Regex.Escape(step.Label);

            var pageObjectMethodName = step.ActionType == ActionType.Navigate
                ? "NavigateTo"
                : Naming.ToPascalCaseIdentifier(step.Label);

            var bindingMethodName = targetSection + Naming.ToPascalCaseIdentifier(step.Label);

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
