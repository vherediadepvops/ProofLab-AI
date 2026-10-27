using System.Diagnostics;
using System.Text.Json;
using ProofLab.Science;

namespace ProofLab.Api;

/// <summary>
/// Mission 1: a constrained Lean 4 verifier. It NEVER executes arbitrary Lean
/// source supplied by API callers or AI models. Only fixed, curated statements
/// and fixed strategies from MathResearchCatalog can reach the compiler.
/// </summary>
public sealed class LeanProofVerifier(IWebHostEnvironment hosting)
{
    private static readonly SemaphoreSlim SingleCompiler = new(1, 1);

    public string? FindLean()
    {
        var configured = Environment.GetEnvironmentVariable("PROOFLAB_LEAN_PATH");
        if (!string.IsNullOrWhiteSpace(configured) &&
            Path.IsPathFullyQualified(configured) && File.Exists(configured))
            return configured;

        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        var elan = Path.Combine(home, ".elan", "bin",
            OperatingSystem.IsWindows() ? "lean.exe" : "lean");
        if (File.Exists(elan)) return elan;

        // Windows winget installs the elan executable aliases in WinGet/Links.
        if (OperatingSystem.IsWindows())
        {
            var wingetLean = Path.Combine(home, "AppData", "Local",
                "Microsoft", "WinGet", "Links", "lean.exe");
            if (File.Exists(wingetLean)) return wingetLean;
        }

        var name = OperatingSystem.IsWindows() ? "lean.exe" : "lean";
        return (Environment.GetEnvironmentVariable("PATH") ?? "")
            .Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries)
            .Select(segment => Path.Combine(segment, name))
            .FirstOrDefault(File.Exists);
    }

    public async Task<MathVerificationResult> VerifyAsync(
        MathVerificationRequest attempt, CancellationToken cancellationToken)
    {
        var problem = MathResearchCatalog.Find(attempt.ProblemId);
        var source = MathResearchCatalog.BuildLeanSource(problem, attempt.Strategy);
        var attemptId = Guid.NewGuid().ToString("N");
        var digest = MathResearchCatalog.Sha256(source);
        var time = DateTimeOffset.UtcNow;
        var executable = FindLean();

        MathVerificationResult Result(string outcome, string diagnostic) =>
            new(attemptId, problem.Id, attempt.Strategy, outcome,
                "Lean 4 kernel, constrained strategy, known benchmark",
                digest, time, diagnostic.Length > 2500 ? diagnostic[..2500] : diagnostic,
                IsNewDiscovery: false);

        if (executable is null)
            return Result("UNAVAILABLE", "Lean 4 is not installed or could not be located.");

        await SingleCompiler.WaitAsync(cancellationToken);
        var workingDir = Path.Combine(Path.GetTempPath(), "prooflab-math", attemptId);
        try
        {
            Directory.CreateDirectory(workingDir);
            var leanFile = Path.Combine(workingDir, "Attempt.lean");
            await File.WriteAllTextAsync(leanFile, source, cancellationToken);
            var start = new ProcessStartInfo(executable)
            {
                WorkingDirectory = workingDir,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true
            };
            start.ArgumentList.Add("Attempt.lean");

            using var proc = new Process { StartInfo = start };
            try
            {
                if (!proc.Start())
                    return Result("UNAVAILABLE", "Lean process could not be started.");
                var stdout = proc.StandardOutput.ReadToEndAsync();
                var stderr = proc.StandardError.ReadToEndAsync();
                using var limit = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                limit.CancelAfter(TimeSpan.FromSeconds(25));
                try
                {
                    await proc.WaitForExitAsync(limit.Token);
                }
                catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
                {
                    if (!proc.HasExited) proc.Kill(entireProcessTree: true);
                    return Result("TIMED_OUT", "Lean verification exceeded 25 seconds.");
                }
                // Drain process output after exit to avoid pipe blocking.
                var diagnostic = (await stdout + "\n" + await stderr).Trim();
                return proc.ExitCode == 0
                    ? Result("VERIFIED", "Lean accepted the fixed theorem and proof strategy. " +
                        "This is a known theorem, not a novel discovery.")
                    : Result("REJECTED", string.IsNullOrWhiteSpace(diagnostic)
                        ? "Lean rejected this proof strategy." : diagnostic);
            }
            catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or IOException)
            {
                return Result("UNAVAILABLE", "Lean could not run: " + ex.Message);
            }
        }
        finally
        {
            try { Directory.Delete(workingDir, recursive: true); } catch (IOException) { }
            catch (UnauthorizedAccessException) { }
            SingleCompiler.Release();
        }
    }

    public async Task RecordAsync(MathVerificationResult result, CancellationToken ct)
    {
        // Append-only private local audit file; do not commit user investigation records.
        var directory = Path.Combine(hosting.ContentRootPath, ".private", "math");
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, "verification-attempts.jsonl");
        await File.AppendAllTextAsync(path,
            JsonSerializer.Serialize(result) + Environment.NewLine, ct);
    }
}
