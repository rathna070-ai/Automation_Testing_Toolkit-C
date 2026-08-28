using OpenQA.Selenium;
using OpenQA.Selenium.Support.UI;
using WebTestToolkit.GeneratedTests.Support;

namespace WebTestToolkit.GeneratedTests.PageObjects;

public class LoginPage
{
    private readonly IWebDriver _driver;
    private readonly WebDriverWait _wait;
    private readonly PageLocators _locators;

    public LoginPage(DriverContext driverContext)
    {
        _driver = driverContext.Driver;
        _wait = new WebDriverWait(_driver, TimeSpan.FromSeconds(10));
        _locators = LocatorRepository.Load("LoginPage");
    }

    public void NavigateTo()
    {
        _driver.Navigate().GoToUrl(_locators.Url);
    }

    public void EnterUsername(string username)
    {
        var element = FindVisible("UsernameInput");
        element.Clear();
        element.SendKeys(username);
    }

    public void EnterPassword(string password)
    {
        var element = FindVisible("PasswordInput");
        element.Clear();
        element.SendKeys(password);
    }

    public void ClickLogin()
    {
        FindVisible("LoginButton").Click();
    }

    public string GetFlashMessage()
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
