using System.Text;
using WebTestToolkit.Contracts.Models;

namespace WebTestToolkit.CodeGenerator;

public record GeneratedPageObject(string PageName, string Content);

// Reproduces the shape of the Phase 1 hand-written LoginPage.cs: constructor takes a
// DriverContext, loads locators via LocatorRepository.Load(pageName), one action method
// per captured element, and a shared FindVisible(locatorKey) wait helper.
public static class PageObjectGenerator
{
    public static List<GeneratedPageObject> Generate(IReadOnlyList<StepPlan> plans)
    {
        var results = new List<GeneratedPageObject>();

        foreach (var pageGroup in plans.GroupBy(p => p.PageName))
        {
            var sb = new StringBuilder();
            sb.AppendLine("using OpenQA.Selenium;");
            sb.AppendLine("using OpenQA.Selenium.Support.UI;");
            sb.AppendLine("using WebTestToolkit.GeneratedTests.Support;");
            sb.AppendLine();
            sb.AppendLine("namespace WebTestToolkit.GeneratedTests.PageObjects;");
            sb.AppendLine();
            sb.AppendLine($"public class {pageGroup.Key}");
            sb.AppendLine("{");
            sb.AppendLine("    private readonly IWebDriver _driver;");
            sb.AppendLine("    private readonly WebDriverWait _wait;");
            sb.AppendLine("    private readonly PageLocators _locators;");
            sb.AppendLine();
            sb.AppendLine($"    public {pageGroup.Key}(DriverContext driverContext)");
            sb.AppendLine("    {");
            sb.AppendLine("        _driver = driverContext.Driver;");
            sb.AppendLine("        _wait = new WebDriverWait(_driver, TimeSpan.FromSeconds(10));");
            sb.AppendLine($"        _locators = LocatorRepository.Load(\"{pageGroup.Key}\");");
            sb.AppendLine("    }");
            sb.AppendLine();
            sb.AppendLine("    public void NavigateTo()");
            sb.AppendLine("    {");
            sb.AppendLine("        _driver.Navigate().GoToUrl(_locators.Url);");
            sb.AppendLine("    }");

            var emittedMethods = new HashSet<string>();
            foreach (var plan in pageGroup.Where(p => p.Step.ActionType != ActionType.Navigate))
            {
                if (!emittedMethods.Add(plan.PageObjectMethodName))
                    continue;

                sb.AppendLine();
                AppendActionMethod(sb, plan);
            }

            sb.AppendLine();
            sb.AppendLine("    private IWebElement FindVisible(string locatorKey)");
            sb.AppendLine("    {");
            sb.AppendLine("        var entry = _locators.Locators[locatorKey];");
            sb.AppendLine("        var by = LocatorRepository.ToBy(entry);");
            sb.AppendLine("        return _wait.Until(driver =>");
            sb.AppendLine("        {");
            sb.AppendLine("            var element = driver.FindElement(by);");
            sb.AppendLine("            return element.Displayed ? element : null;");
            sb.AppendLine("        });");
            sb.AppendLine("    }");
            sb.AppendLine("}");

            results.Add(new GeneratedPageObject(pageGroup.Key, sb.ToString()));
        }

        return results;
    }

    private static void AppendActionMethod(StringBuilder sb, StepPlan plan)
    {
        switch (plan.Step.ActionType)
        {
            case ActionType.Type:
                sb.AppendLine($"    public void {plan.PageObjectMethodName}(string value)");
                sb.AppendLine("    {");
                sb.AppendLine($"        var element = FindVisible(\"{plan.LocatorKey}\");");
                sb.AppendLine("        element.Clear();");
                sb.AppendLine("        element.SendKeys(value);");
                sb.AppendLine("    }");
                break;

            case ActionType.Select:
                sb.AppendLine($"    public void {plan.PageObjectMethodName}(string value)");
                sb.AppendLine("    {");
                sb.AppendLine($"        var element = FindVisible(\"{plan.LocatorKey}\");");
                // Clear()+SendKeys() would throw here: Clear() on a non-editable element is
                // "invalid element state" per the WebDriver spec. SelectElement is the
                // supported way to drive a <select>, and SelectByText matches what the
                // Gherkin step and the captured option text both say.
                sb.AppendLine("        new SelectElement(element).SelectByText(value);");
                sb.AppendLine("    }");
                break;

            case ActionType.Click:
                sb.AppendLine($"    public void {plan.PageObjectMethodName}()");
                sb.AppendLine("    {");
                sb.AppendLine($"        FindVisible(\"{plan.LocatorKey}\").Click();");
                sb.AppendLine("    }");
                break;

            case ActionType.AssertText:
                sb.AppendLine($"    public string {plan.PageObjectMethodName}()");
                sb.AppendLine("    {");
                sb.AppendLine($"        return FindVisible(\"{plan.LocatorKey}\").Text;");
                sb.AppendLine("    }");
                break;

            case ActionType.AssertVisible:
                sb.AppendLine($"    public bool {plan.PageObjectMethodName}()");
                sb.AppendLine("    {");
                sb.AppendLine($"        return FindVisible(\"{plan.LocatorKey}\").Displayed;");
                sb.AppendLine("    }");
                break;
        }
    }
}
