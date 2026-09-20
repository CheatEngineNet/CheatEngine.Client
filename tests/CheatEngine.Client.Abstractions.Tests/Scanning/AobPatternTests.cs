using CheatEngine.Client.Scanning;
using CheatEngine.SDK.Engine.Enums;
using CheatEngine.SDK.Engine.Inspection;
using CheatEngine.SDK.Engine.Scanning.Aob;

namespace CheatEngine.Client.Tests.Scanning;

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
	public void RequestPreservesPatternOptionsLimitAndModule()
	{
		AobPattern pattern = new("90 90");
		AobScanOptions options = new("-w+x-c", default, null);
		ModuleName module = new("game.exe");
		AobScanRange range = new(0x400000, 0x4FFFFF);

		AobScanRequest request = new(pattern, options, 2, module, range);

		Assert.Equal(pattern, request.Pattern);
		Assert.Equal("+X-C-W", request.Options.ProtectionFlags);
		Assert.Equal(2, request.MaximumResults);
		Assert.Equal(module, request.Module);
		Assert.Equal(range, request.Range);
	}

	[Fact]
	public void RequestNormalizesAlignmentParametersBeforeTheyReachTheScanner()
	{
		AobScanRequest aligned = new(
			new AobPattern("90"), new AobScanOptions(null, FastScanMethod.Aligned, "00016"), 1);
		AobScanRequest lastDigits = new(
			new AobPattern("90"), new AobScanOptions(null, FastScanMethod.LastDigits, "f0"), 1);

		Assert.Equal("16", aligned.Options.AlignmentParameter);
		Assert.Equal("F0", lastDigits.Options.AlignmentParameter);
	}

	[Theory]
	[InlineData("X")]
	[InlineData("+X+X")]
	[InlineData("+Q")]
	[InlineData("+X ")]
	public void RequestRejectsMalformedProtectionExpressions(string protection)
	{
		Assert.Throws<ArgumentException>(() => new AobScanRequest(
			new AobPattern("90"), new AobScanOptions(protection, FastScanMethod.NotAligned, null), 1));
	}

	[Theory]
	[InlineData("0")]
	[InlineData("-4")]
	[InlineData("FFGG")]
	public void RequestRejectsMalformedAlignmentParameters(string parameter)
	{
		Assert.Throws<ArgumentException>(() => new AobScanRequest(
			new AobPattern("90"), new AobScanOptions(null, FastScanMethod.Aligned, parameter), 1));
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
		Assert.Throws<ArgumentOutOfRangeException>(() => new AobScanRequest(
			new AobPattern("90"), AobScanOptions.Default, maximumResults));
	}

	[Fact]
	public void RequestRejectsDefaultPatternBeforeReachingTheScanner()
	{
		Assert.Throws<ArgumentException>(() => new AobScanRequest(
			default, AobScanOptions.Default, 1));
	}

	[Fact]
	public void ResultRejectsTruncationWithoutAnyRetainedMatch()
	{
		Assert.Throws<ArgumentException>(() => new AobScanResult([], true));
	}
}
