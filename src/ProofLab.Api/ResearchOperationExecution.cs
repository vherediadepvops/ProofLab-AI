using Microsoft.AspNetCore.Http;
namespace ProofLab.Api;

public static class ResearchOperationExecution
{
    // The submitted stage owns its bounded lifetime. Browser loss does not
    // become a provider retry; shutdown remains an explicit interruption.
    public static async Task RunAsync(HttpContext context, Func<Task> next,
        ResearchOperationJournal journal, string study, string role, string stage,
        CancellationToken applicationStopping)
    {
        var id = context.Request.Headers["X-ProofLab-Operation-Id"].FirstOrDefault() ?? Guid.NewGuid().ToString("N");
        ResearchOperation operation;
        try { operation = await journal.BeginAsync(study, role, stage, id); }
        catch (ArgumentException) { context.Response.StatusCode = 400; return; }
        catch (IOException) { context.Response.StatusCode = 409; return; }
        // Keep the one explicit request alive after a browser disconnect. This
        // does not schedule the next stage, retry, or issue a provider request itself.
        var original = context.RequestAborted; using var deadline = CancellationTokenSource.CreateLinkedTokenSource(applicationStopping);
        deadline.CancelAfter(TimeSpan.FromMinutes(70)); context.RequestAborted = deadline.Token;
        var completed = false;
        try { await next(); completed = context.Response.StatusCode is >= 200 and < 300; }
        finally { context.RequestAborted = original; await journal.FinishAsync(operation, completed); }
    }
}
