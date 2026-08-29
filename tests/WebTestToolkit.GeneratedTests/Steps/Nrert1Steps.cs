using Reqnroll;
using WebTestToolkit.GeneratedTests.PageObjects;

namespace WebTestToolkit.GeneratedTests.Steps;

[Binding]
public class Nrert1Steps
{
    private readonly HomePage _homePage;
    private readonly InventoryPage _inventoryPage;

    public Nrert1Steps(HomePage homePage, InventoryPage inventoryPage)
    {
        _homePage = homePage;
        _inventoryPage = inventoryPage;
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

    [When(@"I\ click\ the\ password\ \(2\)")]
    public void WhenIClickThePassword2()
    {
        _homePage.IClickThePassword2();
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

    [When(@"I\ click\ the\ sauce\ labs\ backpackcarry\ all\ the\ things")]
    public void WhenIClickTheSauceLabsBackpackcarryAllTheThings()
    {
        _inventoryPage.IClickTheSauceLabsBackpackcarryAllTheThings();
    }

    [When(@"I\ click\ the\ a\ link")]
    public void WhenIClickTheALink()
    {
        _inventoryPage.IClickTheALink();
    }
}
