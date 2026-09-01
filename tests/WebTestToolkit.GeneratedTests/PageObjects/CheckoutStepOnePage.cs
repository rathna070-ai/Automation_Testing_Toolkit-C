using OpenQA.Selenium;
using OpenQA.Selenium.Support.UI;
using WebTestToolkit.GeneratedTests.Support;

namespace WebTestToolkit.GeneratedTests.PageObjects;

public class CheckoutStepOnePage
{
    private readonly IWebDriver _driver;
    private readonly WebDriverWait _wait;
    private readonly PageLocators _locators;

    public CheckoutStepOnePage(DriverContext driverContext)
    {
        _driver = driverContext.Driver;
        _wait = new WebDriverWait(_driver, TimeSpan.FromSeconds(10));
        _locators = LocatorRepository.Load("CheckoutStepOnePage");
    }

    public void NavigateTo()
    {
        _driver.Navigate().GoToUrl(_locators.Url);
    }

    public void IClickTheFirstName()
    {
        ClickSafely(FindVisible("FirstNameInput"), "FirstNameInput");
    }

    public void IEnterTheFirstName(string value)
    {
        var element = FindVisible("FirstNameInput2");
        element.Clear();
        element.SendKeys(value);
    }

    public void IClickTheLastName()
    {
        ClickSafely(FindVisible("LastNameInput"), "LastNameInput");
    }

    public void IEnterTheLastName(string value)
    {
        var element = FindVisible("LastNameInput2");
        element.Clear();
        element.SendKeys(value);
    }

    public void IClickThePostalCode()
    {
        ClickSafely(FindVisible("PostalCodeInput"), "PostalCodeInput");
    }

    public void IEnterThePostalCode(string value)
    {
        var element = FindVisible("PostalCodeInput2");
        element.Clear();
        element.SendKeys(value);
    }

    public void IClickTheContinueButton()
    {
        ClickSafely(FindVisible("ContinueButton"), "ContinueButton");
    }

    public void IClickThePostalCode2()
    {
        ClickSafely(FindVisible("PostalCodeInput3"), "PostalCodeInput3");
    }

    public void IClickTheLastName2()
    {
        ClickSafely(FindVisible("LastNameInput3"), "LastNameInput3");
    }

    public void IClickTheFirstName2()
    {
        ClickSafely(FindVisible("FirstNameInput3"), "FirstNameInput3");
    }

    public void IClickTheDiv()
    {
        ClickSafely(FindVisible("DivElement"), "DivElement");
    }

    public void IClickTheContinueButton2()
    {
        ClickSafely(FindVisible("ContinueButton2"), "ContinueButton2");
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
