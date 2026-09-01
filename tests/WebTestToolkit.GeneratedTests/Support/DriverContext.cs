using OpenQA.Selenium;
using OpenQA.Selenium.Chrome;

namespace WebTestToolkit.GeneratedTests.Support;

// Reqnroll context-injection: one instance of this class is shared across all
// bindings within a single scenario, so the browser session survives across steps.
public class DriverContext : IDisposable
{
    private IWebDriver? _driver;

    public IWebDriver Driver => _driver ??= CreateDriver();

    private static IWebDriver CreateDriver()
    {
        var options = new ChromeOptions();
        options.AddArgument("--start-maximized");
        ApplyPopupSuppression(options);

        try
        {
            return new ChromeDriver(options);
        }
        catch (Exception ex)
        {
            // Almost always Chrome not being installed, or Selenium Manager unable to reach
            // the network to fetch a matching driver on first run — the same failure mode
            // InspectController.Start already gives an actionable message for. Left as the
            // raw WebDriverException/Win32Exception here, this surfaces in a test run as an
            // opaque stack trace with no hint that the fix is "install Chrome" or "check your
            // network", not "the generated test is broken".
            throw new InvalidOperationException(
                "Could not start Chrome for this test run — Chrome may not be installed, or " +
                "Selenium Manager could not reach the network to fetch a matching driver. " +
                $"Original error: {ex.Message}", ex);
        }
    }

    // --- Browser popup suppression -------------------------------------------------------
    //
    // Chrome's own dialogs are browser chrome, not page DOM: Selenium cannot see or dismiss
    // them, so they must be prevented rather than handled. One of them blocked a real
    // recording — "Change your password: the password you just used was found in a data
    // breach", which Google Password Manager raises for well-known test credentials like
    // saucedemo's. Note that leak detection is a *separate* preference from the two obvious
    // password ones, which is exactly why setting those was not enough.
    //
    // This same set is duplicated in backend/WebTestToolkit.Inspector/InspectorSession.cs,
    // because this project deliberately has zero project references to the toolkit and so
    // cannot share a class. ChromeOptionsParityTests reads both files and fails if they
    // drift — without it, you would record in a hardened browser and replay in a bare one,
    // which is how a suppressed dialog reappears mid-run and fails a test somewhere
    // unrelated to its cause. Until this was added, that was literally the case: the
    // Inspector applied six options here and this file applied one.
    private static void ApplyPopupSuppression(ChromeOptions options)
    {
        // Password manager: save prompt, the manager itself, and the data-breach warning.
        options.AddUserProfilePreference("credentials_enable_service", false);
        options.AddUserProfilePreference("profile.password_manager_enabled", false);
        options.AddUserProfilePreference("profile.password_manager_leak_detection", false);

        // Autofill: "Save address?" and "Save card?".
        options.AddUserProfilePreference("autofill.profile_enabled", false);
        options.AddUserProfilePreference("autofill.credit_card_enabled", false);

        // 2 = block, so a site asking for notifications never raises a permission prompt.
        options.AddUserProfilePreference("profile.default_content_setting_values.notifications", 2);

        options.AddArgument("--disable-popup-blocking");
        options.AddArgument("--no-first-run");
        options.AddArgument("--no-default-browser-check");

        // "Restore pages?" — more likely here than in a normal browser, because the P16 Job
        // Object *kills* Chrome on an unclean API exit rather than closing it, which Chrome
        // then reads as a crash on the next launch.
        options.AddArgument("--disable-session-crashed-bubble");
        options.AddArgument("--hide-crash-restore-bubble");

        // The EU search-engine choice screen, which a fresh profile can show over the page.
        options.AddArgument("--disable-search-engine-choice-screen");

        // A page's own alert()/confirm() is NOT suppressed — that is application behaviour,
        // not browser chrome. Ignore only stops the driver throwing on the *next unrelated*
        // command; the dialog is still there and still reported. Deliberately not Accept or
        // Dismiss: silently answering a confirm("Delete everything?") would let a test pass
        // through a step it never actually took.
        options.UnhandledPromptBehavior = UnhandledPromptBehavior.Ignore;
    }

    public void Dispose()
    {
        _driver?.Quit();
        _driver?.Dispose();
        _driver = null;
    }
}
