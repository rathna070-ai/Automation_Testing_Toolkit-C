using OpenQA.Selenium;
using OpenQA.Selenium.Support.UI;
using WebTestToolkit.GeneratedTests.Support;

namespace WebTestToolkit.GeneratedTests.PageObjects;

public class HomePage
{
    private readonly IWebDriver _driver;
    private readonly WebDriverWait _wait;
    private readonly PageLocators _locators;

    public HomePage(DriverContext driverContext)
    {
        _driver = driverContext.Driver;
        _wait = new WebDriverWait(_driver, TimeSpan.FromSeconds(10));
        _locators = LocatorRepository.Load("HomePage");
    }

    public void NavigateTo()
    {
        _driver.Navigate().GoToUrl(_locators.Url);
    }

    public void IClickTheUserName()
    {
        ClickSafely(FindVisible("UserNameInput"), "UserNameInput");
    }

    public void IEnterTheUserName(string value)
    {
        var element = FindVisible("UserNameInput2");
        element.Clear();
        element.SendKeys(value);
    }

    public void IClickThePassword()
    {
        ClickSafely(FindVisible("PasswordInput"), "PasswordInput");
    }

    public void IEnterThePassword(string value)
    {
        var element = FindVisible("PasswordInput2");
        element.Clear();
        element.SendKeys(value);
    }

    public void IClickTheLoginButtonButton()
    {
        ClickSafely(FindVisible("LoginButton"), "LoginButton");
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

    public void IClickTheEpicSadfaceUsernameAndPasswordDoN()
    {
        ClickSafely(FindVisible("EpicSadfaceUsernameAndPasswordDoNElement"), "EpicSadfaceUsernameAndPasswordDoNElement");
    }

    public void IClickThePath()
    {
        ClickSafely(FindVisible("PathElement"), "PathElement");
    }

    public void IClickTheEpicSadfaceUsernameAndPasswordDoN2()
    {
        ClickSafely(FindVisible("EpicSadfaceUsernameAndPasswordDoNElement2"), "EpicSadfaceUsernameAndPasswordDoNElement2");
    }

    public void IClickTheLoginButtonButton2()
    {
        ClickSafely(FindVisible("LoginButton2"), "LoginButton2");
    }

    public void IClickThePassword2()
    {
        ClickSafely(FindVisible("PasswordInput2"), "PasswordInput2");
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
