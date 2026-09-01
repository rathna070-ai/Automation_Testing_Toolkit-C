using Reqnroll;
using WebTestToolkit.GeneratedTests.PageObjects;

namespace WebTestToolkit.GeneratedTests.Steps;

[Binding]
[Scope(Feature = "flow new 1")]
public class FlowNew1Steps
{
    private readonly HomePage _homePage;
    private readonly InventoryPage _inventoryPage;
    private readonly CartPage _cartPage;
    private readonly CheckoutStepOnePage _checkoutStepOnePage;
    private readonly CheckoutStepTwoPage _checkoutStepTwoPage;
    private readonly CheckoutCompletePage _checkoutCompletePage;

    public FlowNew1Steps(HomePage homePage, InventoryPage inventoryPage, CartPage cartPage, CheckoutStepOnePage checkoutStepOnePage, CheckoutStepTwoPage checkoutStepTwoPage, CheckoutCompletePage checkoutCompletePage)
    {
        _homePage = homePage;
        _inventoryPage = inventoryPage;
        _cartPage = cartPage;
        _checkoutStepOnePage = checkoutStepOnePage;
        _checkoutStepTwoPage = checkoutStepTwoPage;
        _checkoutCompletePage = checkoutCompletePage;
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

    [When(@"I\ click\ the\ login\ button")]
    public void WhenIClickTheLoginButton()
    {
        _homePage.IClickTheLoginButton();
    }

    [When(@"I\ click\ the\ sauce\ labs\ backpackcarry\ all\ the\ things")]
    public void WhenIClickTheSauceLabsBackpackcarryAllTheThings()
    {
        _inventoryPage.IClickTheSauceLabsBackpackcarryAllTheThings();
    }

    [When(@"I\ click\ the\ \$29\.99")]
    public void WhenIClickThe2999()
    {
        _inventoryPage.IClickThe2999();
    }

    [When(@"I\ click\ the\ add\ to\ cart\ sauce\ labs\ backpack\ button")]
    public void WhenIClickTheAddToCartSauceLabsBackpackButton()
    {
        _inventoryPage.IClickTheAddToCartSauceLabsBackpackButton();
    }

    [When(@"I\ click\ the\ products\ name\ ato\ zname\ ato\ zname")]
    public void WhenIClickTheProductsNameAtoZnameAtoZname()
    {
        _inventoryPage.IClickTheProductsNameAtoZnameAtoZname();
    }

    [When(@"I\ click\ the\ 1\ link")]
    public void WhenIClickThe1Link()
    {
        _inventoryPage.IClickThe1Link();
    }

    [When(@"I\ click\ the\ cart\ contents\ container")]
    public void WhenIClickTheCartContentsContainer()
    {
        _cartPage.IClickTheCartContentsContainer();
    }

    [When(@"I\ click\ the\ checkout\ button")]
    public void WhenIClickTheCheckoutButton()
    {
        _cartPage.IClickTheCheckoutButton();
    }

    [When(@"I\ click\ the\ first\ name")]
    public void WhenIClickTheFirstName()
    {
        _checkoutStepOnePage.IClickTheFirstName();
    }

    [When(@"I\ enter\ the\ first\ name ""(.*)""")]
    public void WhenIEnterTheFirstName(string value)
    {
        _checkoutStepOnePage.IEnterTheFirstName(value);
    }

    [When(@"I\ click\ the\ last\ name")]
    public void WhenIClickTheLastName()
    {
        _checkoutStepOnePage.IClickTheLastName();
    }

    [When(@"I\ enter\ the\ last\ name ""(.*)""")]
    public void WhenIEnterTheLastName(string value)
    {
        _checkoutStepOnePage.IEnterTheLastName(value);
    }

    [When(@"I\ click\ the\ postal\ code")]
    public void WhenIClickThePostalCode()
    {
        _checkoutStepOnePage.IClickThePostalCode();
    }

    [When(@"I\ enter\ the\ postal\ code ""(.*)""")]
    public void WhenIEnterThePostalCode(string value)
    {
        _checkoutStepOnePage.IEnterThePostalCode(value);
    }

    [When(@"I\ click\ the\ continue\ button")]
    public void WhenIClickTheContinueButton()
    {
        _checkoutStepOnePage.IClickTheContinueButton();
    }

    [When(@"I\ click\ the\ total3239")]
    public void WhenIClickTheTotal3239()
    {
        _checkoutStepTwoPage.IClickTheTotal3239();
    }

    [When(@"I\ click\ the\ \$29\.99\ \(2\)")]
    public void WhenIClickThe29992()
    {
        _checkoutStepTwoPage.IClickThe29992();
    }

    [When(@"I\ click\ the\ swag\ labs")]
    public void WhenIClickTheSwagLabs()
    {
        _checkoutStepTwoPage.IClickTheSwagLabs();
    }

    [When(@"I\ click\ the\ 1\ link\ \(2\)")]
    public void WhenIClickThe1Link2()
    {
        _checkoutStepTwoPage.IClickThe1Link2();
    }

    [When(@"I\ click\ the\ continue\ shopping\ button")]
    public void WhenIClickTheContinueShoppingButton()
    {
        _cartPage.IClickTheContinueShoppingButton();
    }

    [When(@"I\ click\ the\ add\ to\ cart\ sauce\ labs\ bike\ light\ button")]
    public void WhenIClickTheAddToCartSauceLabsBikeLightButton()
    {
        _inventoryPage.IClickTheAddToCartSauceLabsBikeLightButton();
    }

    [When(@"I\ click\ the\ 2\ link")]
    public void WhenIClickThe2Link()
    {
        _inventoryPage.IClickThe2Link();
    }

    [When(@"I\ click\ the\ cart\ contents\ container\ \(2\)")]
    public void WhenIClickTheCartContentsContainer2()
    {
        _cartPage.IClickTheCartContentsContainer2();
    }

    [When(@"I\ click\ the\ checkout\ button\ \(2\)")]
    public void WhenIClickTheCheckoutButton2()
    {
        _cartPage.IClickTheCheckoutButton2();
    }

    [When(@"I\ click\ the\ postal\ code\ \(2\)")]
    public void WhenIClickThePostalCode2()
    {
        _checkoutStepOnePage.IClickThePostalCode2();
    }

    [When(@"I\ click\ the\ last\ name\ \(2\)")]
    public void WhenIClickTheLastName2()
    {
        _checkoutStepOnePage.IClickTheLastName2();
    }

    [When(@"I\ click\ the\ first\ name\ \(2\)")]
    public void WhenIClickTheFirstName2()
    {
        _checkoutStepOnePage.IClickTheFirstName2();
    }

    [When(@"I\ click\ the\ div")]
    public void WhenIClickTheDiv()
    {
        _checkoutStepOnePage.IClickTheDiv();
    }

    [When(@"I\ click\ the\ continue\ button\ \(2\)")]
    public void WhenIClickTheContinueButton2()
    {
        _checkoutStepOnePage.IClickTheContinueButton2();
    }

    [When(@"I\ click\ the\ total4318")]
    public void WhenIClickTheTotal4318()
    {
        _checkoutStepTwoPage.IClickTheTotal4318();
    }

    [When(@"I\ click\ the\ finish\ button")]
    public void WhenIClickTheFinishButton()
    {
        _checkoutStepTwoPage.IClickTheFinishButton();
    }

    [When(@"I\ click\ the\ checkout\ complete\ container")]
    public void WhenIClickTheCheckoutCompleteContainer()
    {
        _checkoutCompletePage.IClickTheCheckoutCompleteContainer();
    }

    [When(@"I\ click\ the\ img")]
    public void WhenIClickTheImg()
    {
        _checkoutCompletePage.IClickTheImg();
    }
}
