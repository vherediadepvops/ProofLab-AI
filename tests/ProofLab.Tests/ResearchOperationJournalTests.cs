using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.FileProviders;
using ProofLab.Api;
namespace ProofLab.Tests;
public sealed class ResearchOperationJournalTests:IDisposable {
    private readonly string root=Path.Combine(Path.GetTempPath(),"prooflab-recovery-"+Guid.NewGuid().ToString("N"));
    private sealed class Env(string root):IWebHostEnvironment {
        public string ContentRootPath{get;set;}=root;public string WebRootPath{get;set;}=root;
        public string EnvironmentName{get;set;}="Test";public string ApplicationName{get;set;}="Test";
        public IFileProvider ContentRootFileProvider{get;set;}=new NullFileProvider();
        public IFileProvider WebRootFileProvider{get;set;}=new NullFileProvider();
    }
    [Fact]
    public async Task Browser_disconnect_keeps_one_submitted_stage_alive_and_duplicate_id_never_repeats_it()
    {
        var journal=new ResearchOperationJournal(new Env(root));using var browser=new CancellationTokenSource();
        var context=new Microsoft.AspNetCore.Http.DefaultHttpContext();context.RequestAborted=browser.Token;
        var study=new string('a',32);var id=new string('f',32);context.Request.Headers["X-ProofLab-Operation-Id"]=id;
        var calls=0;
        await ResearchOperationExecution.RunAsync(context,async()=>{
            calls++;browser.Cancel();Assert.False(context.RequestAborted.IsCancellationRequested);
            Assert.Equal("RUNNING",Assert.Single(await journal.ReadAsync(study,CancellationToken.None)).State);
            await Task.Delay(5,context.RequestAborted);context.Response.StatusCode=200;
        },journal,study,"baseline","sam/manual-one-shot",CancellationToken.None);
        Assert.True(context.RequestAborted.IsCancellationRequested);
        Assert.Equal("COMPLETED",Assert.Single(await journal.ReadAsync(study,CancellationToken.None)).State);
        await ResearchOperationExecution.RunAsync(context,()=>{calls++;return Task.CompletedTask;},journal,study,"baseline","sam/manual-one-shot",CancellationToken.None);
        Assert.Equal(409,context.Response.StatusCode);Assert.Equal(1,calls);
    }

    [Fact]
    public async Task Server_shutdown_remains_cancelable_and_records_an_unresolved_operation()
    {
        var journal=new ResearchOperationJournal(new Env(root));using var stopping=new CancellationTokenSource();
        var context=new Microsoft.AspNetCore.Http.DefaultHttpContext();var study=new string('b',32);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(()=>ResearchOperationExecution.RunAsync(context,async()=>{
            stopping.Cancel();await Task.Delay(100,context.RequestAborted);
        },journal,study,"followup","sam-adaptive/manual-batch",stopping.Token));
        Assert.Equal("REVIEW_REQUIRED",Assert.Single(await journal.ReadAsync(study,CancellationToken.None)).State);
    }

    [Fact] public async Task ReloadKeepsActiveAndCompletedReceiptsWithoutExecutingWork() {
        var study=new string('a',32);var id=new string('b',32);
        var first=new ResearchOperationJournal(new Env(root));var operation=await first.BeginAsync(study,"followup","sam/manual-one-shot",id);
        var reloaded=new ResearchOperationJournal(new Env(root));
        Assert.Equal("RUNNING",Assert.Single(await reloaded.ReadAsync(study,default)).State);
        await first.FinishAsync(operation,true);
        var saved=Assert.Single(await reloaded.ReadAsync(study,default));
        Assert.Equal("COMPLETED",saved.State);Assert.Equal("followup",saved.Role);
        await Assert.ThrowsAsync<IOException>(()=>reloaded.BeginAsync(study,"followup","sam/manual-one-shot",id));
        Assert.Empty(await reloaded.ReadAsync(new string('c',32),default));
    }
    [Fact] public async Task InterruptedAndFailedRequestsAreNeverReportedAsSuccessful() {
        var store=new ResearchOperationJournal(new Env(root));var op=await store.BeginAsync(new string('a',32),"baseline","assembly/grounding",new string('d',32));
        Assert.Equal("INTERRUPTED",ResearchOperationJournal.EffectiveState(op with {ProcessStartTicks=0}));
        Assert.Equal("INTERRUPTED",ResearchOperationJournal.EffectiveState(op with {DeadlineUtc=DateTimeOffset.UtcNow.AddSeconds(-1)}));
        await store.FinishAsync(op,false);
        Assert.Equal("REVIEW_REQUIRED",Assert.Single(await store.ReadAsync(op.StudyId,default)).State);
    }
    [Fact] public async Task RejectsPathTraversalAndUnknownRoles() {
        var store=new ResearchOperationJournal(new Env(root));
        await Assert.ThrowsAsync<ArgumentException>(()=>store.BeginAsync("../escape","baseline","sam",new string('a',32)));
        await Assert.ThrowsAsync<ArgumentException>(()=>store.BeginAsync(new string('a',32),"other","sam",new string('b',32)));
    }
    public void Dispose(){if(Directory.Exists(root))Directory.Delete(root,true);}
}
