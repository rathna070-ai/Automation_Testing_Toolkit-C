using Reqnroll;
using WebTestToolkit.GeneratedTests.PageObjects;

namespace WebTestToolkit.GeneratedTests.Steps;

[Binding]
[Scope(Feature = "test445")]
public class Test445Steps
{
    private readonly HomePage _homePage;
    private readonly InventoryPage _inventoryPage;
    private readonly CartPage _cartPage;

    public Test445Steps(HomePage homePage, InventoryPage inventoryPage, CartPage cartPage)
    {
        _homePage = homePage;
        _inventoryPage = inventoryPage;
        _cartPage = cartPage;
    }

    [Given(@"I\ open\ the\ home\ page")]
    public void GivenIOpenTheHomePage()
    {
        _homePage.NavigateTo();
    }

    [When(@"I\ click\ the\ user\ name")]
    public void WhenIClickTheUserName()
    {
        _homePage.IClickTheUserName();
    }

    [When(@"I\ enter\ the\ user\ name ""(.*)""")]
    public void WhenIEnterTheUserName(string value)
    {
        _homePage.IEnterTheUserName(value);
    }

    [When(@"I\ click\ the\ password")]
    public void WhenIClickThePassword()
    {
        _homePage.IClickThePassword();
    }

    [When(@"I\ enter\ the\ password ""(.*)""")]
    public void WhenIEnterThePassword(string value)
    {
        _homePage.IEnterThePassword(value);
    }

    [When(@"I\ click\ the\ login\ button\ button")]
    public void WhenIClickTheLoginButtonButton()
    {
        _homePage.IClickTheLoginButtonButton();
    }

    [When(@"I\ click\ the\ inventory\ container")]
    public void WhenIClickTheInventoryContainer()
    {
        _inventoryPage.IClickTheInventoryContainer();
    }

    [When(@"I\ click\ the\ sauce\ labs\ backpackcarry\ all\ the\ things")]
    public void WhenIClickTheSauceLabsBackpackcarryAllTheThings()
    {
        _inventoryPage.IClickTheSauceLabsBackpackcarryAllTheThings();
    }

    [When(@"I\ click\ the\ _2999")]
    public void WhenIClickThe2999()
    {
        _inventoryPage.IClickThe2999();
    }

    [When(@"I\ click\ the\ add\ to\ cart\ sauce\ labs\ backpack\ button")]
    public void WhenIClickTheAddToCartSauceLabsBackpackButton()
    {
        _inventoryPage.IClickTheAddToCartSauceLabsBackpackButton();
    }

    [When(@"I\ click\ the\ _1\ link")]
    public void WhenIClickThe1Link()
    {
        _inventoryPage.IClickThe1Link();
    }

    [When(@"I\ click\ the\ _1\ sauce\ labs\ backpackcarry\ all\ the\ things")]
    public void WhenIClickThe1SauceLabsBackpackcarryAllTheThings()
    {
        _cartPage.IClickThe1SauceLabsBackpackcarryAllTheThings();
    }

    [When(@"I\ click\ the\ qtydescription1\ sauce\ labs\ backpackcarry")]
    public void WhenIClickTheQtydescription1SauceLabsBackpackcarry()
    {
        _cartPage.IClickTheQtydescription1SauceLabsBackpackcarry();
    }
}
