using CheatEngine.Client.Core.Domains.ValueScanning;
using CheatEngine.Client.Results;
using CheatEngine.Client.Scanning;
using CheatEngine.SDK.Engine.Enums;
using CheatEngine.SDK.Engine.Scanning.Values;
using CheatEngine.SDK.Engine.Values;

namespace CheatEngine.Client.Core.Tests.Domains.ValueScanning;

/// <summary>The Client value-scan requests become CheatEngine.SDK's positional requests, or are refused before dispatch.</summary>
public sealed class ValueScanRequestsTests
{
	private const string Operation = "Scans.Contract";

	[Fact]
	public void AnExactFirstScanUsesCheatEngineDefaultsOverTheWholeAddressSpace()
	{
		Assert.True(ValueScanRequests.TryCreateFirst(ValueScanFirstRequest.Exact(ValueScanValue.FromInt32(100)),
			Operation, out FirstScanRequest request, out CheatEngineFailure failure));

		Assert.Equal(default, failure);
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
			.Between(ValueScanValue.FromDouble(1.5), ValueScanValue.FromDouble(2.5))
			.WithRange(new Address(0x1000), new Address(0x2000))
			.WithProtection(new ScanProtectionFilter(ScanProtectionRequirement.Any, ScanProtectionRequirement.Excluded,
				ScanProtectionRequirement.Required))
			.WithAlignment(ScanAlignment.AlignedTo(8));

		Assert.True(ValueScanRequests.TryCreateFirst(client, Operation, out FirstScanRequest request, out _));
		Assert.True(ValueScanRequests.TryCreateFirst(client.WithAlignment(ScanAlignment.LastDigits("0c")), Operation,
			out FirstScanRequest lastDigits, out _));

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
		Assert.True(ValueScanRequests.TryCreateFirst(client.WithAlignment(ScanAlignment.None), Operation,
			out FirstScanRequest none, out _));
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

	[Fact]
	public void ADefaultOrMalformedRequestIsRefusedWithoutACheatEngineCall()
	{
		Assert.False(ValueScanRequests.TryCreateFirst(default, Operation, out _, out CheatEngineFailure defaultFailure));
		Assert.False(ValueScanRequests.TryCreateNext(default, ValueScanValueType.Integer32, Operation, out _,
			out CheatEngineFailure nextFailure));

		Assert.Equal(CheatEngineFailureKind.OperationRejected, defaultFailure.Kind);
		Assert.Equal(CheatEngineHostEffect.NotStarted, defaultFailure.HostEffect);
		Assert.Equal(Operation, defaultFailure.Operation);
		Assert.Equal(CheatEngineFailureKind.OperationRejected, nextFailure.Kind);
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
