using System.Text;
using System.Text.RegularExpressions;
using WebTestToolkit.Contracts.Models;
using WebTestToolkit.Llm.Skills;

namespace WebTestToolkit.Execution.Generation;

// Turns one LLM-suggested edge case into a real, independently-generatable TestFlow. This is
// deliberately plain C#, not another model call: the LLM only ever proposes which values and
// expected outcomes change (never a real captured value, see EdgeCaseGenerationModels.cs) —
// every step's locator, page, and label is copied verbatim from the original flow, so an
// edge case can never invent an element the way free-form codegen could.
//
// Each edge case gets its own PageName per step (original name + suffix), not the original
// flow's PageName. Reusing the same PageName would make a later /api/flows/generate call for
// the edge case silently overwrite the original flow's PageObjects/<Page>.cs the next time it
// runs (GeneratedProjectWriter writes by relative path, last write wins) — giving every edge
// case its own page namespace sidesteps that entirely, at the cost of a small amount of
// duplicated locator JSON.
public static class EdgeCaseFlowBuilder
{
    public static TestFlow Build(TestFlow original, EdgeCaseSuggestion suggestion)
    {
        var suffix = SanitizeSuffix(suggestion.NameSuffix);
        var overridesByOrder = suggestion.Overrides.ToDictionary(o => o.StepOrder);

        var flow = new TestFlow
        {
            Name = SanitizeSuffix(original.Name) + suffix,
            StartUrl = original.StartUrl
        };

        foreach (var step in original.Steps.OrderBy(s => s.Order))
        {
            var clone = new TestStep
            {
                Order = step.Order,
                ActionType = step.ActionType,
                Label = step.Label,
                PageName = step.PageName + suffix,
                LocatorKey = step.LocatorKey,
                Element = step.Element,
                InputValue = step.InputValue,
                ExpectedText = step.ExpectedText
            };

            if (overridesByOrder.TryGetValue(step.Order, out var over))
            {
                if (over.NewInputValue is not null)
                    clone.InputValue = over.NewInputValue;
                if (over.NewExpectedText is not null)
                    clone.ExpectedText = over.NewExpectedText;
            }

            flow.Steps.Add(clone);
        }

        return flow;
    }

    // A flow/page name has to be a safe fragment to append to an identifier, and a suffix
    // colliding with the original flow's own name would produce an unreadable duplicate
    // (e.g. "LoginLogin") - PascalCase-join alphanumeric words only, same shape as
    // CodeGenerator.Naming.ToPascalCaseIdentifier (kept local: that helper is internal to
    // the CodeGenerator assembly, and this one small routine isn't worth exposing it for).
    private static string SanitizeSuffix(string text)
    {
        var words = Regex.Matches(text, "[A-Za-z0-9]+").Select(m => m.Value);
        var sb = new StringBuilder();
        foreach (var word in words)
        {
            sb.Append(char.ToUpperInvariant(word[0]));
            if (word.Length > 1)
                sb.Append(word[1..]);
        }
        return sb.ToString();
    }
}
