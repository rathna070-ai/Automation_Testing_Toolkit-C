namespace WebTestToolkit.Inspector.Tests;

// The Inspector records in one Chrome and the generated suite replays in another. Until P24
// those two browsers were configured differently — the Inspector applied six hardening
// options, DriverContext applied one — so a dialog suppressed while recording was free to
// appear mid-run and fail a test somewhere unrelated to its cause.
//
// They cannot share a class: tests/WebTestToolkit.GeneratedTests has zero project references
// to the toolkit by design, so it stays runnable standalone by anyone who clones the repo.
// The options are therefore duplicated, and this test is what stops the duplication rotting —
// the same approach OverlayContractTests uses to pin the JS overlay's VERSION against the C#
// constant that checks it.
//
// Reading source as text is deliberate. The alternative — asserting on a constructed
// ChromeOptions — would need a reference this project must not take, and would not notice one
// file quietly losing an option the other still sets.
//
// These are substring checks, so a preference named only inside a comment would satisfy them.
// That is an accepted limit: this is a drift alarm, not a parser, and the failure it exists to
// catch is an option present in one file and absent from the other.
public class ChromeOptionsParityTests
{
    // Every setting both drivers must apply. Adding one here fails until both files carry it,
    // which is the point: the list is the contract, not either copy of the code.
    private static readonly string[] RequiredPreferences =
    [
        "credentials_enable_service",
        "profile.password_manager_enabled",
        // The data-breach dialog. A separate preference from the two above, which is why
        // setting those was not enough and a real recording got blocked.
        "profile.password_manager_leak_detection",
        "autofill.profile_enabled",
        "autofill.credit_card_enabled",
        "profile.default_content_setting_values.notifications"
    ];

    private static readonly string[] RequiredArguments =
    [
        "--disable-popup-blocking",
        "--no-first-run",
        "--no-default-browser-check",
        "--disable-session-crashed-bubble",
        "--hide-crash-restore-bubble",
        "--disable-search-engine-choice-screen"
    ];

    private static string InspectorSource() => ReadRepoFile(
        "backend/WebTestToolkit.Inspector/InspectorSession.cs");

    private static string DriverContextSource() => ReadRepoFile(
        "tests/WebTestToolkit.GeneratedTests/Support/DriverContext.cs");

    [Test]
    public void BothDrivers_SuppressEveryRequiredDialog()
    {
        var inspector = InspectorSource();
        var driverContext = DriverContextSource();

        Assert.Multiple(() =>
        {
            foreach (var preference in RequiredPreferences)
            {
                Assert.That(inspector, Does.Contain($"\"{preference}\""),
                    $"InspectorSession does not set the '{preference}' preference.");
                Assert.That(driverContext, Does.Contain($"\"{preference}\""),
                    $"DriverContext does not set the '{preference}' preference — a generated test "
                    + "would then hit a dialog the recording never saw.");
            }

            foreach (var argument in RequiredArguments)
            {
                Assert.That(inspector, Does.Contain($"\"{argument}\""),
                    $"InspectorSession does not pass {argument}.");
                Assert.That(driverContext, Does.Contain($"\"{argument}\""),
                    $"DriverContext does not pass {argument}.");
            }
        });
    }

    // Ignore, specifically. Accept or Dismiss would silently answer a page's
    // confirm("Delete everything?") and let a test pass through a step it never took.
    [Test]
    public void BothDrivers_LeaveJsDialogsVisibleRatherThanAnsweringThem()
    {
        Assert.Multiple(() =>
        {
            Assert.That(InspectorSource(), Does.Contain("UnhandledPromptBehavior.Ignore"));
            Assert.That(DriverContextSource(), Does.Contain("UnhandledPromptBehavior.Ignore"));
        });
    }

    // Guards the guard: if the option list and the two files drifted *together* — say someone
    // renamed the helper and the assertions above still passed vacuously — this notices that
    // the shared block stopped existing at all.
    [Test]
    public void BothDrivers_KeepTheSuppressionInAnAppliedHelper()
    {
        Assert.Multiple(() =>
        {
            Assert.That(InspectorSource(), Does.Contain("ApplyPopupSuppression(options)"),
                "InspectorSession defines the helper but never calls it.");
            Assert.That(DriverContextSource(), Does.Contain("ApplyPopupSuppression(options)"),
                "DriverContext defines the helper but never calls it.");
        });
    }

    // Walks up from the test assembly to the repo root. AppContext.BaseDirectory is
    // bin/Debug/net8.0, and the depth differs between a local run and CI, so anchor on a file
    // only the root has rather than counting directories.
    private static string ReadRepoFile(string relativePath)
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "WebTestToolkit.sln")))
            dir = dir.Parent;

        Assert.That(dir, Is.Not.Null, "Could not find the repository root from the test assembly.");

        var full = Path.Combine(dir!.FullName, relativePath.Replace('/', Path.DirectorySeparatorChar));
        Assert.That(File.Exists(full), Is.True, $"Expected to find {relativePath} at {full}.");
        return File.ReadAllText(full);
    }

}
