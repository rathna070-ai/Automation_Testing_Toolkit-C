using Reqnroll;
using WebTestToolkit.GeneratedTests.PageObjects;

namespace WebTestToolkit.GeneratedTests.Steps;

[Binding]
[Scope(Feature = "signup")]
public class SignupSteps
{
    private readonly SignupPage _signupPage;

    public SignupSteps(SignupPage signupPage)
    {
        _signupPage = signupPage;
    }

    [Given(@"I open the signup page")]
    public void GivenIOpenTheSignupPage()
    {
        _signupPage.NavigateTo();
    }

    [When(@"I choose the country dropdown ""(.*)""")]
    public void WhenIChooseTheCountryDropdown(string value)
    {
        _signupPage.ChooseTheCountryDropdown(value);
    }

    [Then(@"I should see the chosen message")]
    public void ThenIShouldSeeTheChosenMessage()
    {
        var actual = _signupPage.GetChosenMessage();
        Assert.That(actual, Does.Contain("India"));
    }
}
