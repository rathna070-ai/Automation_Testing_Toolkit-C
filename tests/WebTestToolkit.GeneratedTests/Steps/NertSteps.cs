using Reqnroll;
using WebTestToolkit.GeneratedTests.PageObjects;

namespace WebTestToolkit.GeneratedTests.Steps;

[Binding]
public class NertSteps
{
    private readonly HomePage _homePage;
    private readonly InventoryPage _inventoryPage;

    public NertSteps(HomePage homePage, InventoryPage inventoryPage)
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

    [When(@"I\ click\ the\ epic\ sadface\ username\ and\ password\ do\ n")]
    public void WhenIClickTheEpicSadfaceUsernameAndPasswordDoN()
    {
        _homePage.IClickTheEpicSadfaceUsernameAndPasswordDoN();
    }

    [When(@"I\ click\ the\ path")]
    public void WhenIClickThePath()
    {
        _homePage.IClickThePath();
    }

    [When(@"I\ click\ the\ epic\ sadface\ username\ and\ password\ do\ n\ \(2\)")]
    public void WhenIClickTheEpicSadfaceUsernameAndPasswordDoN2()
    {
        _homePage.IClickTheEpicSadfaceUsernameAndPasswordDoN2();
    }

    [When(@"I\ click\ the\ login\ button\ button\ \(2\)")]
    public void WhenIClickTheLoginButtonButton2()
    {
        _homePage.IClickTheLoginButtonButton2();
    }

    [When(@"I\ click\ the\ add\ to\ cart\ sauce\ labs\ bike\ light\ button")]
    public void WhenIClickTheAddToCartSauceLabsBikeLightButton()
    {
        _inventoryPage.IClickTheAddToCartSauceLabsBikeLightButton();
    }

    [When(@"I\ click\ the\ _1\ link")]
    public void WhenIClickThe1Link()
    {
        _inventoryPage.IClickThe1Link();
    }
}
