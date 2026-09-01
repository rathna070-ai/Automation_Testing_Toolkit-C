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

    public void IClickTheSauceLabsBackpackcarryAllTheThings()
    {
        ClickSafely(FindVisible("SauceLabsBackpackcarryAllTheThingsElement"), "SauceLabsBackpackcarryAllTheThingsElement");
    }

    public void IClickThe2999()
    {
        ClickSafely(FindVisible("_2999Element"), "_2999Element");
    }

    public void IClickTheAddToCartSauceLabsBackpackButton()
    {
        ClickSafely(FindVisible("AddToCartSauceLabsBackpackButton"), "AddToCartSauceLabsBackpackButton");
    }

    public void IClickTheProductsNameAtoZnameAtoZname()
    {
        ClickSafely(FindVisible("ProductsNameAToZNameAToZNameElement"), "ProductsNameAToZNameAToZNameElement");
    }

    public void IClickThe1Link()
    {
        ClickSafely(FindVisible("_1Link"), "_1Link");
    }

    public void IClickTheAddToCartSauceLabsBikeLightButton()
    {
        ClickSafely(FindVisible("AddToCartSauceLabsBikeLightButton"), "AddToCartSauceLabsBikeLightButton");
    }

    public void IClickThe2Link()
    {
        ClickSafely(FindVisible("_2Link"), "_2Link");
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

    public void IClickTheInventoryContainer()
    {
        ClickSafely(FindVisible("InventoryContainerElement"), "InventoryContainerElement");
    }

    public void IClickTheALink()
    {
        ClickSafely(FindVisible("ALink"), "ALink");
    }
}
