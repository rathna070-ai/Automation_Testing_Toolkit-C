# Recorded against live saucedemo.com, so this scenario's result depends on a third-party site
# being up — the same dependency P16 item 6 removed from the hand-written sample suite.
# Tagged so a plain CI run skips it (see .github/workflows/ci.yml), while
# `dotnet test --filter "Category=liveSite"` still runs it locally on demand — the Gherkin
# equivalent of the [Explicit] attribute InspectorSessionBrowserTests already uses.
@liveSite
Feature: test445

  Scenario: test445 flow
    Given I open the home page
    When I click the user name
    And I enter the user name "standard_user"
    And I click the password
    And I enter the password "secret_sauce"
    And I click the login button button
    And I click the inventory container
    And I click the sauce labs backpackcarry all the things
    And I click the _2999
    And I click the add to cart sauce labs backpack button
    And I click the _1 link
    And I click the _1 sauce labs backpackcarry all the things
    And I click the qtydescription1 sauce labs backpackcarry
