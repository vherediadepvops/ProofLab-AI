using System.Text.Json;
using ProofLab.Api;
namespace ProofLab.Tests;

public sealed class ResearchFrozenSourceTests
{
    private const string Study = "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa";
    private const string Analysis = "bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb";
    private static MuseExperimentState Frozen(string? batch)
    {
        var payload = JsonSerializer.Serialize(new { identity = new { analysisId = Analysis, videoSha256 = "video", adaptiveBatchId = batch } });
        var observation = new MuseObservation("observation", Study, "video", Analysis, DateTimeOffset.UtcNow, payload, "hash");
        var prediction = new MusePrediction(Study, observation.Id, "response", DateTimeOffset.UtcNow, "{}", "hash");
        return new(observation, null, prediction, null, true);
    }

    [Fact]
    public void Recovery_pins_the_frozen_analysis_not_the_latest_same_video_result()
    {
        var source = ResearchFrozenSource.Resolve(Frozen(Analysis), Study, "video");
        Assert.Equal(Analysis, source!.AnalysisId);
        Assert.Equal(Analysis, source.AdaptiveBatchId);
        Assert.Null(ResearchFrozenSource.Resolve(Frozen(null), Study, "video")!.AdaptiveBatchId);
    }

    [Fact]
    public void Changed_video_or_observation_must_not_replace_frozen_evidence()
    {
        var state = Frozen(Analysis);
        Assert.Throws<InvalidDataException>(() => ResearchFrozenSource.Resolve(state, Study, "different-video"));
        Assert.Throws<InvalidDataException>(() => ResearchFrozenSource.Resolve(state with {
            Observation = state.Observation! with { Id = "different-observation" }
        }, Study, "video"));
        Assert.Throws<InvalidDataException>(() => ResearchFrozenSource.Resolve(Frozen(new string('c',32)), Study, "video"));
    }
}
