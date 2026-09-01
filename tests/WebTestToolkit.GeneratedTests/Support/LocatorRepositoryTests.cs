using WebTestToolkit.GeneratedTests.Support;

namespace WebTestToolkit.GeneratedTests;

// Plain NUnit tests over the suite's own Support code — no browser, no Reqnroll. They exist
// because LocatorRepository's cache is static and shared by every scenario, which only became
// safe to do once ParallelExecution.cs turned parallelism on.
public class LocatorRepositoryTests
{
    [Test]
    public void Load_IsSafeToCallConcurrently_AndCachesOneInstance()
    {
        // The pre-fix cache was a plain Dictionary mutated without a lock. Concurrent writes
        // to one of those can corrupt its buckets or spin indefinitely, which would surface as
        // a hang rather than a clean failure — so this hammers the exact race.
        var results = new PageLocators[64];

        Assert.DoesNotThrow(() =>
            Parallel.For(0, results.Length, i => results[i] = LocatorRepository.Load("LoginPage")));

        Assert.Multiple(() =>
        {
            Assert.That(results, Has.All.Not.Null);
            Assert.That(results.Distinct().Count(), Is.EqualTo(1),
                "Every caller should get the one cached instance.");
            Assert.That(results[0].Locators, Does.ContainKey("UsernameInput"));
        });
    }

    [Test]
    public void Load_ForAMissingPage_FailsLoudly()
    {
        Assert.Throws<FileNotFoundException>(() => LocatorRepository.Load("NoSuchPage"));
    }

    [Test]
    public void ToBy_RejectsAnUnsupportedStrategy()
    {
        // LocatorRepository.ToBy is what turns a JSON strategy string into a Selenium By, so
        // an unknown strategy has to fail here rather than silently locating nothing.
        Assert.Throws<NotSupportedException>(() => LocatorRepository.ToBy(new LocatorEntry("linktext", "Sign in")));
    }
}
