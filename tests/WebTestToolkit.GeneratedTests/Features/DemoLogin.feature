# Recorded against live the-internet.herokuapp.com, so this scenario's result depends on a third-party site
# being up — the same dependency P16 item 6 removed from the hand-written sample suite.
# Tagged so a plain CI run skips it (see .github/workflows/ci.yml), while
# `dotnet test --filter "Category=liveSite"` still runs it locally on demand — the Gherkin
# equivalent of the [Explicit] attribute InspectorSessionBrowserTests already uses.
@liveSite
Feature: DemoLogin

  Scenario: DemoLogin flow
    Given I browse to the demo login page
    When I supply the demo username "tomsmith"
    And I supply the demo password "SuperSecretPassword!"
    And I press the demo login button
    Then I should reach the demo secure area
