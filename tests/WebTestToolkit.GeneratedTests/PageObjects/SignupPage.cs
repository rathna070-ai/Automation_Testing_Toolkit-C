using OpenQA.Selenium;
using OpenQA.Selenium.Support.UI;
using WebTestToolkit.GeneratedTests.Support;

namespace WebTestToolkit.GeneratedTests.PageObjects;

// Byte-for-byte the shape PageObjectGenerator emits for an ActionType.Select step — the
// point of this page object is that ChooseTheCountryDropdown is generated code's exact
// output, so running it proves the generator's fix works against a real browser rather
// than only against a string assertion.
public class SignupPage
{
    private readonly IWebDriver _driver;
    private readonly WebDriverWait _wait;
    private readonly PageLocators _locators;

    public SignupPage(DriverContext driverContext)
    {
        _driver = driverContext.Driver;
        _wait = new WebDriverWait(_driver, TimeSpan.FromSeconds(10));
        _locators = LocatorRepository.Load("SignupPage");
    }

    public void NavigateTo()
    {
        _driver.Navigate().GoToUrl(_locators.Url);
    }

    public void ChooseTheCountryDropdown(string value)
    {
        var element = FindVisible("CountryDropdown");
        new SelectElement(element).SelectByText(value);
    }

    public string GetChosenMessage()
    {
        return FindVisible("ChosenMessage").Text;
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
