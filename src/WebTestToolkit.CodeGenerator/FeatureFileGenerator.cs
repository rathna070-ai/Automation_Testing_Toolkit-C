using System.Text;
using WebTestToolkit.Contracts.Models;

namespace WebTestToolkit.CodeGenerator;

public static class FeatureFileGenerator
{
    public static string Generate(TestFlow flow, IReadOnlyList<StepPlan> plans)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"Feature: {flow.Name}");
        sb.AppendLine();
        sb.AppendLine($"  Scenario: {flow.Name} flow");
        foreach (var plan in plans)
            sb.AppendLine($"    {plan.DisplayKeyword} {plan.GherkinLine}");

        return sb.ToString();
    }
}
