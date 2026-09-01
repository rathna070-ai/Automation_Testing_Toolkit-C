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
            sb.AppendLine();
            AppendClickSafely(sb);
            sb.AppendLine("}");

            results.Add(new GeneratedPageObject(pageGroup.Key, sb.ToString()));
        }

        return results;
    }

    // Selenium reports a click blocked by an overlay as ElementClickInterceptedException, and
    // an open JS dialog as UnhandledAlertException on whatever command happens to run next.
    // Both name neither the step nor the cause, so a cookie banner or a stray alert() reads as
    // an unrelated mystery failure. Rethrowing with the locator key — and, for a dialog, its
    // actual text — turns each into something a person can act on, and gives the failure
    // analysis skill real material instead of a bare exception type.
    //
    // Note this catches rather than *handles*: an overlay is not dismissed and a dialog is not
    // answered. Silently clicking past either would let a scenario report success for a step it
    // never really performed.
    private static void AppendClickSafely(StringBuilder sb)
    {
        sb.AppendLine("    private void ClickSafely(IWebElement element, string locatorKey)");
        sb.AppendLine("    {");
        sb.AppendLine("        try");
        sb.AppendLine("        {");
        sb.AppendLine("            element.Click();");
        sb.AppendLine("        }");
        sb.AppendLine("        catch (ElementClickInterceptedException ex)");
        sb.AppendLine("        {");
        sb.AppendLine("            throw new InvalidOperationException(");
        sb.AppendLine("                $\"Could not click '{locatorKey}': something on the page is covering it \" +");
        sb.AppendLine("                \"(a cookie banner, consent dialog or modal is the usual cause). \" +");
        sb.AppendLine("                $\"Selenium said: {ex.Message}\", ex);");
        sb.AppendLine("        }");
        sb.AppendLine("        catch (UnhandledAlertException ex)");
        sb.AppendLine("        {");
        sb.AppendLine("            throw new InvalidOperationException(");
        sb.AppendLine("                $\"Could not click '{locatorKey}': the page has an open dialog saying \" +");
        sb.AppendLine("                $\"\\\"{ex.AlertText}\\\". This flow does not handle dialogs — re-record it \" +");
        sb.AppendLine("                \"including the dialog, or stop the page raising it.\", ex);");
        sb.AppendLine("        }");
        sb.AppendLine("    }");
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

            case ActionType.Click when DesiredToggleState(plan.Step.Element) is { } desired:
                // A checkbox or radio whose recorded end state we know. An unconditional
                // Click() *toggles*, so replaying it against a page that already has the box
                // set — a remembered preference, a second run in the same session, a default
                // that changed — silently inverts the state the recording captured and the
                // rest of the scenario then tests the wrong thing. Asserting the end state
                // instead of repeating the gesture makes the step idempotent.
                sb.AppendLine($"    public void {plan.PageObjectMethodName}()");
                sb.AppendLine("    {");
                sb.AppendLine($"        var element = FindVisible(\"{plan.LocatorKey}\");");
                sb.AppendLine($"        if (element.Selected != {(desired ? "true" : "false")})");
                sb.AppendLine($"            ClickSafely(element, \"{plan.LocatorKey}\");");
                sb.AppendLine("    }");
                break;

            case ActionType.Click:
                sb.AppendLine($"    public void {plan.PageObjectMethodName}()");
                sb.AppendLine("    {");
                sb.AppendLine($"        ClickSafely(FindVisible(\"{plan.LocatorKey}\"), \"{plan.LocatorKey}\");");
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

    // The end state a click on a checkbox/radio was recorded as producing, or null when the
    // element is not a toggle (or the capture predates Checked being recorded) and a plain
    // Click() is the right thing.
    //
    // Radios are deliberately included only for the "ends up selected" case: clicking a radio
    // cannot deselect it, so a false here would generate a branch that can never run.
    private static bool? DesiredToggleState(CapturedElement? element)
    {
        if (element?.Checked is not { } isChecked)
            return null;

        var type = (element.Type ?? "").ToLowerInvariant();
        if (type == "checkbox")
            return isChecked;

        return type == "radio" && isChecked ? true : null;
    }
}
