using CheatEngine.Client.Scanning;
using CheatEngine.SDK.Engine.Inspection;

namespace CheatEngine.Client.Abstractions.Tests.Scanning;

public sealed class AobPatternTests
{
	[Fact]
	public void PatternNormalizesWhitespaceAndHexadecimalCasing()
	{
		AobPattern pattern = new(" 48\t8b\r\n??89 ");

		Assert.Equal("48 8B ?? 89", pattern.Value);
		Assert.Equal(pattern.Value, pattern.ToString());
		Assert.Equal(4, pattern.ByteLength);
		Assert.False(pattern.IsWildcardOnly);
	}

	[Theory]
	[InlineData("")]
	[InlineData(" ")]
	[InlineData("\t\r\n")]
	public void PatternRejectsBlankText(string patternText)
	{
		Assert.Throws<ArgumentException>(() => new AobPattern(patternText));
	}

	[Theory]
	[InlineData("4")]
	[InlineData("GG")]
	[InlineData("?")]
	[InlineData("?0")]
	[InlineData("00,")]
	[InlineData("00 ? ?")]
	public void PatternRejectsTokensOutsideTheDocumentedStringFormSubset(string patternText)
	{
		Assert.Throws<ArgumentException>(() => new AobPattern(patternText));
		Assert.False(AobPattern.TryParse(patternText, out _));
	}

	[Fact]
	public void PatternTryParseNormalizesWildcardOnlyPatternsWithoutThrowing()
	{
		bool parsed = AobPattern.TryParse("??\t??", out AobPattern pattern);

		Assert.True(parsed);
		Assert.Equal("?? ??", pattern.Value);
		Assert.Equal(2, pattern.ByteLength);
		Assert.True(pattern.IsWildcardOnly);
	}

	[Fact]
	public void RequestPreservesPatternLimitModuleRangeProtectionAndAlignment()
	{
		AobPattern pattern = new("90 90");
		ModuleName module = new("game.exe");
		AobScanRange range = new(0x400000, 0x4FFFFF);
		ScanProtectionFilter protection = new(ScanProtectionRequirement.Required, ScanProtectionRequirement.Excluded,
			ScanProtectionRequirement.Any);

		AobScanRequest request = new(pattern, 2, module, range, protection, ScanAlignment.AlignedTo(4));

		Assert.Equal(pattern, request.Pattern);
		Assert.Equal(2, request.MaximumResults);
		Assert.Equal(module, request.Module);
		Assert.Equal(range, request.Range);
		Assert.Equal(protection, request.Protection);
		Assert.Equal(ScanAlignment.AlignedTo(4), request.Alignment);
	}

	[Fact]
	public void RequestDefaultsToAnUnspecifiedFilterAndNoAlignment()
	{
		AobScanRequest request = new(new AobPattern("90"), 1);

		Assert.True(request.Protection.IsUnspecified);
		Assert.Equal(ScanAlignment.None, request.Alignment);
		Assert.Equal(ScanAlignmentKind.None, request.Alignment.Kind);
		Assert.Null(request.Module);
		Assert.Null(request.Range);
	}

	[Fact]
	public void AlignmentFactoriesNormalizeTheirArgument()
	{
		ScanAlignment aligned = ScanAlignment.AlignedTo(16);
		ScanAlignment lastDigits = ScanAlignment.LastDigits("f0");

		Assert.Equal(ScanAlignmentKind.AlignedTo, aligned.Kind);
		Assert.Equal(16, aligned.Divisor);
		Assert.Null(aligned.Digits);
		Assert.Equal(ScanAlignmentKind.LastDigits, lastDigits.Kind);
		Assert.Equal("F0", lastDigits.Digits);
		Assert.Equal(0, lastDigits.Divisor);
		Assert.Equal(ScanAlignment.LastDigits("F0"), lastDigits);
	}

	[Theory]
	[InlineData(0)]
	[InlineData(-4)]
	public void AlignedToRejectsANonPositiveDivisor(int divisor)
	{
		Assert.Throws<ArgumentOutOfRangeException>(() => ScanAlignment.AlignedTo(divisor));
	}

	[Theory]
	[InlineData("")]
	[InlineData("FFGG")]
	[InlineData("0x10")]
	[InlineData("12345678901234567")]
	public void LastDigitsRejectsMalformedDigits(string digits)
	{
		Assert.Throws<ArgumentException>(() => ScanAlignment.LastDigits(digits));
	}

	[Fact]
	public void LastDigitsRejectsNull()
	{
		Assert.Throws<ArgumentNullException>(() => ScanAlignment.LastDigits(null!));
	}

	[Theory]
	[InlineData(4, 0, 0)]
	[InlineData(0, 4, 0)]
	[InlineData(0, 0, -1)]
	public void ProtectionFilterRejectsAnUndefinedRequirement(int executable, int copyOnWrite, int writable)
	{
		Assert.Throws<ArgumentOutOfRangeException>(() => new ScanProtectionFilter(
			(ScanProtectionRequirement) executable, (ScanProtectionRequirement) copyOnWrite,
			(ScanProtectionRequirement) writable));
	}

	[Fact]
	public void ProtectionFilterIsUnspecifiedOnlyWhenEveryFlagIs()
	{
		Assert.True(default(ScanProtectionFilter).IsUnspecified);
		Assert.False(new ScanProtectionFilter(ScanProtectionRequirement.Unspecified,
			ScanProtectionRequirement.Unspecified, ScanProtectionRequirement.Any).IsUnspecified);
	}

	[Fact]
	public void RangeIsInclusiveAtBothBoundaries()
	{
		AobScanRange range = new(0x1000, 0x1FFF);

		Assert.True(range.Contains(0x1000));
		Assert.True(range.Contains(0x1FFF));
		Assert.False(range.Contains(0x0FFF));
		Assert.False(range.Contains(0x2000));
		Assert.Throws<ArgumentOutOfRangeException>(() => new AobScanRange(0x2000, 0x1FFF));
	}

	[Theory]
	[InlineData(0)]
	[InlineData(-1)]
	public void RequestRejectsNonPositiveMaterializationLimit(int maximumResults)
	{
		Assert.Throws<ArgumentOutOfRangeException>(() => new AobScanRequest(new AobPattern("90"), maximumResults));
	}

	[Fact]
	public void RequestRejectsDefaultPatternBeforeReachingTheScanner()
	{
		Assert.Throws<ArgumentException>(() => new AobScanRequest(default, 1));
	}

	[Fact]
	public void ResultRejectsTruncationWithoutAnyRetainedMatch()
	{
		Assert.Throws<ArgumentException>(() => new AobScanResult([], true));
	}
}
