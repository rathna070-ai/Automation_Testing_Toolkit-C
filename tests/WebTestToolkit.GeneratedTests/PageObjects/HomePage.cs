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
        FindVisible("UserNameInput").Click();
    }

    public void IEnterTheUserName(string value)
    {
        var element = FindVisible("UserNameInput2");
        element.Clear();
        element.SendKeys(value);
    }

    public void IClickThePassword()
    {
        FindVisible("PasswordInput").Click();
    }

    public void IEnterThePassword(string value)
    {
        var element = FindVisible("PasswordInput2");
        element.Clear();
        element.SendKeys(value);
    }

    public void IClickTheLoginButtonButton()
    {
        FindVisible("LoginButton").Click();
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
        FindVisible("EpicSadfaceUsernameAndPasswordDoNElement").Click();
    }

    public void IClickThePath()
    {
        FindVisible("PathElement").Click();
    }

    public void IClickTheEpicSadfaceUsernameAndPasswordDoN2()
    {
        FindVisible("EpicSadfaceUsernameAndPasswordDoNElement2").Click();
    }

    public void IClickTheLoginButtonButton2()
    {
        FindVisible("LoginButton2").Click();
    }

    public void IClickThePassword2()
    {
        FindVisible("PasswordInput2").Click();
    }
}
