Feature: signup

  Scenario: Choosing a country from the dropdown
    Given I open the signup page
    When I choose the country dropdown "India"
    Then I should see the chosen message
