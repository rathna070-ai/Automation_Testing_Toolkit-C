# Recorded against the live saucedemo.com, so this scenario's result depends on a
# third-party site being up — the same dependency P16 item 6 removed from the hand-written
# sample suite. Tagged so a plain CI run skips it (see .github/workflows/ci.yml), while
# `dotnet test --filter "Category=liveSite"` still runs it locally on demand — the Gherkin
# equivalent of the [Explicit] attribute InspectorSessionBrowserTests already uses.
@liveSite
Feature: nert

  Scenario: nert flow
    Given I open the home page
    When I click the user name
    And I enter the user name "standard_user"
    And I click the password
    And I enter the password "secret_sauce"
    And I click the login button button
    And I click the epic sadface username and password do n
    And I click the path
    And I click the epic sadface username and password do n (2)
    And I click the login button button (2)
    And I click the add to cart sauce labs bike light button
    And I click the _1 link
