using ProofLab.Science;

namespace ProofLab.Tests;

public sealed class ScientificMethodWorkflowTests
{
    private static ResearchProject Create() =>
        ScientificMethodWorkflow.NewProject("Mathematical hypothesis verification", "Mathematics");

    private static Dictionary<string, string> ValidFields(int step)
    {
        var fields = ScientificMethodWorkflow.Stage(step).Fields
            .ToDictionary(f => f.Key, f => new string('X', f.MinimumCharacters + 10));
        if (step == 1)
            fields["sources"] = "https://doi.org/10.1000/example-example-review";
        if (step == 5)
            fields["verification"] = "INCONCLUSIVE: an independent mathematical proof has not yet been verified.";
        return fields;
    }

    [Fact]
    public void CatalogContainsExactlyFiveBilingualStagesAndReports()
    {
        Assert.Equal(5, ScientificMethodWorkflow.Stages.Count);
        Assert.Equal(new[] { 1, 2, 3, 4, 5 },
            ScientificMethodWorkflow.Stages.Select(x => x.Index));
        Assert.All(ScientificMethodWorkflow.Stages, x =>
        {
            Assert.NotEmpty(x.NameEs);
            Assert.NotEmpty(x.NameEn);
            Assert.NotEmpty(x.ReportEs);
            Assert.NotEmpty(x.ReportEn);
            Assert.NotEmpty(x.ReferenceFrameworks);
        });
    }

    [Fact]
    public void CannotAdvanceBeforeRequiredInformationAndHumanAcknowledgement()
    {
        var p = Create();
        Assert.Throws<ArgumentException>(() =>
            ScientificMethodWorkflow.Approve(p, 1, true));
        p = ScientificMethodWorkflow.SaveDraft(p, 1, ValidFields(1));
        Assert.Throws<ArgumentException>(() =>
            ScientificMethodWorkflow.Approve(p, 1, false));
        p = ScientificMethodWorkflow.Approve(p, 1, true);
        Assert.Equal(2, p.ActiveStage);
        Assert.Equal("APPROVED_COMPLETENESS", p.Reports[0].Status);
        Assert.False(string.IsNullOrWhiteSpace(p.Reports[0].Sha256));
        Assert.NotNull(p.Reports[0].ApprovedAtUtc);
        Assert.Equal("DRAFT", p.Reports[1].Status);
    }

    [Fact]
    public void ApprovedStageCannotBeEditedOrApprovedAgain()
    {
        var p = Create();
        p = ScientificMethodWorkflow.Approve(
            ScientificMethodWorkflow.SaveDraft(p, 1, ValidFields(1)), 1, true);
        Assert.Throws<ArgumentException>(() =>
            ScientificMethodWorkflow.SaveDraft(p, 1, ValidFields(1)));
        Assert.Throws<ArgumentException>(() =>
            ScientificMethodWorkflow.Approve(p, 1, true));
        Assert.Throws<ArgumentException>(() =>
            ScientificMethodWorkflow.SaveDraft(p, 4, ValidFields(4)));
    }

    [Fact]
    public void SourcesMustIncludeTraceableDoiOrUrl()
    {
        var p = Create();
        var fields = ValidFields(1);
        fields["sources"] = "A long but untraceable prose citation in an unspecified publication.";
        p = ScientificMethodWorkflow.SaveDraft(p, 1, fields);
        Assert.Contains(ScientificMethodWorkflow.Validate(p.Reports[0]),
            issue => issue.Contains("DOI or URL"));
    }

    [Fact]
    public void EntireWorkflowCanFinishWithoutClaimingDiscovery()
    {
        var p = Create();
        for (var step = 1; step <= 5; step++)
        {
            p = ScientificMethodWorkflow.SaveDraft(p, step, ValidFields(step));
            p = ScientificMethodWorkflow.Approve(p, step, true);
        }
        Assert.Equal(6, p.ActiveStage);
        Assert.All(p.Reports, r => Assert.Equal("APPROVED_COMPLETENESS", r.Status));
        Assert.Contains("no certification", p.Disclaimer);
    }

    [Fact]
    public void RejectsUnrecognizedFieldsAndUnsupportedDisciplines()
    {
        Assert.Throws<ArgumentException>(() =>
            ScientificMethodWorkflow.NewProject("A valid project name", "ClinicalTrials"));
        Assert.Throws<ArgumentException>(() =>
            ScientificMethodWorkflow.SaveDraft(Create(), 1,
                new Dictionary<string, string> { ["injected"] = "bad" }));
    }
}
