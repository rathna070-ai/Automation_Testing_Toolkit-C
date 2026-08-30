namespace WebTestToolkit.Contracts.Models;

public enum ActionType
{
    Navigate,
    Click,
    Type,

    // A <select>. Distinct from Type because the two are not interchangeable at runtime:
    // Type emits Clear()+SendKeys(), and Clear() on a non-editable element is "invalid
    // element state" per the WebDriver spec — it throws. A dropdown needs SelectElement.
    Select,

    AssertText,
    AssertVisible
}
