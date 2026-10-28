using ProofLab.Api;

namespace ProofLab.Tests;

/// <summary>
/// Synthetic metadata ONLY. A passing test is NOT provider provenance.
/// No Meta endpoints, credentials, videos, or billable operations involved.
/// </summary>
public sealed class SamVideoEvidenceIndexTests
{
    private static string H(char c)=>new(c,64);
    private static SamIndexedEvidence Sample(string? prompt=null)=>
        new(
            "1234567890abcdef1234567890abcdef",
            H('a'), H('b'), "sam-3.1",
            "PROOFLAB_SAM_VIDEO_1_SOURCE_PTS_REMUX_MP4_V2",
            prompt ?? H('c'), H('d'),H('e'),H('b'),
            1828,"2026-10-28T15:00:00.0000000+00:00",
            SamVideoEvidenceIndex.Provenance);

    private static (SamVideoEvidenceIndex Index,string Dir) OpenTemporary()
    {
        var folder=Path.Combine(Path.GetTempPath(),
            "prooflab-sam-sqlite-test-"+Guid.NewGuid().ToString("N"));
        return (new SamVideoEvidenceIndex(folder),folder);
    }

    [Fact]
    public async Task CacheMiss_StaysEmpty_WithoutProviderCalls()
    {
        var (index,root)=OpenTemporary();
        try
        {
            var data=await index.FindLatestAsync(H('a'),H('b'),H('c'),
                CancellationToken.None);
            Assert.Null(data);
            Assert.True(File.Exists(Path.Combine(root,
                ".private","physics","sam-video-evidence.sqlite")));
        }
        finally { Directory.Delete(root,true); }
    }

    [Fact]
    public async Task SameVideo_Model_Prompt_AndProtocol_RoundTripIdempotently()
    {
        var (index,root)=OpenTemporary();
        try
        {
            var record=Sample();
            await index.RegisterVerifiedAsync(record,CancellationToken.None);
            await index.RegisterVerifiedAsync(record,CancellationToken.None);
            var stored=await index.FindLatestAsync(record.VideoSha256,
                record.CacheKey,record.PromptSha256,CancellationToken.None);
            Assert.Equal(record,stored);
            // A different prompt hash must NOT reuse observations.
            Assert.Null(await index.FindLatestAsync(record.VideoSha256,
                record.CacheKey,H('f'),CancellationToken.None));
            Assert.Null(await index.FindLatestAsync(H('f'),
                record.CacheKey,record.PromptSha256,CancellationToken.None));
        }
        finally { Directory.Delete(root,true); }
    }

    [Fact]
    public async Task ConflictingDuplicateCannotOverwriteScientificProvenance()
    {
        var (index,root)=OpenTemporary();
        try
        {
            var entry=Sample();
            await index.RegisterVerifiedAsync(entry,CancellationToken.None);
            var conflicting=entry with {SourceFrames=200};
            await Assert.ThrowsAsync<InvalidDataException>(()=>
                index.RegisterVerifiedAsync(conflicting,CancellationToken.None));
            var recalled=await index.FindLatestAsync(entry.VideoSha256,
                entry.CacheKey,entry.PromptSha256,CancellationToken.None);
            Assert.Equal(entry,recalled);
        }
        finally {Directory.Delete(root,true);}
    }

    [Fact]
    public async Task RestartedIndex_RecallsTheSameVerifiedMetadata()
    {
        var (index,root)=OpenTemporary();
        try
        {
            var entry=Sample();
            await index.RegisterVerifiedAsync(entry,CancellationToken.None);
            var reopened=new SamVideoEvidenceIndex(root);
            var remembered=await reopened.FindLatestAsync(entry.VideoSha256,
                entry.CacheKey,entry.PromptSha256,CancellationToken.None);
            Assert.Equal(entry,remembered);
        }
        finally {Directory.Delete(root,true);}
    }

    [Theory]
    [InlineData("../private/different-cache")]
    [InlineData("bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb/../attack")]
    [InlineData("bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb/runs/../../escape")]
    [InlineData("bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb/runs/not-a-uuid")]
    public void RejectsUnsafeRelativeArchivePaths(string relative)
    {
        Assert.False(SamVideoEvidenceIndex.ValidRelativePath(H('b'),relative));
        var e=Sample() with {ArchiveRelativePath=relative};
        Assert.Throws<ArgumentException>(()=>SamVideoEvidenceIndex.Validate(e));
    }

    [Fact]
    public void RefusesClaimedProviderResultWithoutRequiredFields()
    {
        var e=Sample();
        Assert.Throws<ArgumentException>(()=>
            SamVideoEvidenceIndex.Validate(e with {ProvenanceStatus="USER_PASTED_JSON"}));
        Assert.Throws<ArgumentException>(()=>
            SamVideoEvidenceIndex.Validate(e with {Model="not-sam"}));
        Assert.Throws<ArgumentException>(()=>
            SamVideoEvidenceIndex.Validate(e with {SourceFrames=0}));
        Assert.Throws<ArgumentException>(()=>
            SamVideoEvidenceIndex.Validate(e with {ResponseSha256="invalid"}));
    }

    [Fact]
    public async Task DeclinesMalformedShaQueries()
    {
        var (index,root)=OpenTemporary();
        try
        {
            await Assert.ThrowsAsync<ArgumentException>(()=>
                index.FindLatestAsync("bad",H('b'),H('c'),CancellationToken.None));
        }
        finally {Directory.Delete(root,true);}
    }
}
