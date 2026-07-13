using NzbWebDAV.Utils;

namespace NzbWebDAV.Tests.Utils;

public class SubtitlePreferenceTests
{
    [Theory]
    [InlineData("English", "en")]
    [InlineData("eng", "en")]
    [InlineData("en", "en")]
    public void ParseLanguages_CanonicalizesCommonEnglishAliases(string input, string expected)
    {
        var languages = SubtitlePreference.ParseLanguages(input);
        Assert.Equal(new HashSet<string>(StringComparer.Ordinal) { expected }, languages);
    }

    [Fact]
    public void ParseLanguages_SplitsMultipleLanguages()
    {
        var languages = SubtitlePreference.ParseLanguages("English, Spanish");
        Assert.Equal(new HashSet<string>(StringComparer.Ordinal) { "en", "es" }, languages);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void ParseLanguages_EmptyInputsYieldEmptySet(string? input)
    {
        Assert.Empty(SubtitlePreference.ParseLanguages(input));
    }

    [Fact]
    public void ParseLanguages_FoldsAccentedPortuguese()
    {
        var languages = SubtitlePreference.ParseLanguages("Português");
        Assert.Equal(new HashSet<string>(StringComparer.Ordinal) { "pt" }, languages);
    }

    [Fact]
    public void ParseLanguages_PassesUnknownTokensThroughFolded()
    {
        var languages = SubtitlePreference.ParseLanguages("klingon");
        Assert.Equal(new HashSet<string>(StringComparer.Ordinal) { "klingon" }, languages);
    }

    [Fact]
    public void Rank_SharedLanguageWithSubbedPrimary_IsHighest()
    {
        var primary = SubtitlePreference.ParseLanguages("English");
        Assert.Equal(2, SubtitlePreference.Rank("eng", primary, primaryHasSubs: true));
    }

    [Fact]
    public void Rank_SubbedCandidateWithoutMatch_IsMiddle()
    {
        var primary = SubtitlePreference.ParseLanguages("English");
        Assert.Equal(1, SubtitlePreference.Rank("Spanish", primary, primaryHasSubs: true));
        Assert.Equal(1, SubtitlePreference.Rank("English", new HashSet<string>(StringComparer.Ordinal), primaryHasSubs: false));
    }

    [Fact]
    public void Rank_SublessCandidate_IsLowest()
    {
        var primary = SubtitlePreference.ParseLanguages("English");
        Assert.Equal(0, SubtitlePreference.Rank(null, primary, primaryHasSubs: true));
        Assert.Equal(0, SubtitlePreference.Rank("", primary, primaryHasSubs: true));
    }

    [Theory]
    [InlineData("Movie.srt", true)]
    [InlineData("a.ASS", true)]
    [InlineData("movie.mkv", false)]
    [InlineData(null, false)]
    [InlineData("noext", false)]
    public void IsSubtitleFile_RecognizesSidecarExtensions(string? fileName, bool expected)
    {
        Assert.Equal(expected, SubtitlePreference.IsSubtitleFile(fileName));
    }

    [Fact]
    public void OrderByDescending_Rank_PreservesRelativeOrderWithinTier()
    {
        var primary = SubtitlePreference.ParseLanguages("English");
        var items = new[]
        {
            ("a", (string?)null),     // rank 0
            ("b", "Spanish"),         // rank 1
            ("c", ""),                // rank 0
            ("d", "English"),         // rank 2
        };

        var ordered = items
            .OrderByDescending(x => SubtitlePreference.Rank(x.Item2, primary, primaryHasSubs: true))
            .Select(x => x.Item1)
            .ToList();

        Assert.Equal(["d", "b", "a", "c"], ordered);
    }
}
