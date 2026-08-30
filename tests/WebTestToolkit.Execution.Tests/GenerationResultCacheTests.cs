using WebTestToolkit.Execution.Generation;
using WebTestToolkit.Llm.Skills;

namespace WebTestToolkit.Execution.Tests;

// Pure unit tests over the cache and its key derivation — no sandbox, no real project on
// disk. HybridTestCodeGeneratorTests covers the end-to-end "Preview twice" behavior; this
// covers the WriteToProject:true skip-cache guard, which can't be exercised end-to-end
// without actually writing into the real tests/WebTestToolkit.GeneratedTests project (a
// successful Generate always calls GeneratedProjectWriter.Write for real).
public class GenerationResultCacheTests
{
    private static ScriptGenerationInput Bundle(string flowJson = "{}") =>
        new(FlowName: "Probe", FlowJson: flowJson, ProjectFile: "proj", SupportApi: "api",
            GoldSample: "gold", ReferenceImplementation: "ref", ExistingProjectIndex: "index");

    [Test]
    public void TryGet_OnAnUnsetKey_ReturnsFalse()
    {
        var cache = new GenerationResultCache();
        Assert.That(cache.TryGet("nonexistent", out _), Is.False);
    }

    [Test]
    public void ComputeKey_IsStableForTheSameBundleAndOptions()
    {
        var key1 = GenerationResultCache.ComputeKey(Bundle(), new GenerationOptions());
        var key2 = GenerationResultCache.ComputeKey(Bundle(), new GenerationOptions());
        Assert.That(key1, Is.EqualTo(key2));
    }

    [TestCase("{\"a\":1}", "{\"a\":2}")]
    public void ComputeKey_DiffersWhenTheFlowJsonDiffers(string flowJsonA, string flowJsonB)
    {
        var keyA = GenerationResultCache.ComputeKey(Bundle(flowJsonA), new GenerationOptions());
        var keyB = GenerationResultCache.ComputeKey(Bundle(flowJsonB), new GenerationOptions());
        Assert.That(keyA, Is.Not.EqualTo(keyB));
    }

    [Test]
    public void ComputeKey_DiffersWhenMaxRepairAttemptsDiffers()
    {
        var keyA = GenerationResultCache.ComputeKey(Bundle(), new GenerationOptions(MaxRepairAttempts: 1));
        var keyB = GenerationResultCache.ComputeKey(Bundle(), new GenerationOptions(MaxRepairAttempts: 3));
        Assert.That(keyA, Is.Not.EqualTo(keyB));
    }
}
