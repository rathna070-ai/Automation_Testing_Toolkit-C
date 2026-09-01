using OpenQA.Selenium;
using OpenQA.Selenium.Support.UI;
using WebTestToolkit.GeneratedTests.Support;

namespace WebTestToolkit.GeneratedTests.PageObjects;

public class CheckoutStepTwoPage
{
    private readonly IWebDriver _driver;
    private readonly WebDriverWait _wait;
    private readonly PageLocators _locators;

    public CheckoutStepTwoPage(DriverContext driverContext)
    {
        _driver = driverContext.Driver;
        _wait = new WebDriverWait(_driver, TimeSpan.FromSeconds(10));
        _locators = LocatorRepository.Load("CheckoutStepTwoPage");
    }

    public void NavigateTo()
    {
        _driver.Navigate().GoToUrl(_locators.Url);
    }

    public void IClickTheTotal3239()
    {
        ClickSafely(FindVisible("Total3239Element"), "Total3239Element");
    }

    public void IClickThe29992()
    {
        ClickSafely(FindVisible("_2999Element"), "_2999Element");
    }

    public void IClickTheSwagLabs()
    {
        ClickSafely(FindVisible("SwagLabsElement"), "SwagLabsElement");
    }

    public void IClickThe1Link2()
    {
        ClickSafely(FindVisible("_1Link"), "_1Link");
    }

    public void IClickTheTotal4318()
    {
        ClickSafely(FindVisible("Total4318Element"), "Total4318Element");
    }

    public void IClickTheFinishButton()
    {
        ClickSafely(FindVisible("FinishButton"), "FinishButton");
    }

    private IWebElement FindVisible(string locatorKey)
    {
        var entry = _locators.Locators[locatorKey];
        var by = LocatorRepository.ToBy(entry);
        return _wait.Until(driver =>
        {
            var element = driver.FindElement(by);
            return element.Displayed ? element : null;
        });
    }

    private void ClickSafely(IWebElement element, string locatorKey)
    {
        try
        {
            element.Click();
        }
        catch (ElementClickInterceptedException ex)
        {
            throw new InvalidOperationException(
                $"Could not click '{locatorKey}': something on the page is covering it " +
                "(a cookie banner, consent dialog or modal is the usual cause). " +
                $"Selenium said: {ex.Message}", ex);
        }
        catch (UnhandledAlertException ex)
        {
            throw new InvalidOperationException(
                $"Could not click '{locatorKey}': the page has an open dialog saying " +
                $"\"{ex.AlertText}\". This flow does not handle dialogs — re-record it " +
                "including the dialog, or stop the page raising it.", ex);
        }
    }
}
