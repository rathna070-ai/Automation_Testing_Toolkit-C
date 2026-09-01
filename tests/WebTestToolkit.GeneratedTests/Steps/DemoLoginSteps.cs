using Reqnroll;
using WebTestToolkit.GeneratedTests.PageObjects;

namespace WebTestToolkit.GeneratedTests.Steps;

[Binding]
[Scope(Feature = "DemoLogin")]
public class DemoLoginSteps
{
    private readonly DemoLoginPage _demoLoginPage;

    public DemoLoginSteps(DemoLoginPage demoLoginPage)
    {
        _demoLoginPage = demoLoginPage;
    }

    [Given(@"I\ browse\ to\ the\ demo\ login\ page")]
    public void GivenIBrowseToTheDemoLoginPage()
    {
        _demoLoginPage.NavigateTo();
    }

    [When(@"I\ supply\ the\ demo\ username ""(.*)""")]
    public void WhenISupplyTheDemoUsername(string value)
    {
        _demoLoginPage.ISupplyTheDemoUsername(value);
    }

    [When(@"I\ supply\ the\ demo\ password ""(.*)""")]
    public void WhenISupplyTheDemoPassword(string value)
    {
        _demoLoginPage.ISupplyTheDemoPassword(value);
    }

    [When(@"I\ press\ the\ demo\ login\ button")]
    public void WhenIPressTheDemoLoginButton()
    {
        _demoLoginPage.IPressTheDemoLoginButton();
    }

    [Then(@"I\ should\ reach\ the\ demo\ secure\ area")]
    public void ThenIShouldReachTheDemoSecureArea()
    {
        var actual = _demoLoginPage.IShouldReachTheDemoSecureArea();
        Assert.That(actual, Does.Contain("You logged into a secure area"), "FlashMessage did not contain the expected text.");
    }
}
