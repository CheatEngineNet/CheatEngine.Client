using System.Collections.Immutable;
using System.Globalization;

using CheatEngine.Client.Scanning;
using CheatEngine.SDK.Engine.Values;

namespace CheatEngine.Client.Abstractions.Tests.Scanning;

public sealed class ValueScanContractTests
{
	[Fact]
	public void SessionStateStartsWithUnknownAndFollowsTheClientLifecycle()
	{
		Assert.Equal(0, (int) ValueScanSessionState.Unknown);
		Assert.Equal(1, (int) ValueScanSessionState.Created);
		Assert.Equal(2, (int) ValueScanSessionState.Scanning);
		Assert.Equal(3, (int) ValueScanSessionState.ResultsReady);
		Assert.Equal(4, (int) ValueScanSessionState.Invalidated);
		Assert.Equal(5, (int) ValueScanSessionState.Closed);
		Assert.Equal(0, (int) ValueScanInvalidationKind.Unknown);
		Assert.Equal(1, (int) ValueScanInvalidationKind.None);
	}

	[Fact]
	public void ReadRequestRejectsNegativeStartAndNonPositiveCountAndKeepsAWideStart()
	{
		ValueScanReadRequest wide = new((long) int.MaxValue + 1, 1);

		Assert.Throws<ArgumentOutOfRangeException>(() => new ValueScanReadRequest(-1, 1));
		Assert.Throws<ArgumentOutOfRangeException>(() => new ValueScanReadRequest(0, 0));
		Assert.Equal((long) int.MaxValue + 1, wide.StartIndex);
	}

	[Fact]
	public void PageNormalizesADefaultArrayAndLocatesTheNextPage()
	{
		ValueScanPage empty = new(0, 0, default);
		ValueScanPage first = new(0, 3, [new ValueScanMatch(new Address(0x1000), "7"), new ValueScanMatch(
			new Address(0x2000), "8")]);
		ValueScanPage last = new(2, 3, [new ValueScanMatch(new Address(0x3000), "9")]);

		Assert.True(empty.Matches.IsEmpty);
		Assert.Equal(ImmutableArray<ValueScanMatch>.Empty, empty.Matches);
		Assert.False(empty.HasMore);
		Assert.Equal(2, first.NextStartIndex);
		Assert.True(first.HasMore);
		Assert.Equal(3, last.NextStartIndex);
		Assert.False(last.HasMore);
		Assert.Throws<ArgumentOutOfRangeException>(() => new ValueScanPage(-1, 0, []));
	}

	[Fact]
	public void MatchRejectsANullValueText()
	{
		ValueScanMatch match = new(new Address(0x401000), "100");

		Assert.Throws<ArgumentNullException>(() => new ValueScanMatch(Address.Zero, null!));
		Assert.Equal(new Address(0x401000), match.Address);
		Assert.Equal("100", match.ValueText);
	}

	[Fact]
	public void ValueFactoriesChooseTheTypeAndFormatTheInvariantText()
	{
		CultureInfo previous = CultureInfo.CurrentCulture;
		CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("fr-FR");
		try
		{
			Assert.Equal((ValueScanValueType.Integer8, "255"), Describe(ValueScanValue.FromByte(255)));
			Assert.Equal((ValueScanValueType.Integer16, "-2"), Describe(ValueScanValue.FromInt16(-2)));
			Assert.Equal((ValueScanValueType.Integer32, "-100000"), Describe(ValueScanValue.FromInt32(-100_000)));
			Assert.Equal((ValueScanValueType.Integer64, "9223372036854775807"),
				Describe(ValueScanValue.FromInt64(long.MaxValue)));
			Assert.Equal((ValueScanValueType.SingleFloat, "1.50"), Describe(ValueScanValue.FromSingle(1.5f, 2)));
			Assert.Equal((ValueScanValueType.DoubleFloat, "-0.250"), Describe(ValueScanValue.FromDouble(-0.25, 3)));
			Assert.Equal((ValueScanValueType.Utf8String, "Gold"), Describe(ValueScanValue.FromUtf8String("Gold")));
			Assert.Equal((ValueScanValueType.Utf16String, "Gold"), Describe(ValueScanValue.FromUtf16String("Gold")));
			Assert.Equal((ValueScanValueType.ByteArray, "48 8B 05"),
				Describe(ValueScanValue.FromBytes([0x48, 0x8B, 0x05])));
		}
		finally
		{
			CultureInfo.CurrentCulture = previous;
		}

		Assert.True(ValueScanValue.FromDouble(2, 0).IsNumeric);
		Assert.False(ValueScanValue.FromBytes([1]).IsNumeric);
		Assert.Null(default(ValueScanValue).Text);
	}

	[Theory]
	[InlineData(0.00001, 5, "0.00001")]
	[InlineData(0.00001, 7, "0.0000100")]
	[InlineData(100.0, 0, "100")]
	[InlineData(100.0, 2, "100.00")]
	[InlineData(1048576.0, 1, "1048576.0")]
	[InlineData(2.5, 15, "2.500000000000000")]
	public void FloatingPointValuesAreWrittenInFixedPointWithTheRequestedDecimals(double value, int decimals,
		string expected)
	{
		// The number of decimals is the precision of Cheat Engine's rounded exact comparison: exponent notation, which a
		// round-trip format produces for 0.00001 ("1E-05"), would carry none.
		Assert.Equal(expected, ValueScanValue.FromDouble(value, decimals).Text);
		Assert.Equal(expected, ValueScanValue.FromSingle((float) value, decimals).Text);
	}

	[Fact]
	public void DoublesFarFromOneAreNeverWrittenInExponentNotation()
	{
		Assert.Equal("100000000000000000000.0", ValueScanValue.FromDouble(1e20, 1).Text);
		Assert.Equal("0.000000000000000", ValueScanValue.FromDouble(1e-20, 15).Text);
	}

	[Fact]
	public void ValueFactoriesRejectValuesCheatEngineCannotParse()
	{
		Assert.Throws<ArgumentOutOfRangeException>(() => ValueScanValue.FromSingle(float.NaN, 2));
		Assert.Throws<ArgumentOutOfRangeException>(() => ValueScanValue.FromDouble(double.PositiveInfinity, 2));
		Assert.Throws<ArgumentOutOfRangeException>(() => ValueScanValue.FromDouble(1, -1));
		Assert.Throws<ArgumentOutOfRangeException>(() => ValueScanValue.FromSingle(1, 16));
		Assert.Throws<ArgumentException>(() => ValueScanValue.FromUtf8String(string.Empty));
		Assert.Throws<ArgumentNullException>(() => ValueScanValue.FromUtf16String(null!));
		Assert.Throws<ArgumentException>(() => ValueScanValue.FromBytes([]));
	}

	[Fact]
	public void FirstRequestFactoriesScanTheWholeAddressSpaceWithoutFilters()
	{
		ValueScanFirstRequest exact = ValueScanFirstRequest.Exact(ValueScanValue.FromUtf8String("Gold"));
		ValueScanFirstRequest between =
			ValueScanFirstRequest.Between(ValueScanValue.FromInt32(1), ValueScanValue.FromInt32(9));
		ValueScanFirstRequest unknown = ValueScanFirstRequest.UnknownInitialValue(ValueScanValueType.SingleFloat);

		Assert.Equal(ValueScanComparison.Exact, exact.Comparison);
		Assert.Equal(ValueScanValueType.Utf8String, exact.ValueType);
		Assert.Equal(Address.Zero, exact.StartAddress);
		Assert.Equal(new Address(ulong.MaxValue), exact.StopAddress);
		Assert.Equal(default, exact.Protection);
		Assert.Equal(ScanAlignment.None, exact.Alignment);
		Assert.Null(exact.UpperValue);
		Assert.Equal(ValueScanComparison.Between, between.Comparison);
		Assert.Equal("1", between.Value?.Text);
		Assert.Equal("9", between.UpperValue?.Text);
		Assert.Equal(ValueScanComparison.UnknownInitialValue, unknown.Comparison);
		Assert.Equal(ValueScanValueType.SingleFloat, unknown.ValueType);
		Assert.Null(unknown.Value);
	}

	[Fact]
	public void FirstRequestFactoriesRejectComparisonsCheatEngineDoesNotDefine()
	{
		Assert.Throws<ArgumentException>(() => ValueScanFirstRequest.Exact(default));
		Assert.Throws<ArgumentException>(() =>
			ValueScanFirstRequest.Between(ValueScanValue.FromInt32(1), ValueScanValue.FromInt64(9)));
		Assert.Throws<ArgumentException>(() =>
			ValueScanFirstRequest.BiggerThan(ValueScanValue.FromUtf8String("Gold")));
		Assert.Throws<ArgumentException>(() => ValueScanFirstRequest.SmallerThan(ValueScanValue.FromBytes([1])));
		Assert.Throws<ArgumentOutOfRangeException>(() =>
			ValueScanFirstRequest.UnknownInitialValue(ValueScanValueType.Utf16String));
	}

	[Fact]
	public void FirstRequestNarrowsItsRangeFiltersAndAlignment()
	{
		ScanProtectionFilter writable = new(ScanProtectionRequirement.Unspecified, ScanProtectionRequirement.Excluded,
			ScanProtectionRequirement.Required);
		ValueScanFirstRequest request = ValueScanFirstRequest.Exact(ValueScanValue.FromInt32(100))
			.WithRange(new Address(0x1000), new Address(0x2000))
			.WithProtection(writable)
			.WithAlignment(ScanAlignment.AlignedTo(4));

		Assert.Equal(new Address(0x1000), request.StartAddress);
		Assert.Equal(new Address(0x2000), request.StopAddress);
		Assert.Equal(writable, request.Protection);
		Assert.Equal(4, request.Alignment.Divisor);
		Assert.Throws<ArgumentOutOfRangeException>(() => request.WithRange(new Address(0x2000), new Address(0x2000)));
	}

	[Fact]
	public void NextRequestFactoriesCarryAValueOnlyWhenTheyCompareOne()
	{
		ValueScanNextRequest exact = ValueScanNextRequest.Exact(ValueScanValue.FromInt32(95));
		ValueScanNextRequest decreasedBy = ValueScanNextRequest.DecreasedBy(ValueScanValue.FromInt32(5));

		Assert.Equal(ValueScanComparison.Exact, exact.Comparison);
		Assert.Equal("95", exact.Value?.Text);
		Assert.Equal(ValueScanComparison.DecreasedBy, decreasedBy.Comparison);
		Assert.All(
			[
				ValueScanNextRequest.Increased(), ValueScanNextRequest.Decreased(), ValueScanNextRequest.Changed(),
				ValueScanNextRequest.Unchanged()
			],
			static request =>
			{
				Assert.Null(request.Value);
				Assert.Null(request.UpperValue);
			});
		Assert.Throws<ArgumentException>(() => ValueScanNextRequest.IncreasedBy(ValueScanValue.FromUtf8String("a")));
		Assert.Throws<ArgumentException>(() =>
			ValueScanNextRequest.Between(ValueScanValue.FromInt16(1), ValueScanValue.FromInt32(2)));
	}

	[Fact]
	public void ScanAlignmentAndProtectionValidateTheirArguments()
	{
		Assert.Equal(0, ScanAlignment.None.Divisor);
		Assert.Null(ScanAlignment.None.Digits);
		Assert.Equal(ScanAlignmentMode.None, ScanAlignment.None.Mode);
		Assert.Equal("0A", ScanAlignment.LastDigits("0a").Digits);
		Assert.Throws<ArgumentOutOfRangeException>(() => ScanAlignment.AlignedTo(0));
		Assert.Throws<ArgumentException>(() => ScanAlignment.LastDigits("0x10"));
		Assert.Throws<ArgumentException>(() => ScanAlignment.LastDigits(new string('F', 17)));
		Assert.Throws<ArgumentOutOfRangeException>(() => new ScanProtectionFilter(
			(ScanProtectionRequirement) 9, ScanProtectionRequirement.Any, ScanProtectionRequirement.Any));
	}

	private static (ValueScanValueType Type, string? Text) Describe(ValueScanValue value)
	{
		return (value.ValueType, value.Text);
	}
}
