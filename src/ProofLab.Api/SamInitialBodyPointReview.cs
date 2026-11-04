using System.Text.Json;

namespace ProofLab.Api;

public sealed partial class SamAdaptiveLiveService
{
    private string ConfirmationPath(string id)
    {
        if(!ValidBatchId(id))throw new ArgumentException("Invalid initial-reference confirmation identity.");
        return Path.Combine(StoreRoot,"initial-references",id+".json");
    }

    private async Task<SamInitialBodyPointConfirmation> ReadConfirmationAsync(
        string studyId,string role,SamVideoSource source,SamSceneDefinition scene,
        string? sceneRunId,string id,CancellationToken ct,bool verifyFrame=true)
    {
        if(sceneRunId is null || !ValidBatchId(sceneRunId))
            throw new InvalidDataException("A human reference must be bound to its exact Spark run.");
        var review=await SamVideoArchive.ReadJsonAsync<SamInitialBodyPointConfirmation>(ConfirmationPath(id),ct);
        SamInitialBodyPointPolicy.Validate(review,scene);
        if(review.Id!=id || review.StudyId!=studyId || review.VideoRole!=role ||
           review.SourceVideoId!=source.VideoId || review.VideoSha256!=source.Sha256 ||
           review.SceneRunId!=sceneRunId)
            throw new InvalidDataException("Initial-reference confirmation belongs to another source or run.");
        if(verifyFrame){
            var frames=await scenes.InitialReferenceFramesAsync(studyId,role,sceneRunId,ct) ??
                throw new InvalidDataException("The original reference images are unavailable.");
            if(frames.Scene.Key!=scene.Key || frames.InitialFrame.Index!=0 ||
               frames.InitialFrame.Sha256!=review.Frame0Sha256 ||
               frames.InitialFrame.Width!=review.Width || frames.InitialFrame.Height!=review.Height)
                throw new InvalidDataException("The human-reviewed original frame changed.");
        }
        return review;
    }

    private async Task<SamPrincipalReference?> ResolvePrincipalAsync(
        string studyId,string role,SamVideoSource source,SamSceneDefinition scene,
        SamAdaptiveSegmentPlan plan,string? sceneRunId,string? confirmationId,
        CancellationToken ct,string? recovery=null,string? automaticParent=null)
    {
        if(confirmationId is null)return NewPrincipalReference(scene,plan,recovery,automaticParent);
        if(recovery is not null || automaticParent is not null || RequiresInitialPointReview(scene))
            throw new InvalidOperationException("This archived recovery cannot adopt a different initial reference.");
        var review=await ReadConfirmationAsync(studyId,role,source,scene,sceneRunId,confirmationId,ct);
        return SamPrincipalReferencePolicy.Confirm(scene,plan.Windows.Single(),review);
    }

    public async Task<SamAdaptiveLiveQuote> InitialReferenceQuoteAsync(
        string studyId,string role,string? mode,string sceneRunId,string? confirmationId,CancellationToken ct)
    {
        var q=await PreflightAsync(studyId,role,53,1,ct,mode,sceneRunId,confirmationId);
        if(q.InitialBodyPointConfirmation is null)
            throw new InvalidOperationException("No initial-reference review is available for this source.");
        return q;
    }

    private async Task<SamInitialBodyPointConfirmationView?> ConfirmationViewAsync(
        string studyId,string role,SamSceneDefinition scene,string? runId,string? confirmationId,CancellationToken ct)
    {
        if(runId is null || scene.InitialBodyPoint is null)return null;
        var frames=await scenes.InitialReferenceFramesAsync(studyId,role,runId,ct);
        if(frames is null)return null; // Read-only test/runtime without image capabilities.
        if(frames.RunId!=runId || frames.Scene.Key!=scene.Key || frames.InitialFrame.Index!=0 ||
           frames.InitialFrame.Width!=scene.Width || frames.InitialFrame.Height!=scene.Height)
            throw new InvalidDataException("Initial-reference metadata differs from the selected scene.");
        var route="/api/science/director/pendulum/sessions/"+Uri.EscapeDataString(studyId)+
            "/sam-adaptive/"+Uri.EscapeDataString(role)+"/initial-reference/image/";
        var query="?sceneRunId="+runId+"&videoSha256="+scene.VideoSha256+"&sceneKey="+scene.Key;
        return new(runId,scene.VideoSha256,scene.Key,frames.InitialFrame.Sha256,
            scene.Width,scene.Height,scene.InitialBodyPoint,route+"0"+query,
            route+frames.ClearFrame.Index+query,frames.ClearFrame.Index,confirmationId);
    }

    public async Task<byte[]> InitialReferenceImageAsync(string studyId,string role,string runId,
        string videoSha256,string sceneKey,int frame,CancellationToken ct)
    {
        if(!ValidBatchId(runId) || !ValidSha(videoSha256) || !ValidSha(sceneKey) || frame<0)
            throw new ArgumentException("Invalid initial-reference image identity.");
        var source=await runtime.GetVerifiedSourceAsync(studyId,role,ct);
        var frames=await scenes.InitialReferenceFramesAsync(studyId,role,runId,ct) ??
            throw new FileNotFoundException("Original reference image unavailable.");
        if(source.Sha256!=videoSha256 || frames.Scene.VideoSha256!=videoSha256 ||
           frames.RunId!=runId || frames.Scene.Key!=sceneKey)
            throw new InvalidDataException("Original reference image belongs to another source.");
        return frame==0?frames.InitialBytes:frame==frames.ClearFrame.Index?frames.ClearBytes:
            throw new ArgumentException("Only the initial and clear comparison frames are available.");
    }

    public async Task<SamAdaptiveLiveQuote> ConfirmInitialBodyPointAsync(
        string studyId,string role,SamInitialBodyPointConfirmationRequest request,CancellationToken ct)
    {
        if(!request.ConfirmBodyInterior || !ValidBatchId(request.ClientConfirmationId) ||
           !ValidBatchId(request.SceneRunId) || !ValidSha(request.VideoSha256) ||
           !ValidSha(request.SceneKey) || !ValidSha(request.Frame0Sha256) ||
           role is not ("baseline" or "followup"))
            throw new ArgumentException("Explicit visible-body selection and exact source identity are required.");
        // Local selection is independent from payment credentials and consumes no provider quota.
        await gate.WaitAsync(ct);
        try{
            var source=await runtime.GetVerifiedSourceAsync(studyId,role,ct);
            var frames=await scenes.InitialReferenceFramesAsync(studyId,role,request.SceneRunId,ct) ??
                throw new FileNotFoundException("Verified original reference frames unavailable.");
            var scene=frames.Scene;
            if(source.Sha256!=request.VideoSha256 || scene.VideoSha256!=source.Sha256 ||
               frames.RunId!=request.SceneRunId || scene.Key!=request.SceneKey ||
               frames.InitialFrame.Index!=0 || frames.InitialFrame.Sha256!=request.Frame0Sha256 ||
               frames.InitialFrame.Width!=scene.Width || frames.InitialFrame.Height!=scene.Height ||
               scene.InitialBodyPoint is null || RequiresInitialPointReview(scene))
                throw new InvalidDataException("Selected source, frame or Spark review changed.");
            var review=new SamInitialBodyPointConfirmation(request.ClientConfirmationId,
                SamInitialBodyPointPolicy.Protocol,studyId,role,source.VideoId,source.Sha256,
                request.SceneRunId,scene.Key,scene.ResponseSha256,request.Frame0Sha256,
                scene.Width,scene.Height,new(0,request.X,request.Y),DateTimeOffset.UtcNow,"");
            review=review with{Sha256=SamInitialBodyPointPolicy.Digest(review)};
            SamInitialBodyPointPolicy.Validate(review,scene);
            var path=ConfirmationPath(review.Id);
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            try{await WriteJson(path,review,CancellationToken.None);}
            catch(IOException) when(File.Exists(path)){
                var existing=await ReadConfirmationAsync(studyId,role,source,scene,
                    request.SceneRunId,review.Id,ct);
                if(existing.Point!=review.Point || existing.Frame0Sha256!=review.Frame0Sha256)
                    throw new InvalidOperationException("Confirmation identity was already used for another selection.");
            }
            return await PreflightAsync(studyId,role,53,1,ct,request.Mode,request.SceneRunId,review.Id);
        }finally{gate.Release();}
    }
}
