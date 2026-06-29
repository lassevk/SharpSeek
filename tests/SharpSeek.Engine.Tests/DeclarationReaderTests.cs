using SharpSeek.Engine;

using Xunit;

namespace SharpSeek.Engine.Tests;

[Collection(SampleBlazorAppCollection.Name)]
public class DeclarationReaderTests
{
    private readonly SampleBlazorAppFixture _fixture;

    public DeclarationReaderTests(SampleBlazorAppFixture fixture) => _fixture = fixture;

    [Fact]
    public async Task GetRanges_IncludesDocCommentAttributeAndBody()
    {
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;
        DeclarationReader reader = new();

        IReadOnlyList<DeclarationRange> ranges =
            await reader.GetRangesAsync(_fixture.Solution, "DeclarationSamples.Add", cancellationToken);

        DeclarationRange range = Assert.Single(ranges);
        Assert.Equal(ReferenceOrigin.Handwritten, range.Origin);
        Assert.EndsWith("Declarations.cs", range.FilePath);
        Assert.True(range.EndLine > range.StartLine);

        // Reading the reported range yields exactly the documented, attributed member.
        string[] lines = await File.ReadAllLinesAsync(range.FilePath, cancellationToken);
        string slice = string.Join('\n', lines[(range.StartLine - 1)..range.EndLine]);
        Assert.StartsWith("/// <summary>", slice.TrimStart());
        Assert.Contains("[Obsolete(", slice);
        Assert.Contains("return sum;", slice);
    }

    [Fact]
    public async Task GetRanges_Overloads_EachReturnsItsOwnEntry()
    {
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;
        DeclarationReader reader = new();

        IReadOnlyList<DeclarationRange> ranges =
            await reader.GetRangesAsync(_fixture.Solution, "DeclarationSamples.Describe", cancellationToken);

        Assert.Equal(2, ranges.Count);
        Assert.All(ranges, range => Assert.EndsWith("Declarations.cs", range.FilePath));
    }

    [Fact]
    public async Task GetRanges_PartialType_ReturnsOneEntryPerDeclaration()
    {
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;
        DeclarationReader reader = new();

        IReadOnlyList<DeclarationRange> ranges =
            await reader.GetRangesAsync(_fixture.Solution, "DeclarationSamples", cancellationToken);

        Assert.Equal(2, ranges.Count);
    }

    [Fact]
    public async Task GetRanges_QualifiedName_DisambiguatesAcrossTypes()
    {
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;
        DeclarationReader reader = new();

        // "Speak" exists on Animal, Dog, and Puppy; the qualified name picks exactly one.
        IReadOnlyList<DeclarationRange> ranges =
            await reader.GetRangesAsync(_fixture.Solution, "Dog.Speak", cancellationToken);

        DeclarationRange range = Assert.Single(ranges);
        Assert.EndsWith("Animals.cs", range.FilePath);
    }
}
