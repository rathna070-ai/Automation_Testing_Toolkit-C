Feature: Login
  As a user of the practice login site
  I want to log in with valid credentials
  So that I can reach the secure area

  Scenario: Successful login with valid credentials
    Given I am on the login page
    When I enter username "tomsmith" and password "SuperSecretPassword!"
    And I click the login button
    Then I should see a success message

  Scenario: Failed login with invalid credentials
    Given I am on the login page
    When I enter username "tomsmith" and password "wrong-password"
    And I click the login button
    Then I should see an error message
