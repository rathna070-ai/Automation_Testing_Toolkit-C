using OpenQA.Selenium;
using OpenQA.Selenium.Support.UI;
using WebTestToolkit.GeneratedTests.Support;

namespace WebTestToolkit.GeneratedTests.PageObjects;

public class CartPage
{
    private readonly IWebDriver _driver;
    private readonly WebDriverWait _wait;
    private readonly PageLocators _locators;

    public CartPage(DriverContext driverContext)
    {
        _driver = driverContext.Driver;
        _wait = new WebDriverWait(_driver, TimeSpan.FromSeconds(10));
        _locators = LocatorRepository.Load("CartPage");
    }

    public void NavigateTo()
    {
        _driver.Navigate().GoToUrl(_locators.Url);
    }

    public void IClickTheCartContentsContainer()
    {
        ClickSafely(FindVisible("CartContentsContainerElement"), "CartContentsContainerElement");
    }

    public void IClickTheCheckoutButton()
    {
        ClickSafely(FindVisible("CheckoutButton"), "CheckoutButton");
    }

    public void IClickTheContinueShoppingButton()
    {
        ClickSafely(FindVisible("ContinueShoppingButton"), "ContinueShoppingButton");
    }

    public void IClickTheCartContentsContainer2()
    {
        ClickSafely(FindVisible("CartContentsContainerElement2"), "CartContentsContainerElement2");
    }

    public void IClickTheCheckoutButton2()
    {
        ClickSafely(FindVisible("CheckoutButton2"), "CheckoutButton2");
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

    public void IClickThe1SauceLabsBackpackcarryAllTheThings()
    {
        ClickSafely(FindVisible("_1SauceLabsBackpackcarryAllTheThingsElement"), "_1SauceLabsBackpackcarryAllTheThingsElement");
    }

    public void IClickTheQtydescription1SauceLabsBackpackcarry()
    {
        ClickSafely(FindVisible("QTYDescription1SauceLabsBackpackcarryElement"), "QTYDescription1SauceLabsBackpackcarryElement");
    }
}
