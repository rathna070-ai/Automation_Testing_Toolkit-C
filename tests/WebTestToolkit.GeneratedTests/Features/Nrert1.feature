# Recorded against the live saucedemo.com, so this scenario's result depends on a
# third-party site being up — the same dependency P16 item 6 removed from the hand-written
# sample suite. Tagged so a plain CI run skips it (see .github/workflows/ci.yml), while
# `dotnet test --filter "Category=liveSite"` still runs it locally on demand — the Gherkin
# equivalent of the [Explicit] attribute InspectorSessionBrowserTests already uses.
@liveSite
Feature: nrert 1

  Scenario: nrert 1 flow
    Given I open the home page
    When I click the user name
    And I enter the user name "standard_user"
    And I click the password
    And I click the password (2)
    And I enter the password "secret_sauce"
    And I click the login button button
    And I click the sauce labs backpackcarry all the things
    And I click the a link
