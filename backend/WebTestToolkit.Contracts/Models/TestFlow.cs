namespace WebTestToolkit.Contracts.Models;

// One captured Inspect session, ready to hand to the code generator.
// Name drives output file naming: "Login" -> Login.feature, LoginPage.cs, LoginPage.locators.json, LoginSteps.cs.
public class TestFlow
{
    public string Name { get; set; } = "";
    public string StartUrl { get; set; } = "";
    public List<TestStep> Steps { get; set; } = new();
}
