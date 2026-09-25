using CheatEngine.Client.Core.Domains.ValueScanning;
using CheatEngine.Client.Core.Tests.TestSupport;
using CheatEngine.Client.Results;
using CheatEngine.Client.Scanning;
using CheatEngine.SDK.Engine.Enums;
using CheatEngine.SDK.Engine.Scanning.Values;
using CheatEngine.SDK.Engine.Values;

namespace CheatEngine.Client.Core.Tests.Domains.ValueScanning;

/// <summary>
///     The Client value-scan requests become CheatEngine.SDK's positional requests, or throw before dispatch.
/// </summary>
public sealed class ValueScanRequestsTests
{
	private const string Operation = "ValueScans.Contract";

	[Fact]
	public void AnExactFirstScanUsesCheatEngineDefaultsOverTheWholeAddressSpace()
	{
		FirstScanRequest request =
			ValueScanRequests.CreateFirst(ValueScanFirstRequest.Exact(ValueScanValue.FromInt32(100)));

		Assert.Equal(ScanOption.ExactValue, request.ScanOption);
		Assert.Equal(VariableType.Dword, request.VariableType);
		Assert.Equal(RoundingType.Rounded, request.RoundingType);
		Assert.Equal("100", request.Input1);
		Assert.Equal(string.Empty, request.Input2);
		Assert.Equal(Address.Zero, request.StartAddress);
		Assert.Equal(new Address(ulong.MaxValue), request.StopAddress);
		Assert.Equal(string.Empty, request.ProtectionFlags);
		Assert.Equal(FastScanMethod.NotAligned, request.FastScanMethod);
		Assert.Equal(string.Empty, request.AlignmentParameter);
		Assert.False(request.IsHexadecimalInput);
		Assert.False(request.IsNotBinaryString);
		Assert.False(request.IsUnicodeScan);
		Assert.False(request.IsCaseSensitive);
	}

	[Fact]
	public void TheRangeProtectionAndAlignmentBecomeCheatEngineArguments()
	{
		ValueScanFirstRequest client = ValueScanFirstRequest
			.Between(ValueScanValue.FromDouble(1.5, 1), ValueScanValue.FromDouble(2.5, 1))
			.WithRange(new Address(0x1000), new Address(0x2000))
			.WithProtection(new ScanProtectionFilter(ScanProtectionRequirement.Any, ScanProtectionRequirement.Excluded,
				ScanProtectionRequirement.Required))
			.WithAlignment(ScanAlignment.AlignedTo(8));

		FirstScanRequest request = ValueScanRequests.CreateFirst(client);
		FirstScanRequest lastDigits =
			ValueScanRequests.CreateFirst(client.WithAlignment(ScanAlignment.LastDigits("0c")));

		Assert.Equal(ScanOption.ValueBetween, request.ScanOption);
		Assert.Equal(VariableType.Double, request.VariableType);
		Assert.Equal("1.5", request.Input1);
		Assert.Equal("2.5", request.Input2);
		Assert.Equal(new Address(0x1000), request.StartAddress);
		Assert.Equal(new Address(0x2000), request.StopAddress);
		Assert.Equal("*X-C+W", request.ProtectionFlags);
		Assert.Equal(FastScanMethod.Aligned, request.FastScanMethod);
		Assert.Equal("8", request.AlignmentParameter);
		Assert.Equal(FastScanMethod.LastDigits, lastDigits.FastScanMethod);
		Assert.Equal("0C", lastDigits.AlignmentParameter);
		FirstScanRequest none = ValueScanRequests.CreateFirst(client.WithAlignment(ScanAlignment.None));
		Assert.Equal(FastScanMethod.NotAligned, none.FastScanMethod);
		Assert.Equal(string.Empty, none.AlignmentParameter);
	}

	[Theory]
	[InlineData(ValueScanValueType.Integer8, VariableType.Byte, false, false, false)]
	[InlineData(ValueScanValueType.Integer16, VariableType.Word, false, false, false)]
	[InlineData(ValueScanValueType.Integer32, VariableType.Dword, false, false, false)]
	[InlineData(ValueScanValueType.Integer64, VariableType.Qword, false, false, false)]
	[InlineData(ValueScanValueType.SingleFloat, VariableType.Single, false, false, false)]
	[InlineData(ValueScanValueType.DoubleFloat, VariableType.Double, false, false, false)]
	[InlineData(ValueScanValueType.Utf8String, VariableType.String, false, false, true)]
	[InlineData(ValueScanValueType.Utf16String, VariableType.String, false, true, true)]
	[InlineData(ValueScanValueType.ByteArray, VariableType.ByteArray, true, false, false)]
	public void EveryValueTypeHasItsCheatEngineTypeAndInputFlags(ValueScanValueType valueType,
		VariableType variableType, bool hexadecimal, bool unicode, bool caseSensitive)
	{
		Assert.Equal(new ValueScanRequests.ScanValueFlags(variableType, hexadecimal, unicode, caseSensitive),
			ValueScanRequests.GetFlags(valueType));
	}

	[Fact]
	public void EveryDefinedValueTypeAndComparisonMapsAndAnUndefinedOneIsRefused()
	{
		Assert.All(Enum.GetValues<ValueScanValueType>(),
			static valueType => Assert.True(Enum.IsDefined(ValueScanRequests.GetFlags(valueType).VariableType)));
		Assert.All(Enum.GetValues<ValueScanComparison>(),
			static comparison => Assert.True(Enum.IsDefined(ValueScanRequests.ToScanOption(comparison))));
		Assert.Equal(Enum.GetValues<ScanOption>().Order(),
			Enum.GetValues<ValueScanComparison>().Select(ValueScanRequests.ToScanOption).Order());
		Assert.Throws<ArgumentOutOfRangeException>(() => ValueScanRequests.GetFlags((ValueScanValueType) 99));
		Assert.Throws<ArgumentOutOfRangeException>(() => ValueScanRequests.ToScanOption((ValueScanComparison) 99));
	}

	/// <summary>
	///     A default request is a programming error: it throws, whatever the session, and builds no SDK request.
	/// </summary>
	[Fact]
	public void ADefaultRequestThrowsWithoutACheatEngineCall()
	{
		ArgumentException first = Assert.Throws<ArgumentException>(() => ValueScanRequests.CreateFirst(default));
		ArgumentException next = Assert.Throws<ArgumentException>(() => ValueScanRequests.ValidateNext(default));

		Assert.Equal("request", first.ParamName);
		Assert.Equal("request", next.ParamName);
	}

	/// <summary>
	///     A next scan whose bounds were tampered with to have two types throws before the session is asked.
	/// </summary>
	[Fact]
	public void ANextScanRangeOfTwoTypesThrows()
	{
		ValueScanNextRequest tampered = TamperedValues.WithBackingField(
			ValueScanNextRequest.Between(ValueScanValue.FromInt32(1), ValueScanValue.FromInt32(2)),
			nameof(ValueScanNextRequest.UpperValue), (ValueScanValue?) ValueScanValue.FromInt16(2));

		ArgumentException thrown = Assert.Throws<ArgumentException>(() => ValueScanRequests.ValidateNext(tampered));

		Assert.Equal("request", thrown.ParamName);
	}

	/// <summary>
	///     A value whose type no factory creates is tampered: a first or a next scan throws for it before the
	///     comparison rules, which would report it as another type than the request's or the session's.
	/// </summary>
	[Theory]
	[InlineData("First.Value")]
	[InlineData("First.UpperValue")]
	[InlineData("Next.Value")]
	[InlineData("Next.UpperValue")]
	public void AValueOfAnUndefinedTypeThrows(string tampered)
	{
		const ValueScanValueType undefinedType = (ValueScanValueType) 99;
		ValueScanValue? undefined = TamperedValues.WithBackingField(ValueScanValue.FromInt32(2),
			nameof(ValueScanValue.ValueType), undefinedType);
		ValueScanFirstRequest firstRange =
			ValueScanFirstRequest.Between(ValueScanValue.FromInt32(1), ValueScanValue.FromInt32(2));
		ValueScanNextRequest nextRange =
			ValueScanNextRequest.Between(ValueScanValue.FromInt32(1), ValueScanValue.FromInt32(2));
		Action validate = tampered switch
		{
			"First.Value" => () => _ = ValueScanRequests.CreateFirst(TamperedValues.WithBackingField(
				ValueScanFirstRequest.Exact(ValueScanValue.FromInt32(1)), nameof(ValueScanFirstRequest.Value),
				undefined)),
			"First.UpperValue" => () => _ = ValueScanRequests.CreateFirst(TamperedValues.WithBackingField(firstRange,
				nameof(ValueScanFirstRequest.UpperValue), undefined)),
			"Next.Value" => () => ValueScanRequests.ValidateNext(TamperedValues.WithBackingField(
				ValueScanNextRequest.Exact(ValueScanValue.FromInt32(1)), nameof(ValueScanNextRequest.Value),
				undefined)),
			"Next.UpperValue" => () => ValueScanRequests.ValidateNext(TamperedValues.WithBackingField(nextRange,
				nameof(ValueScanNextRequest.UpperValue), undefined)),
			_ => throw new ArgumentOutOfRangeException(nameof(tampered), tampered, null)
		};

		ArgumentOutOfRangeException thrown = Assert.Throws<ArgumentOutOfRangeException>(validate);

		Assert.Equal("request", thrown.ParamName);
		Assert.Equal(undefinedType, thrown.ActualValue);
	}

	[Fact]
	public void ANextScanValueMustHaveTheTypeOfTheFirstScan()
	{
		Assert.True(ValueScanRequests.TryCreateNext(ValueScanNextRequest.IncreasedBy(ValueScanValue.FromInt16(3)),
			ValueScanValueType.Integer16, Operation, out NextScanRequest increasedBy, out _));
		Assert.True(ValueScanRequests.TryCreateNext(ValueScanNextRequest.Unchanged(), ValueScanValueType.ByteArray,
			Operation, out NextScanRequest unchanged, out _));
		Assert.False(ValueScanRequests.TryCreateNext(ValueScanNextRequest.Exact(ValueScanValue.FromInt32(3)),
			ValueScanValueType.Integer16, Operation, out _, out CheatEngineFailure mismatch));

		Assert.Equal(ScanOption.IncreasedValueBy, increasedBy.ScanOption);
		Assert.Equal("3", increasedBy.Input1);
		Assert.False(increasedBy.IsPercentageScan);
		Assert.Null(increasedBy.SavedResultName);
		Assert.Equal(ScanOption.Unchanged, unchanged.ScanOption);
		Assert.Equal(string.Empty, unchanged.Input1);
		Assert.True(unchanged.IsHexadecimalInput);
		Assert.Equal(CheatEngineFailureKind.OperationRejected, mismatch.Kind);
		Assert.Equal(CheatEngineHostEffect.NotStarted, mismatch.HostEffect);
	}
}
