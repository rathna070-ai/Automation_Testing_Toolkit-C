using Microsoft.Extensions.Logging.Abstractions;
using WebTestToolkit.Api.Services;
using WebTestToolkit.Contracts.Models;

namespace WebTestToolkit.Api.Tests;

// Every test points the store at a throwaway temp directory rather than the real
// %AppData%\WebTestToolkit\flows — the same discipline LocatorJsonPatcherTests uses, and the
// reason FlowStore takes its base directory as a parameter at all.
public class FlowStoreTests
{
    private string _root = "";
    private FlowStore _store = null!;

    [SetUp]
    public void SetUp()
    {
        _root = Path.Combine(Path.GetTempPath(), "wtt-flowstore-tests-" + Guid.NewGuid().ToString("N"));
        _store = new FlowStore(NullLogger<FlowStore>.Instance, _root);
    }

    [TearDown]
    public void TearDown()
    {
        if (Directory.Exists(_root))
            Directory.Delete(_root, recursive: true);
    }

    private static TestFlow SampleFlow(string name = "Checkout") => new()
    {
        Name = name,
        StartUrl = "https://example.com/checkout",
        Steps =
        [
            new TestStep { Order = 1, ActionType = ActionType.Navigate, Label = "I open checkout", PageName = "CheckoutPage" },
            new TestStep
            {
                Order = 2,
                ActionType = ActionType.Select,
                Label = "I choose the country dropdown",
                InputValue = "India",
                PageName = "CheckoutPage",
                LocatorKey = "CountryDropdown",
                Element = new CapturedElement
                {
                    TagName = "select",
                    Id = "country",
                    Candidates = [new LocatorCandidate("id", "country", 100)],
                    Options = [new SelectOption("in", "India", true)]
                }
            }
        ]
    };

    // The whole point of P19: a recording has to survive the process that made it.
    [Test]
    public async Task SavedFlow_RoundTripsWithEveryFieldIntact()
    {
        await _store.SaveAsync(SampleFlow());

        // A completely separate instance, as if the API had restarted.
        var reloaded = await new FlowStore(NullLogger<FlowStore>.Instance, _root).GetAsync("Checkout");

        Assert.That(reloaded, Is.Not.Null);
        Assert.Multiple(() =>
        {
            Assert.That(reloaded!.Name, Is.EqualTo("Checkout"));
            Assert.That(reloaded.StartUrl, Is.EqualTo("https://example.com/checkout"));
            Assert.That(reloaded.Steps, Has.Count.EqualTo(2));

            // ActionType is an enum over a camelCase converter — a round trip that silently
            // degraded Select to the default would regenerate the dropdown as a text input.
            Assert.That(reloaded.Steps[1].ActionType, Is.EqualTo(ActionType.Select));
            Assert.That(reloaded.Steps[1].InputValue, Is.EqualTo("India"));
            Assert.That(reloaded.Steps[1].Element!.Candidates.Single().Value, Is.EqualTo("country"));
            Assert.That(reloaded.Steps[1].Element!.Options!.Single().Text, Is.EqualTo("India"));
        });
    }

    [Test]
    public async Task Get_ForAnUnknownFlow_ReturnsNull()
    {
        Assert.That(await _store.GetAsync("never-recorded"), Is.Null);
    }

    [Test]
    public async Task List_ReturnsSummariesNewestFirst()
    {
        await _store.SaveAsync(SampleFlow("Older"));
        await Task.Delay(1100); // file timestamps have second-level granularity on some filesystems
        await _store.SaveAsync(SampleFlow("Newer"));

        var listed = await _store.ListAsync();

        Assert.That(listed.Select(f => f.Name), Is.EqualTo(new[] { "Newer", "Older" }).AsCollection);
        Assert.That(listed[0].StepCount, Is.EqualTo(2));
    }

    [Test]
    public async Task List_OnAnEmptyStore_ReturnsNothingRatherThanThrowing()
    {
        Assert.That(await _store.ListAsync(), Is.Empty);
    }

    [Test]
    public async Task Save_OverwritesTheSameNameRatherThanAccumulating()
    {
        await _store.SaveAsync(SampleFlow());
        var updated = SampleFlow();
        updated.Steps.RemoveAt(1);
        await _store.SaveAsync(updated);

        Assert.That(await _store.ListAsync(), Has.Count.EqualTo(1));
        Assert.That((await _store.GetAsync("Checkout"))!.Steps, Has.Count.EqualTo(1));
    }

    [Test]
    public async Task Delete_RemovesTheFlow_AndReportsWhetherThereWasOne()
    {
        await _store.SaveAsync(SampleFlow());

        Assert.That(await _store.DeleteAsync("Checkout"), Is.True);
        Assert.That(await _store.GetAsync("Checkout"), Is.Null);
        Assert.That(await _store.DeleteAsync("Checkout"), Is.False, "Deleting twice is not an error, just false.");
    }

    // The flow name is free text a user typed and it becomes a filename, so it is the one
    // untrusted input reaching the filesystem here.
    [Test]
    public async Task FlowNameWithPathSeparators_StaysInsideTheStore()
    {
        var flow = SampleFlow("../../escaped");
        await _store.SaveAsync(flow);

        var written = Directory.GetFiles(_root, "*.json", SearchOption.AllDirectories);
        Assert.That(written, Has.Length.EqualTo(1));
        Assert.That(Path.GetFullPath(written[0]), Does.StartWith(Path.GetFullPath(_root)));
    }

    [Test]
    public async Task FlowNameWithSpacesAndPunctuation_IsStillRetrievable()
    {
        await _store.SaveAsync(SampleFlow("flow new 1"));
        Assert.That(await _store.GetAsync("flow new 1"), Is.Not.Null);
    }

    [Test]
    public void Save_WithoutAName_IsRejected()
    {
        var flow = SampleFlow("");
        Assert.ThrowsAsync<ArgumentException>(() => _store.SaveAsync(flow));
    }

    // One corrupt file must not take out the listing for every other saved flow.
    [Test]
    public async Task List_SkipsAnUnreadableFile()
    {
        await _store.SaveAsync(SampleFlow("Good"));
        await File.WriteAllTextAsync(Path.Combine(_root, "Broken.json"), "{ not json");

        var listed = await _store.ListAsync();

        Assert.That(listed.Select(f => f.Name), Is.EqualTo(new[] { "Good" }).AsCollection);
    }
}
