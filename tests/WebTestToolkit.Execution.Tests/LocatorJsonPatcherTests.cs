using WebTestToolkit.Contracts.Models;
using WebTestToolkit.Execution.Generation;

namespace WebTestToolkit.Execution.Tests;

// Every test points baseDir at a throwaway temp directory rather than the real
// tests/WebTestToolkit.GeneratedTests — this class exists specifically to write files, and
// the real locator files must never be touched by a test run.
public class LocatorJsonPatcherTests
{
    private string _root = "";

    [SetUp]
    public void SetUp()
    {
        _root = Path.Combine(Path.GetTempPath(), "wtt-autoheal-tests-" + Guid.NewGuid().ToString("N"));
        var repoDir = Path.Combine(_root, "LocatorRepository");
        Directory.CreateDirectory(repoDir);

        File.WriteAllText(Path.Combine(repoDir, "LoginPage.locators.json"), """
            {
              "url": "https://example.com/login",
              "locators": {
                "UsernameInput": { "strategy": "id", "value": "username" },
                "LoginButton": { "strategy": "css", "value": "button[type='submit']" }
              }
            }
            """);
    }

    [TearDown]
    public void TearDown()
    {
        if (Directory.Exists(_root))
            Directory.Delete(_root, recursive: true);
    }

    [Test]
    public void ListPages_FindsEveryLocatorFile()
    {
        Assert.That(LocatorJsonPatcher.ListPages(_root), Is.EquivalentTo(new[] { "LoginPage" }));
    }

    [Test]
    public void ListPages_OnMissingDirectory_ReturnsEmpty()
    {
        Assert.That(LocatorJsonPatcher.ListPages(Path.Combine(_root, "nonexistent")), Is.Empty);
    }

    [Test]
    public void Patch_RewritesOnlyTheTargetedKey()
    {
        var patched = LocatorJsonPatcher.Patch(
            "LoginPage", "UsernameInput", new LocatorEntry("css", "#username-v2"), _root);

        Assert.That(patched.Locators["UsernameInput"], Is.EqualTo(new LocatorEntry("css", "#username-v2")));
        // Untouched: the other key, and the page's URL.
        Assert.That(patched.Locators["LoginButton"], Is.EqualTo(new LocatorEntry("css", "button[type='submit']")));
        Assert.That(patched.Url, Is.EqualTo("https://example.com/login"));
    }

    [Test]
    public void Patch_PersistsToDisk()
    {
        LocatorJsonPatcher.Patch("LoginPage", "UsernameInput", new LocatorEntry("id", "healed-id"), _root);

        var reloaded = LocatorJsonPatcher.Load("LoginPage", _root);
        Assert.That(reloaded.Locators["UsernameInput"], Is.EqualTo(new LocatorEntry("id", "healed-id")));
    }

    [Test]
    public void Patch_UnknownKey_Throws()
    {
        Assert.Throws<InvalidOperationException>(() =>
            LocatorJsonPatcher.Patch("LoginPage", "NoSuchKey", new LocatorEntry("id", "x"), _root));
    }

    [Test]
    public void Patch_UnknownPage_ThrowsFileNotFound()
    {
        Assert.Throws<FileNotFoundException>(() =>
            LocatorJsonPatcher.Patch("NoSuchPage", "UsernameInput", new LocatorEntry("id", "x"), _root));
    }

    [Test]
    public void Patch_UnsupportedStrategy_Throws()
    {
        // "text" isn't one LocatorRepository.ToBy() understands — must be rejected before
        // it ever reaches disk, or it would only fail much later, at test-run time.
        Assert.Throws<ArgumentException>(() =>
            LocatorJsonPatcher.Patch("LoginPage", "UsernameInput", new LocatorEntry("text", "Username"), _root));
    }

    [Test]
    public void Patch_PathTraversalInPageName_Throws()
    {
        Assert.Throws<ArgumentException>(() =>
            LocatorJsonPatcher.Patch("../../evil", "UsernameInput", new LocatorEntry("id", "x"), _root));
    }
}
