using Reqnroll;
using WebTestToolkit.GeneratedTests.PageObjects;

namespace WebTestToolkit.GeneratedTests.Steps;

[Binding]
public class LoginSteps
{
    private readonly LoginPage _loginPage;

    public LoginSteps(LoginPage loginPage)
    {
        _loginPage = loginPage;
    }

    [Given(@"I am on the login page")]
    public void GivenIAmOnTheLoginPage()
    {
        _loginPage.NavigateTo();
    }

    [When(@"I enter username ""(.*)"" and password ""(.*)""")]
    public void WhenIEnterUsernameAndPassword(string username, string password)
    {
        _loginPage.EnterUsername(username);
        _loginPage.EnterPassword(password);
    }

    [When(@"I click the login button")]
    public void WhenIClickTheLoginButton()
    {
        _loginPage.ClickLogin();
    }

    [Then(@"I should see a success message")]
    public void ThenIShouldSeeASuccessMessage()
    {
        var message = _loginPage.GetFlashMessage();
        Assert.That(message, Does.Contain("You logged into a secure area"));
    }

    [Then(@"I should see an error message")]
    public void ThenIShouldSeeAnErrorMessage()
    {
        var message = _loginPage.GetFlashMessage();
        Assert.That(message, Does.Contain("Your password is invalid"));
    }
}
