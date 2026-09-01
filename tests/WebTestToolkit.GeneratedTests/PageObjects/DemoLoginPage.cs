using OpenQA.Selenium;
using OpenQA.Selenium.Support.UI;
using WebTestToolkit.GeneratedTests.Support;

namespace WebTestToolkit.GeneratedTests.PageObjects;

public class DemoLoginPage
{
    private readonly IWebDriver _driver;
    private readonly WebDriverWait _wait;
    private readonly PageLocators _locators;

    public DemoLoginPage(DriverContext driverContext)
    {
        _driver = driverContext.Driver;
        _wait = new WebDriverWait(_driver, TimeSpan.FromSeconds(10));
        _locators = LocatorRepository.Load("DemoLoginPage");
    }

    public void NavigateTo()
    {
        _driver.Navigate().GoToUrl(_locators.Url);
    }

    public void ISupplyTheDemoUsername(string value)
    {
        var element = FindVisible("UsernameInput");
        element.Clear();
        element.SendKeys(value);
    }

    public void ISupplyTheDemoPassword(string value)
    {
        var element = FindVisible("PasswordInput");
        element.Clear();
        element.SendKeys(value);
    }

    public void IPressTheDemoLoginButton()
    {
        FindVisible("LoginButton").Click();
    }

    public string IShouldReachTheDemoSecureArea()
    {
        return FindVisible("FlashMessage").Text;
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
}
