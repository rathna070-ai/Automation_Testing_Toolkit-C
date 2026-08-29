using WebTestToolkit.Execution.Generation;

namespace WebTestToolkit.Execution.Tests;

// Every test points projectDir at a throwaway temp directory rather than the real
// tests/WebTestToolkit.GeneratedTests — this class exists specifically to read files.
public class PageObjectMergerTests
{
    private string _root = "";

    [SetUp]
    public void SetUp() =>
        _root = Path.Combine(Path.GetTempPath(), "wtt-pageobjectmerger-tests-" + Guid.NewGuid().ToString("N"));

    [TearDown]
    public void TearDown()
    {
        if (Directory.Exists(_root))
            Directory.Delete(_root, recursive: true);
    }

    private void WriteExisting(string relativePath, string content)
    {
        var full = Path.Combine(_root, relativePath.Replace('/', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(Path.GetDirectoryName(full)!);
        File.WriteAllText(full, content);
    }

    private const string ExistingHomePage = """
        using OpenQA.Selenium;
        using OpenQA.Selenium.Support.UI;
        using WebTestToolkit.GeneratedTests.Support;

        namespace WebTestToolkit.GeneratedTests.PageObjects;

        public class HomePage
        {
            private readonly IWebDriver _driver;
            private readonly WebDriverWait _wait;
            private readonly PageLocators _locators;

            public HomePage(DriverContext driverContext)
            {
                _driver = driverContext.Driver;
                _wait = new WebDriverWait(_driver, TimeSpan.FromSeconds(10));
                _locators = LocatorRepository.Load("HomePage");
            }

            public void NavigateTo()
            {
                _driver.Navigate().GoToUrl(_locators.Url);
            }

            public void IClickThePassword()
            {
                FindVisible("PasswordA").Click();
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
        }
        """;

    // The fresh generation only needs a different method on the same page — it doesn't
    // redefine IClickThePassword at all.
    private const string FreshHomePage = """
        using OpenQA.Selenium;
        using OpenQA.Selenium.Support.UI;
        using WebTestToolkit.GeneratedTests.Support;

        namespace WebTestToolkit.GeneratedTests.PageObjects;

        public class HomePage
        {
            private readonly IWebDriver _driver;
            private readonly WebDriverWait _wait;
            private readonly PageLocators _locators;

            public HomePage(DriverContext driverContext)
            {
                _driver = driverContext.Driver;
                _wait = new WebDriverWait(_driver, TimeSpan.FromSeconds(10));
                _locators = LocatorRepository.Load("HomePage");
            }

            public void NavigateTo()
            {
                _driver.Navigate().GoToUrl(_locators.Url);
            }

            public void IClickThePasswordField()
            {
                FindVisible("PasswordB").Click();
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
        }
        """;

    [Test]
    public void MergeWithExisting_PreservesAMethodTheFreshGenerationDoesNotRedefine()
    {
        WriteExisting("PageObjects/HomePage.cs", ExistingHomePage);

        var candidate = new Dictionary<string, string> { ["PageObjects/HomePage.cs"] = FreshHomePage };
        var merged = PageObjectMerger.MergeWithExisting(candidate, _root);

        var mergedContent = merged["PageObjects/HomePage.cs"];
        Assert.Multiple(() =>
        {
            // The other, differently-named flow's own new method is untouched.
            Assert.That(mergedContent, Does.Contain("public void IClickThePasswordField()"));
            Assert.That(mergedContent, Does.Contain("FindVisible(\"PasswordB\")"));

            // The earlier flow's method survives the merge — this is the whole fix.
            Assert.That(mergedContent, Does.Contain("public void IClickThePassword()"));
            Assert.That(mergedContent, Does.Contain("FindVisible(\"PasswordA\")"));
        });
    }

    [Test]
    public void MergeWithExisting_NeverDuplicatesTheConstructorOrFindVisible()
    {
        WriteExisting("PageObjects/HomePage.cs", ExistingHomePage);

        var candidate = new Dictionary<string, string> { ["PageObjects/HomePage.cs"] = FreshHomePage };
        var merged = PageObjectMerger.MergeWithExisting(candidate, _root);
        var mergedContent = merged["PageObjects/HomePage.cs"];

        Assert.Multiple(() =>
        {
            Assert.That(CountOccurrences(mergedContent, "public HomePage(DriverContext driverContext)"), Is.EqualTo(1));
            Assert.That(CountOccurrences(mergedContent, "private IWebElement FindVisible(string locatorKey)"), Is.EqualTo(1));
        });
    }

    [Test]
    public void MergeWithExisting_FreshVersionWinsWhenBothDefineTheSameMethod()
    {
        // Same method name, different body — the current flow's own fresh version must win,
        // not the stale one on disk.
        var existing = ExistingHomePage.Replace(
            """FindVisible("PasswordA")""", """FindVisible("StaleLocatorKey")""");
        WriteExisting("PageObjects/HomePage.cs", existing);

        var fresh = FreshHomePage.Replace(
            "IClickThePasswordField", "IClickThePassword").Replace(
            """FindVisible("PasswordB")""", """FindVisible("FreshLocatorKey")""");

        var candidate = new Dictionary<string, string> { ["PageObjects/HomePage.cs"] = fresh };
        var merged = PageObjectMerger.MergeWithExisting(candidate, _root);
        var mergedContent = merged["PageObjects/HomePage.cs"];

        Assert.Multiple(() =>
        {
            Assert.That(mergedContent, Does.Contain("FreshLocatorKey"));
            Assert.That(mergedContent, Does.Not.Contain("StaleLocatorKey"));
            Assert.That(CountOccurrences(mergedContent, "public void IClickThePassword()"), Is.EqualTo(1));
        });
    }

    [Test]
    public void MergeWithExisting_NewPageWithNoExistingFile_IsUnchanged()
    {
        var candidate = new Dictionary<string, string> { ["PageObjects/BrandNewPage.cs"] = FreshHomePage };
        var merged = PageObjectMerger.MergeWithExisting(candidate, _root);

        Assert.That(merged["PageObjects/BrandNewPage.cs"], Is.EqualTo(FreshHomePage));
    }

    [Test]
    public void MergeWithExisting_NonPageObjectPaths_AreLeftAlone()
    {
        var candidate = new Dictionary<string, string>
        {
            ["Steps/FlowSteps.cs"] = "// steps content",
            ["Features/Flow.feature"] = "Feature: Flow"
        };

        var merged = PageObjectMerger.MergeWithExisting(candidate, _root);

        Assert.That(merged, Is.EquivalentTo(candidate));
    }

    // --- LocatorRepository/*.locators.json merging — the exact same bug, one file type over.

    private const string ExistingHomeLocators = """
        {
          "url": "https://example.com/home",
          "locators": {
            "PasswordA": { "strategy": "id", "value": "password" },
            "UserName": { "strategy": "id", "value": "user-name" }
          }
        }
        """;

    private const string FreshHomeLocators = """
        {
          "url": "https://example.com/home",
          "locators": {
            "PasswordB": { "strategy": "id", "value": "password" }
          }
        }
        """;

    [Test]
    public void MergeWithExisting_PreservesALocatorKeyTheFreshGenerationDoesNotRedefine()
    {
        WriteExisting("LocatorRepository/HomePage.locators.json", ExistingHomeLocators);

        var candidate = new Dictionary<string, string> { ["LocatorRepository/HomePage.locators.json"] = FreshHomeLocators };
        var merged = PageObjectMerger.MergeWithExisting(candidate, _root);

        Assert.Multiple(() =>
        {
            Assert.That(merged["LocatorRepository/HomePage.locators.json"], Does.Contain("\"PasswordB\""));
            // The other flow's locator keys survive the merge — this is the whole fix.
            Assert.That(merged["LocatorRepository/HomePage.locators.json"], Does.Contain("\"PasswordA\""));
            Assert.That(merged["LocatorRepository/HomePage.locators.json"], Does.Contain("\"UserName\""));
        });
    }

    [Test]
    public void MergeWithExisting_LocatorFile_FreshValueWinsWhenKeysCollide()
    {
        var existing = ExistingHomeLocators.Replace("password", "STALE_SELECTOR");
        WriteExisting("LocatorRepository/HomePage.locators.json", existing);

        var fresh = FreshHomeLocators.Replace("PasswordB", "PasswordA");
        var candidate = new Dictionary<string, string> { ["LocatorRepository/HomePage.locators.json"] = fresh };
        var merged = PageObjectMerger.MergeWithExisting(candidate, _root);
        var mergedContent = merged["LocatorRepository/HomePage.locators.json"];

        Assert.That(mergedContent, Does.Contain("\"value\": \"password\""));
        Assert.That(mergedContent, Does.Not.Contain("STALE_SELECTOR"));
    }

    [Test]
    public void MergeWithExisting_LocatorFile_NewPageWithNoExistingFile_IsUnchanged()
    {
        var candidate = new Dictionary<string, string> { ["LocatorRepository/BrandNewPage.locators.json"] = FreshHomeLocators };
        var merged = PageObjectMerger.MergeWithExisting(candidate, _root);

        Assert.That(merged["LocatorRepository/BrandNewPage.locators.json"], Is.EqualTo(FreshHomeLocators));
    }

    private static int CountOccurrences(string haystack, string needle)
    {
        var count = 0;
        var index = 0;
        while ((index = haystack.IndexOf(needle, index, StringComparison.Ordinal)) >= 0)
        {
            count++;
            index += needle.Length;
        }
        return count;
    }
}
