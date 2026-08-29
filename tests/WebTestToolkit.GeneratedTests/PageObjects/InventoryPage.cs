using OpenQA.Selenium;
using OpenQA.Selenium.Support.UI;
using WebTestToolkit.GeneratedTests.Support;

namespace WebTestToolkit.GeneratedTests.PageObjects;

public class InventoryPage
{
    private readonly IWebDriver _driver;
    private readonly WebDriverWait _wait;
    private readonly PageLocators _locators;

    public InventoryPage(DriverContext driverContext)
    {
        _driver = driverContext.Driver;
        _wait = new WebDriverWait(_driver, TimeSpan.FromSeconds(10));
        _locators = LocatorRepository.Load("InventoryPage");
    }

    public void NavigateTo()
    {
        _driver.Navigate().GoToUrl(_locators.Url);
    }

    public void IClickTheAddToCartSauceLabsBikeLightButton()
    {
        FindVisible("AddToCartSauceLabsBikeLightButton").Click();
    }

    public void IClickThe1Link()
    {
        FindVisible("_1Link").Click();
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

    public void IClickTheSauceLabsBackpackcarryAllTheThings()
    {
        FindVisible("SauceLabsBackpackcarryAllTheThingsElement").Click();
    }

    public void IClickTheALink()
    {
        FindVisible("ALink").Click();
    }
}
