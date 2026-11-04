using System.Text.Json;
using System.Text.Json.Serialization;

namespace ProofLab.Api;

public sealed class SamInitialBodyPointConfirmationRequiredException(string? sceneRunId=null,
    string? failedBatchId=null,string? newBatchId=null)
    : InvalidOperationException("SAM_INITIAL_BODY_POINT_CONFIRMATION_REQUIRED")
{
    public string? SceneRunId {get;}=sceneRunId;
    public string? FailedBatchId {get;}=failedBatchId;
    public string? NewBatchId {get;}=newBatchId;
}

// Append-only local review. This is a human choice of a visible surface pixel,
// not a provider observation, a completed tracking review or a scientific conclusion.
public sealed record SamInitialBodyPointConfirmation(
    string Id,string Protocol,string StudyId,string VideoRole,string SourceVideoId,
    string VideoSha256,string SceneRunId,string SceneKey,string SparkResponseSha256,
    string Frame0Sha256,int Width,int Height,SamScenePoint Point,
    DateTimeOffset ConfirmedAtUtc,string Sha256);

public sealed class SamInitialBodyPointConfirmationRequest
{
    public string SceneRunId {get;init;}="";
    public string VideoSha256 {get;init;}="";
    public string SceneKey {get;init;}="";
    public string Frame0Sha256 {get;init;}="";
    public string ClientConfirmationId {get;init;}="";
    public string? Mode {get;init;}
    [JsonRequired] public int X {get;init;}
    [JsonRequired] public int Y {get;init;}
    public bool ConfirmBodyInterior {get;init;}
}

public sealed record AssemblyInitialReferenceFrames(
    string RunId,SamSceneDefinition Scene,KnotFrame InitialFrame,byte[] InitialBytes,
    KnotFrame ClearFrame,byte[] ClearBytes);

public sealed record SamInitialBodyPointConfirmationView(
    string SceneRunId,string VideoSha256,string SceneKey,string Frame0Sha256,
    int Width,int Height,SamScenePoint SuggestedPoint,
    string InitialFrameUrl,string ClearFrameUrl,int ClearFrameIndex,
    [property:JsonIgnore(Condition=JsonIgnoreCondition.WhenWritingNull)] string? ConfirmationId=null);

public static class SamInitialBodyPointPolicy
{
    public const string Protocol="HUMAN_CONFIRMED_VISIBLE_BODY_POINT_V1";
    private static readonly JsonSerializerOptions Json=new(JsonSerializerDefaults.Web);

    // BODY is an AI assertion. A partially hidden initial outline still requires
    // a human-selected pixel even when V5 called its proposed pixel BODY.
    public static bool RequiresConfirmation(SamSceneDefinition? scene)=>
        scene?.InitialBodyPoint is not null &&
        scene.BodyReferences?.FirstOrDefault(q=>q.Frame==0) is not
            {WholeBodyVisible:true,Occluded:false,Clipped:false};

    public static string Digest(SamInitialBodyPointConfirmation review)=>
        SamVideoArchive.Hash(JsonSerializer.Serialize(review with{Sha256=""},Json));

    public static void Validate(SamInitialBodyPointConfirmation review,SamSceneDefinition scene)
    {
        static bool Hex(string value,int length)=>value is not null && value.Length==length &&
            value.All(c=>c is >= '0' and <= '9' or >= 'a' and <= 'f');
        if(review.Protocol!=Protocol || !Hex(review.Id,32) || !Hex(review.SceneRunId,32) ||
           !Hex(review.Frame0Sha256,64) || !Hex(review.Sha256,64) ||
           review.Sha256!=Digest(review) || string.IsNullOrWhiteSpace(review.StudyId) ||
           review.VideoRole is not ("baseline" or "followup") || string.IsNullOrWhiteSpace(review.SourceVideoId) ||
           review.VideoSha256!=scene.VideoSha256 || review.SceneKey!=scene.Key ||
           review.SparkResponseSha256!=scene.ResponseSha256 ||
           review.Width!=scene.Width || review.Height!=scene.Height ||
           scene.InitialBodyPoint is null || review.Point is null || review.Point.Frame!=0 ||
           review.Point.X<0 || review.Point.Y<0 || review.Point.X>=scene.Width || review.Point.Y>=scene.Height)
            throw new InvalidDataException("Human initial-reference provenance or coordinates are inconsistent.");
    }
}
