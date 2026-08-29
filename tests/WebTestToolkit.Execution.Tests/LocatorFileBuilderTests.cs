using WebTestToolkit.Execution.Generation;
using WebTestToolkit.Llm.Skills;

namespace WebTestToolkit.Execution.Tests;

public class LocatorFileBuilderTests
{
    [Test]
    public void GroupsLocatorsByPage_IntoSeparateFiles()
    {
        var files = LocatorFileBuilder.Build(
        [
            new GeneratedLocatorDto("LoginPage", "UsernameInput", "id", "username", "https://example.com/login"),
            new GeneratedLocatorDto("LoginPage", "LoginButton", "css", "button[type='submit']", "https://example.com/login"),
            new GeneratedLocatorDto("SecurePage", "FlashMessage", "id", "flash", "https://example.com/secure")
        ]);

        Assert.That(files.Keys, Is.EquivalentTo(new[]
        {
            "LocatorRepository/LoginPage.locators.json",
            "LocatorRepository/SecurePage.locators.json"
        }));
    }

    [Test]
    public void MatchesTheShapeLocatorRepositoryReads()
    {
        var files = LocatorFileBuilder.Build(
        [
            new GeneratedLocatorDto("LoginPage", "UsernameInput", "id", "username", "https://example.com/login")
        ]);

        var json = files["LocatorRepository/LoginPage.locators.json"];

        Assert.That(json, Does.Contain("\"url\": \"https://example.com/login\""));
        Assert.That(json, Does.Contain("\"locators\""));
        Assert.That(json, Does.Contain("\"UsernameInput\""));
        Assert.That(json, Does.Contain("\"strategy\": \"id\""));
        Assert.That(json, Does.Contain("\"value\": \"username\""));
    }

    [Test]
    public void KeepsSelectorsHumanReadable()
    {
        var files = LocatorFileBuilder.Build(
        [
            new GeneratedLocatorDto("LoginPage", "LoginButton", "css", "button[type='submit']", "https://example.com")
        ]);

        // These files get hand-edited during auto-heal — ' escaping would make that miserable.
        Assert.That(files["LocatorRepository/LoginPage.locators.json"],
            Does.Contain("button[type='submit']"));
    }
}
