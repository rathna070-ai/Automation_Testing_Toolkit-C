using System.Text.Json;
using OpenQA.Selenium;
using OpenQA.Selenium.Chrome;
using WebTestToolkit.Contracts.Models;
using WebTestToolkit.Inspector.Capture;
using WebTestToolkit.Inspector.Overlay;

namespace WebTestToolkit.Inspector;

// One inspect session = one Chrome window the user drives by hand while we watch.
//
// Two rules govern everything here:
//
// 1. WebDriver is not thread-safe, and this object is reached from both HTTP requests and
//    the polling background service. Every driver touch goes through _gate.
// 2. The page under test is hostile until proven otherwise. Anything coming back from the
//    browser is untrusted data — it is parsed into typed records, never eval'd, and it
//    never decides a file path.
public sealed class InspectorSession : IDisposable
{
    // Must match `var VERSION` in Overlay/inspector-overlay.js. OverlayVersionMatchesScript
    // in the test project asserts that, because a silent mismatch would mean re-injecting
    // the overlay on every single poll — capture would still work, just very slowly.
    internal const int OverlayVersion = 3;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly StepLabeler _labeler = new();
    private readonly List<InspectorEvent> _events = new();
    private readonly IWebDriver _driver;

    private int _sequence;
    private string _lastUrl;

    public string Id { get; }
    public string Name { get; }
    public string StartUrl { get; }
    public DateTimeOffset StartedUtc { get; }
    public DateTimeOffset LastActivityUtc { get; private set; }
    public InspectorSessionState State { get; private set; }
    public string? FaultReason { get; private set; }

    private InspectorSession(string id, string name, string startUrl, IWebDriver driver)
    {
        Id = id;
        Name = name;
        StartUrl = startUrl;
        _driver = driver;
        _lastUrl = startUrl;
        StartedUtc = DateTimeOffset.UtcNow;
        LastActivityUtc = StartedUtc;
        State = InspectorSessionState.Running;
    }

    public static async Task<InspectorSession> StartAsync(InspectorStartRequest request, CancellationToken ct)
    {
        if (!Uri.TryCreate(request.StartUrl, UriKind.Absolute, out var uri) ||
            (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
        {
            throw new ArgumentException($"'{request.StartUrl}' is not an http(s) URL.", nameof(request));
        }

        var id = Guid.NewGuid().ToString("N")[..12];

        // Launching Chrome takes seconds and blocks; never do it on the request thread.
        var driver = await Task.Run(() => CreateDriver(request.Headless), ct);

        var session = new InspectorSession(id, request.Name, request.StartUrl, driver);

        try
        {
            await Task.Run(() =>
            {
                driver.Navigate().GoToUrl(request.StartUrl);
                session.InjectOverlay();
            }, ct);
        }
        catch
        {
            driver.Quit();
            driver.Dispose();
            throw;
        }

        // The flow has to start somewhere. This is the only Navigate step the toolkit
        // invents; later navigations are inferred from what the user actually did.
        var pageName = StepLabeler.PageNameFromUrl(request.StartUrl);
        session._events.Add(new InspectorEvent
        {
            SessionId = id,
            Sequence = ++session._sequence,
            ActionType = ActionType.Navigate,
            Url = request.StartUrl,
            PageName = pageName,
            SuggestedLabel = StepLabeler.NavigateLabel(pageName)
        });

        return session;
    }

    private static IWebDriver CreateDriver(bool headless)
    {
        var options = new ChromeOptions();
        if (headless)
            options.AddArgument("--headless=new");
        else
            options.AddArgument("--start-maximized");

        // The user is going to hand-drive this window; a popup blocker or a "restore pages?"
        // bubble in the way is pure friction.
        options.AddArgument("--disable-popup-blocking");
        options.AddArgument("--no-first-run");
        options.AddArgument("--no-default-browser-check");
        options.AddUserProfilePreference("credentials_enable_service", false);
        options.AddUserProfilePreference("profile.password_manager_enabled", false);

        var service = ChromeDriverService.CreateDefaultService();
        service.HideCommandPromptWindow = true;

        return new ChromeDriver(service, options);
    }

    // ---------------------------------------------------------------- polling

    // Called on a timer by the broadcast service. Returns only what is new since last call,
    // so the caller can push exactly those to connected clients.
    public async Task<IReadOnlyList<InspectorEvent>> PollAsync(CancellationToken ct)
    {
        if (State is not (InspectorSessionState.Running or InspectorSessionState.Paused))
            return Array.Empty<InspectorEvent>();

        await _gate.WaitAsync(ct);
        try
        {
            if (State is not (InspectorSessionState.Running or InspectorSessionState.Paused))
                return Array.Empty<InspectorEvent>();

            return await Task.Run(() => PollCore(), ct);
        }
        catch (WebDriverException ex)
        {
            // Overwhelmingly this is "the user closed the Chrome window", which is a normal
            // way to end a session, not a crash. Record it and stop polling.
            Fault(ex.Message);
            return Array.Empty<InspectorEvent>();
        }
        finally
        {
            _gate.Release();
        }
    }

    private IReadOnlyList<InspectorEvent> PollCore()
    {
        // One round trip in the common case: drain if our overlay is live, else tell us so.
        var payload = Execute($"return (window.__wtt && window.__wtt.version === {OverlayVersion}) ? window.__wtt.drain() : null;") as string;

        if (payload is null)
        {
            // Full page load wiped the overlay (or this is a brand new document).
            InjectOverlay();
            payload = Execute("return window.__wtt.drain();") as string;
        }

        var captures = ParseCaptures(payload);
        var url = _driver.Url;

        // "fresh" is everything PollAsync's caller should see this tick — new steps AND
        // corrections to steps it already saw (a retype collapse rewrites an existing
        // event's InputValue in place). "newEvents" is the subset that actually needs
        // appending to _events; a correction was already applied in place by Convert and
        // must not be added again, or _events would carry the same step twice.
        var fresh = new List<InspectorEvent>();
        var newEvents = new List<InspectorEvent>();

        // _gate serialises polls against each other, but the UI edits and reads the same
        // list from HTTP threads at the same time — this is the only lock that covers both.
        lock (_events)
        {
            foreach (var capture in captures)
            {
                var (converted, isUpdate) = Convert(capture);
                if (converted is null)
                    continue;

                fresh.Add(converted);
                if (!isUpdate)
                    newEvents.Add(converted);
            }

            if (!string.Equals(url, _lastUrl, StringComparison.Ordinal))
            {
                // A click that caused the navigation already IS the step — adding "I open
                // the secure page" after it would make the generated test navigate directly
                // and skip the very interaction under test. Only record a navigation the
                // user performed some other way (address bar, back button, a redirect).
                if (!fresh.Any(e => e.ActionType == ActionType.Click))
                {
                    var navigation = NavigationEvent(url);
                    fresh.Add(navigation);
                    newEvents.Add(navigation);
                }

                _lastUrl = url;
            }

            if (newEvents.Count > 0)
                _events.AddRange(newEvents);

            if (fresh.Count > 0)
                LastActivityUtc = DateTimeOffset.UtcNow;
        }

        // Callers (InspectorBroadcastService) push every item here as a stepCaptured event.
        // A correction reuses its original Sequence, so a listener that upserts-by-sequence
        // (rather than blindly appending) picks up the fixed value instead of showing the
        // typo forever.
        return fresh;
    }

    private static List<RawCapture> ParseCaptures(string? payload)
    {
        if (string.IsNullOrWhiteSpace(payload))
            return new List<RawCapture>();

        try
        {
            return JsonSerializer.Deserialize<List<RawCapture>>(payload, JsonOptions) ?? new List<RawCapture>();
        }
        catch (JsonException)
        {
            // The page could have clobbered window.__wtt with something of its own. Losing a
            // batch of captures is annoying; throwing here would kill the whole session.
            return new List<RawCapture>();
        }
    }

    private InspectorEvent NavigationEvent(string url)
    {
        var pageName = StepLabeler.PageNameFromUrl(url);
        return new InspectorEvent
        {
            SessionId = Id,
            Sequence = ++_sequence,
            ActionType = ActionType.Navigate,
            Url = url,
            PageName = pageName,
            SuggestedLabel = StepLabeler.NavigateLabel(pageName)
        };
    }

    // Returns (event, isUpdate). isUpdate means the returned event replaces an existing
    // entry at the same Sequence — PollCore must not append it to _events again (Convert
    // already did, in place), but it still belongs in the broadcast batch so a live listener
    // learns about the correction instead of showing the typo forever.
    private (InspectorEvent? Event, bool IsUpdate) Convert(RawCapture capture)
    {
        var element = LocatorRanker.ToCapturedElement(capture);
        if (!element.HasLocator)
            return (null, false); // Nothing we could ever write into locator JSON.

        var actionType = capture.Kind switch
        {
            "click" => ActionType.Click,
            "input" => ActionType.Type,
            _ => (ActionType?)null
        };
        if (actionType is null)
            return (null, false);

        var pageName = StepLabeler.PageNameFromUrl(capture.Url);

        // The overlay records `change`, which fires on blur — so correcting a typo produces
        // a second event for the same field. Collapse it into the first rather than
        // generating "I enter the username" twice with different values.
        if (actionType == ActionType.Type)
        {
            var index = _events.FindLastIndex(e =>
                e.ActionType == ActionType.Type &&
                e.PageName == pageName &&
                SameElement(e.Element, element));

            if (index >= 0)
            {
                var updated = _events[index] with { InputValue = capture.Value };
                _events[index] = updated;
                return (updated, true);
            }
        }

        // Key allocation mutates the labeler, so it must happen after the collapse check —
        // otherwise a retyped field would burn "UsernameInput2" for no reason.
        var locatorKey = _labeler.LocatorKeyFor(pageName, element);

        var created = new InspectorEvent
        {
            SessionId = Id,
            Sequence = ++_sequence,
            ActionType = actionType.Value,
            Url = capture.Url,
            PageName = pageName,
            LocatorKey = locatorKey,
            SuggestedLabel = StepLabeler.ActionLabel(actionType.Value, element, capture.Value),
            InputValue = actionType == ActionType.Type ? capture.Value : null,
            Element = element,
            CapturedAtUtc = DateTimeOffset.FromUnixTimeMilliseconds(capture.At)
        };
        return (created, false);
    }

    private static bool SameElement(CapturedElement? a, CapturedElement? b)
    {
        var left = a?.BestLocator;
        var right = b?.BestLocator;
        return left is not null && right is not null &&
               left.Strategy == right.Strategy && left.Value == right.Value;
    }

    // ---------------------------------------------------------------- control

    public async Task SetCaptureEnabledAsync(bool enabled, CancellationToken ct)
    {
        await _gate.WaitAsync(ct);
        try
        {
            if (State is not (InspectorSessionState.Running or InspectorSessionState.Paused))
                return;

            await Task.Run(() =>
            {
                Execute(enabled
                    ? "if (window.__wtt) window.__wtt.enable();"
                    : "if (window.__wtt) window.__wtt.disable();");
            }, ct);

            State = enabled ? InspectorSessionState.Running : InspectorSessionState.Paused;
            LastActivityUtc = DateTimeOffset.UtcNow;
        }
        catch (WebDriverException ex)
        {
            Fault(ex.Message);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task StopAsync(CancellationToken ct)
    {
        await _gate.WaitAsync(ct);
        try
        {
            if (State is InspectorSessionState.Stopped or InspectorSessionState.Faulted)
                return;

            State = InspectorSessionState.Stopped;
            await Task.Run(QuitDriver, ct);
        }
        finally
        {
            _gate.Release();
        }
    }

    // ---------------------------------------------------------------- steps

    public IReadOnlyList<InspectorEvent> Steps
    {
        get
        {
            lock (_events)
                return _events.ToList();
        }
    }

    public bool UpdateStep(int sequence, InspectorStepEdit edit)
    {
        lock (_events)
        {
            var index = _events.FindIndex(e => e.Sequence == sequence);
            if (index < 0)
                return false;

            var current = _events[index];
            var actionType = edit.ActionType ?? current.ActionType;

            var expectedText = edit.ExpectedText ?? current.ExpectedText;
            // Switching a step to an assertion with nothing to assert on would generate a
            // Then step that verifies nothing — exactly what StaticValidator's WTT151 exists
            // to reject. Seed it from what was actually on the page.
            if (actionType == ActionType.AssertText && string.IsNullOrWhiteSpace(expectedText))
                expectedText = current.Element?.VisibleText;

            _events[index] = current with
            {
                ActionType = actionType,
                LocatorKey = string.IsNullOrWhiteSpace(edit.LocatorKey) ? current.LocatorKey : edit.LocatorKey.Trim(),
                SuggestedLabel = string.IsNullOrWhiteSpace(edit.Label) ? current.SuggestedLabel : edit.Label.Trim(),
                InputValue = edit.InputValue ?? current.InputValue,
                ExpectedText = expectedText,
                Element = SelectLocator(current.Element, edit.LocatorStrategy, edit.LocatorValue)
            };

            LastActivityUtc = DateTimeOffset.UtcNow;
            return true;
        }
    }

    // Promotes a user-chosen candidate to the top so BestLocator returns it, instead of
    // rewriting the candidate list — the alternatives stay available for auto-heal later.
    private static CapturedElement? SelectLocator(CapturedElement? element, string? strategy, string? value)
    {
        if (element is null || string.IsNullOrWhiteSpace(strategy) || string.IsNullOrWhiteSpace(value))
            return element;

        var top = element.Candidates.Count > 0 ? element.Candidates.Max(c => c.Score) : 0;
        var chosen = new LocatorCandidate(strategy.Trim(), value.Trim(), top + 1);

        element.Candidates = new[] { chosen }
            .Concat(element.Candidates.Where(c => c.Strategy != chosen.Strategy || c.Value != chosen.Value))
            .ToList();

        return element;
    }

    public bool RemoveStep(int sequence)
    {
        lock (_events)
        {
            var removed = _events.RemoveAll(e => e.Sequence == sequence) > 0;
            if (removed)
                LastActivityUtc = DateTimeOffset.UtcNow;
            return removed;
        }
    }

    // Sequence numbers have gaps once steps are deleted; TestStep.Order must not, because
    // the generators use it to order the Gherkin. Renumber on the way out.
    public TestFlow ToFlow()
    {
        lock (_events)
        {
            var flow = new TestFlow { Name = Name, StartUrl = StartUrl };
            var order = 1;

            foreach (var e in _events)
            {
                flow.Steps.Add(new TestStep
                {
                    Order = order++,
                    ActionType = e.ActionType,
                    Label = e.SuggestedLabel,
                    PageName = e.PageName,
                    LocatorKey = e.LocatorKey,
                    Element = e.Element,
                    InputValue = e.InputValue,
                    ExpectedText = e.ExpectedText
                });
            }

            return flow;
        }
    }

    public InspectorSessionInfo Describe()
    {
        return new InspectorSessionInfo
        {
            Id = Id,
            Name = Name,
            StartUrl = StartUrl,
            State = State,
            StepCount = Steps.Count,
            StartedUtc = StartedUtc,
            LastActivityUtc = LastActivityUtc,
            CurrentUrl = _lastUrl,
            FaultReason = FaultReason
        };
    }

    // ---------------------------------------------------------------- plumbing

    // Test-only seam. The browser-integration test needs to drive the page (type, click) to
    // prove the overlay actually captures those; nothing in production reaches past the
    // session's own methods, which is why this is internal rather than public.
    internal IWebDriver Driver => _driver;

    private object? Execute(string script) => ((IJavaScriptExecutor)_driver).ExecuteScript(script);

    private void InjectOverlay() => Execute(OverlayScript.Value);

    private void Fault(string reason)
    {
        State = InspectorSessionState.Faulted;
        FaultReason = reason;
        QuitDriver();
    }

    private void QuitDriver()
    {
        try
        {
            _driver.Quit();
        }
        catch (Exception)
        {
            // Quit on an already-dead browser throws; there is nothing left to clean up.
        }

        try
        {
            _driver.Dispose();
        }
        catch (Exception)
        {
        }
    }

    public void Dispose()
    {
        QuitDriver();
        _gate.Dispose();
    }
}

public sealed record InspectorStepEdit(
    ActionType? ActionType = null,
    string? Label = null,
    string? LocatorKey = null,
    string? InputValue = null,
    string? ExpectedText = null,
    string? LocatorStrategy = null,
    string? LocatorValue = null);
