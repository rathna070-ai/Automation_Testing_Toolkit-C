# Recorded against live saucedemo.com, so this scenario's result depends on a third-party site
# being up — the same dependency P16 item 6 removed from the hand-written sample suite.
# Tagged so a plain CI run skips it (see .github/workflows/ci.yml), while
# `dotnet test --filter "Category=liveSite"` still runs it locally on demand — the Gherkin
# equivalent of the [Explicit] attribute InspectorSessionBrowserTests already uses.
#
# Added by hand, as for every other recorded flow here: the generator does not emit this tag,
# so a live recording lands in the unattended run until someone remembers to add it.
@liveSite
Feature: flow new 1

  Scenario: flow new 1 flow
    Given I open the home page
    When I click the user name
    And I enter the user name "standard_user"
    And I click the password
    And I enter the password "secret_sauce"
    And I click the login button
    And I click the sauce labs backpackcarry all the things
    And I click the $29.99
    And I click the add to cart sauce labs backpack button
    And I click the products name ato zname ato zname
    And I click the 1 link
    And I click the cart contents container
    And I click the checkout button
    And I click the first name
    And I enter the first name "sfs"
    And I click the last name
    And I enter the last name "sfs"
    And I click the postal code
    And I enter the postal code "1212"
    And I click the continue button
    And I click the total3239
    And I click the $29.99 (2)
    And I click the swag labs
    And I click the 1 link (2)
    And I click the continue shopping button
    And I click the add to cart sauce labs bike light button
    And I click the 2 link
    And I click the cart contents container (2)
    And I click the checkout button (2)
    And I click the postal code (2)
    And I click the last name (2)
    And I click the first name (2)
    And I click the div
    And I click the continue button (2)
    And I click the total4318
    And I click the finish button
    And I click the checkout complete container
    And I click the img
