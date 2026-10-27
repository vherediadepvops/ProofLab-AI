using System.Security.Cryptography;
using System.Text;

namespace ProofLab.Science;

public sealed record GoldbachWitness(int EvenNumber, int FirstPrime, int SecondPrime);

/// <summary>
/// Bounded, deterministic computational observation, NOT a Lean formal proof.
/// </summary>
public sealed record GoldbachFiniteResult(
    string ExperimentId, string ConjectureId, DateTimeOffset CheckedAtUtc,
    int CheckedThrough, int EvenCasesExamined, int CasesWithPrimePair,
    int? FirstCaseWithoutPair, IReadOnlyList<GoldbachWitness> WitnessSamples,
    string Outcome, string AlgorithmVersion, string InputSha256, string ResultSha256,
    bool FormalProofVerified, bool IsNewDiscovery,
    string LimitationsEs, string LimitationsEn);

public sealed record GoldbachFiniteRequest(int EvenLimit);

public static class GoldbachFiniteExperiment
{
    public const int MaximumEvenLimit = 2000;
    public const string Version = "goldbach-sieve-v1";

    public static GoldbachFiniteResult Run(int evenLimit, CancellationToken cancellationToken = default)
    {
        if (evenLimit < 4 || evenLimit > MaximumEvenLimit || evenLimit % 2 != 0)
            throw new ArgumentException("evenLimit must be an even number between 4 and 2000.");

        // The sieve operates entirely on bounded integers; no Lean source, user script,
        // model output, network access, or process spawning is involved.
        var prime = new bool[evenLimit + 1];
        Array.Fill(prime, true);
        prime[0] = false;
        prime[1] = false;
        for (var p = 2; p <= evenLimit / p; p++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!prime[p]) continue;
            for (var multiple = p * p; multiple <= evenLimit; multiple += p)
                prime[multiple] = false;
        }

        var count = 0;
        var witnessed = 0;
        int? firstMissing = null;
        var samples = new List<GoldbachWitness>();
        for (var even = 4; even <= evenLimit; even += 2)
        {
            cancellationToken.ThrowIfCancellationRequested();
            count++;
            GoldbachWitness? witness = null;
            for (var p = 2; p <= even / 2; p++)
            {
                if (prime[p] && prime[even - p])
                {
                    witness = new GoldbachWitness(even, p, even - p);
                    break;
                }
            }
            if (witness is null) firstMissing ??= even;
            else
            {
                witnessed++;
                if (samples.Count < 12) samples.Add(witness);
            }
        }

        // Digest excludes timestamp and experiment ID to support independent replay.
        var input = MathResearchCatalog.Sha256(Version + "|4|" + evenLimit);
        var summary = Version + "|" + evenLimit + "|" + count + "|" + witnessed + "|"
            + (firstMissing?.ToString() ?? "none") + "|"
            + string.Join(";", samples.Select(x =>
                x.EvenNumber + "=" + x.FirstPrime + "+" + x.SecondPrime));
        var digest = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(summary)))
            .ToLowerInvariant();
        return new GoldbachFiniteResult(
            Guid.NewGuid().ToString("N"), "GOLDBACH-STRONG", DateTimeOffset.UtcNow,
            evenLimit, count, witnessed, firstMissing, samples.AsReadOnly(),
            firstMissing is null ? "NO_COUNTEREXAMPLE_IN_FINITE_RANGE"
                : "POTENTIAL_COUNTEREXAMPLE_REQUIRES_INDEPENDENT_REVIEW",
            Version, input, digest, FormalProofVerified: false, IsNewDiscovery: false,
            "Observación computacional finita, no prueba formal Lean ni demostración "
                + "de la conjetura abierta. Comparar con literatura previa.",
            "Finite computational observation, not a Lean formal proof or a "
                + "resolution of the open conjecture. Compare with prior literature.");
    }
}
