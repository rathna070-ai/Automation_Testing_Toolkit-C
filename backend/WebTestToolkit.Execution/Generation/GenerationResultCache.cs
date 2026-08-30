using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;
using WebTestToolkit.Contracts.Models;
using WebTestToolkit.Llm.Skills;

namespace WebTestToolkit.Execution.Generation;

// Covers the actual common case this exists for: clicking Preview twice on an unchanged
// flow. Not a general invalidation-proof cache — the key is a hash of the assembled LLM
// prompt bundle (flow JSON + support API + gold sample + existing-project index/bindings),
// which is everything ReferenceBundleBuilder shows the model and therefore everything that
// can change what it would produce. A hand-edit to some *other* PageObjects/*.cs file that
// doesn't change the existing-project index or step bindings would not bust the cache — an
// accepted gap for the same reason the item that motivated this documents it as covering the
// common case, not every case.
public class GenerationResultCache
{
    private readonly ConcurrentDictionary<string, CodeGenerationResult> _cache = new(StringComparer.Ordinal);

    public static string ComputeKey(ScriptGenerationInput bundle, GenerationOptions options)
    {
        // Every field ReferenceBundleBuilder assembled for this call, plus the two option
        // fields that change what GenerateAsync does with an otherwise-identical bundle
        // (more repair attempts can reach a result fewer wouldn't; useLlm is already implied
        // by "this method was called with a bundle" but included for clarity/future-proofing).
        var raw = string.Join('\u001f',
            bundle.FlowName, bundle.FlowJson, bundle.ProjectFile, bundle.SupportApi,
            bundle.GoldSample, bundle.ReferenceImplementation, bundle.ExistingProjectIndex,
            bundle.UntrustedPageContent ?? "", options.UseLlm, options.MaxRepairAttempts);

        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(raw));
        return Convert.ToHexString(hash);
    }

    public bool TryGet(string key, out CodeGenerationResult result) => _cache.TryGetValue(key, out result!);

    public void Set(string key, CodeGenerationResult result) => _cache[key] = result;
}
