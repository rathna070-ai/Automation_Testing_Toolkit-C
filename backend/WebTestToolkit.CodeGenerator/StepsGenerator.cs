using System.Text;
using WebTestToolkit.Contracts.Models;

namespace WebTestToolkit.CodeGenerator;

// Reproduces the shape of the Phase 1 hand-written LoginSteps.cs: a [Binding] class that
// constructor-injects one page object per page touched by the flow, with one step method
// per captured TestStep.
public static class StepsGenerator
{
    public static string Generate(TestFlow flow, IReadOnlyList<StepPlan> plans)
    {
        // flow.Name is free text the user typed into the "Flow name" box — sanitize before
        // it becomes a class/constructor name, or a name like "flow new 1" produces invalid
        // C# ("public class flow new 1Steps") instead of a compile error anyone can act on.
        var className = Naming.ToPascalCaseIdentifier(flow.Name) + "Steps";

        var pageFields = plans
            .Select(p => p.PageName)
            .Distinct()
            .ToDictionary(pageName => pageName, pageName => "_" + Naming.ToCamelCase(pageName));

        var sb = new StringBuilder();
        sb.AppendLine("using Reqnroll;");
        sb.AppendLine("using WebTestToolkit.GeneratedTests.PageObjects;");
        sb.AppendLine();
        sb.AppendLine("namespace WebTestToolkit.GeneratedTests.Steps;");
        sb.AppendLine();
        sb.AppendLine("[Binding]");
        sb.AppendLine($"public class {className}");
        sb.AppendLine("{");

        foreach (var (pageName, fieldName) in pageFields)
            sb.AppendLine($"    private readonly {pageName} {fieldName};");

        sb.AppendLine();
        var ctorParams = string.Join(", ", pageFields.Select(kv => $"{kv.Key} {Naming.ToCamelCase(kv.Key)}"));
        sb.AppendLine($"    public {className}({ctorParams})");
        sb.AppendLine("    {");
        foreach (var (pageName, fieldName) in pageFields)
            sb.AppendLine($"        {fieldName} = {Naming.ToCamelCase(pageName)};");
        sb.AppendLine("    }");

        foreach (var plan in plans)
        {
            sb.AppendLine();
            AppendStepMethod(sb, plan, pageFields[plan.PageName]);
        }

        sb.AppendLine("}");
        return sb.ToString();
    }

    private static void AppendStepMethod(StringBuilder sb, StepPlan plan, string pageField)
    {
        var pattern = Naming.EscapeForVerbatimString(plan.BindingRegexPattern);
        sb.AppendLine($"    [{plan.SectionKeyword}(@\"{pattern}\")]");

        var isParameterizedType = plan.Step.ActionType == ActionType.Type && plan.Step.InputValue is not null;
        var parameter = isParameterizedType ? "string value" : "";
        sb.AppendLine($"    public void {plan.BindingMethodName}({parameter})");
        sb.AppendLine("    {");

        switch (plan.Step.ActionType)
        {
            case ActionType.Navigate:
                sb.AppendLine($"        {pageField}.NavigateTo();");
                break;

            case ActionType.Type:
                sb.AppendLine($"        {pageField}.{plan.PageObjectMethodName}(value);");
                break;

            case ActionType.Click:
                sb.AppendLine($"        {pageField}.{plan.PageObjectMethodName}();");
                break;

            case ActionType.AssertText:
                sb.AppendLine($"        var actual = {pageField}.{plan.PageObjectMethodName}();");
                var expected = Naming.EscapeForRegularString(plan.Step.ExpectedText ?? "");
                sb.AppendLine($"        Assert.That(actual, Does.Contain(\"{expected}\"));");
                break;

            case ActionType.AssertVisible:
                sb.AppendLine($"        Assert.That({pageField}.{plan.PageObjectMethodName}(), Is.True);");
                break;
        }

        sb.AppendLine("    }");
    }
}
