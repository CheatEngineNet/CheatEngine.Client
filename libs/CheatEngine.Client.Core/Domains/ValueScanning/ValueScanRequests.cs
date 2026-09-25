using CheatEngine.Client.Results;
using CheatEngine.Client.Scanning;
using CheatEngine.SDK.Engine.Enums;
using CheatEngine.SDK.Engine.Scanning.Values;

namespace CheatEngine.Client.Core.Domains.ValueScanning;

/// <summary>Validates Client value-scan requests and builds the positional CheatEngine.SDK requests from them.</summary>
/// <remarks>
///     Every refusal is <see cref="CheatEngineFailureKind.OperationRejected" /> with
///     <see cref="CheatEngineHostEffect.NotStarted" />: it is decided before any Cheat Engine work is dispatched. The
///     protection filter and the alignment rule are written by <see cref="ScanOptionTranslation" />, the translation
///     the AOB options use; without alignment the positional SDK request carries an empty parameter.
/// </remarks>
internal static class ValueScanRequests
{
	/// <summary>Builds the SDK first-scan request.</summary>
	/// <param name="request">The Client request.</param>
	/// <param name="operation">The public Client operation name.</param>
	/// <param name="sdkRequest">The SDK request when the method returns <see langword="true" />.</param>
	/// <param name="failure">The refusal when the method returns <see langword="false" />.</param>
	/// <returns><see langword="true" /> when the request is valid.</returns>
	internal static bool TryCreateFirst(ValueScanFirstRequest request, string operation,
		out FirstScanRequest sdkRequest, out CheatEngineFailure failure)
	{
		sdkRequest = default;
		if (!TryValidateFirst(request, out string? refusal))
		{
			failure = Rejected(operation, refusal);
			return false;
		}

		ScanValueFlags flags = GetFlags(request.ValueType);
		(FastScanMethod alignmentMethod, string? alignmentParameter) =
			ScanOptionTranslation.ToFastScan(request.Alignment, string.Empty);
		sdkRequest = new FirstScanRequest(
			ToScanOption(request.Comparison),
			flags.VariableType,
			RoundingType.Rounded,
			request.Value?.Text ?? string.Empty,
			request.UpperValue?.Text ?? string.Empty,
			request.StartAddress,
			request.StopAddress,
			ScanOptionTranslation.ToProtectionText(request.Protection),
			alignmentMethod,
			alignmentParameter ?? string.Empty,
			flags.IsHexadecimalInput,
			false,
			flags.IsUnicodeScan,
			flags.IsCaseSensitive);
		failure = default;
		return true;
	}

	/// <summary>Builds the SDK next-scan request for a session whose first scan compared <paramref name="valueType" />.</summary>
	/// <param name="request">The Client request.</param>
	/// <param name="valueType">The value type of the session's first scan, or <see langword="null" /> without one.</param>
	/// <param name="operation">The public Client operation name.</param>
	/// <param name="sdkRequest">The SDK request when the method returns <see langword="true" />.</param>
	/// <param name="failure">The refusal when the method returns <see langword="false" />.</param>
	/// <returns><see langword="true" /> when the request is valid for the session.</returns>
	/// <remarks>
	///     Without a first scan the request is built with integer flags: CheatEngine.SDK then refuses the next scan by the
	///     session state before any Cheat Engine call.
	/// </remarks>
	internal static bool TryCreateNext(ValueScanNextRequest request, ValueScanValueType? valueType, string operation,
		out NextScanRequest sdkRequest, out CheatEngineFailure failure)
	{
		sdkRequest = default;
		if (!TryValidateNext(request, valueType, out string? refusal))
		{
			failure = Rejected(operation, refusal);
			return false;
		}

		ScanValueFlags flags = GetFlags(valueType ?? request.Value?.Type ?? ValueScanValueType.Integer32);
		sdkRequest = new NextScanRequest(
			ToScanOption(request.Comparison),
			RoundingType.Rounded,
			request.Value?.Text ?? string.Empty,
			request.UpperValue?.Text ?? string.Empty,
			flags.IsHexadecimalInput,
			false,
			flags.IsUnicodeScan,
			flags.IsCaseSensitive,
			false);
		failure = default;
		return true;
	}

	/// <summary>Maps a Client comparison to Cheat Engine's scan option.</summary>
	/// <param name="comparison">A defined comparison.</param>
	/// <returns>The scan option.</returns>
	/// <exception cref="ArgumentOutOfRangeException"><paramref name="comparison" /> is not defined.</exception>
	internal static ScanOption ToScanOption(ValueScanComparison comparison)
	{
		return comparison switch
		{
			ValueScanComparison.Exact => ScanOption.ExactValue,
			ValueScanComparison.Between => ScanOption.ValueBetween,
			ValueScanComparison.BiggerThan => ScanOption.BiggerThan,
			ValueScanComparison.SmallerThan => ScanOption.SmallerThan,
			ValueScanComparison.UnknownInitialValue => ScanOption.UnknownValue,
			ValueScanComparison.Increased => ScanOption.IncreasedValue,
			ValueScanComparison.IncreasedBy => ScanOption.IncreasedValueBy,
			ValueScanComparison.Decreased => ScanOption.DecreasedValue,
			ValueScanComparison.DecreasedBy => ScanOption.DecreasedValueBy,
			ValueScanComparison.Changed => ScanOption.Changed,
			ValueScanComparison.Unchanged => ScanOption.Unchanged,
			_ => throw new ArgumentOutOfRangeException(nameof(comparison), comparison,
				"The value-scan comparison is not defined.")
		};
	}

	/// <summary>Maps a Client value type to Cheat Engine's variable type and input flags.</summary>
	/// <param name="valueType">A defined value type.</param>
	/// <returns>The Cheat Engine variable type and the input flags of the scan.</returns>
	/// <exception cref="ArgumentOutOfRangeException"><paramref name="valueType" /> is not defined.</exception>
	internal static ScanValueFlags GetFlags(ValueScanValueType valueType)
	{
		return valueType switch
		{
			ValueScanValueType.Integer8 => new ScanValueFlags(VariableType.Byte, false, false, false),
			ValueScanValueType.Integer16 => new ScanValueFlags(VariableType.Word, false, false, false),
			ValueScanValueType.Integer32 => new ScanValueFlags(VariableType.Dword, false, false, false),
			ValueScanValueType.Integer64 => new ScanValueFlags(VariableType.Qword, false, false, false),
			ValueScanValueType.SingleFloat => new ScanValueFlags(VariableType.Single, false, false, false),
			ValueScanValueType.DoubleFloat => new ScanValueFlags(VariableType.Double, false, false, false),
			ValueScanValueType.Utf8String => new ScanValueFlags(VariableType.String, false, false, true),
			ValueScanValueType.Utf16String => new ScanValueFlags(VariableType.String, false, true, true),
			ValueScanValueType.ByteArray => new ScanValueFlags(VariableType.ByteArray, true, false, false),
			_ => throw new ArgumentOutOfRangeException(nameof(valueType), valueType,
				"The value-scan value type is not defined.")
		};
	}

	private static bool TryValidateFirst(ValueScanFirstRequest request, out string? refusal)
	{
		refusal = request.Comparison switch
		{
			ValueScanComparison.Exact => ValidateValue(request.Value, request.ValueType, requireNumeric: false),
			ValueScanComparison.BiggerThan or ValueScanComparison.SmallerThan => ValidateValue(request.Value,
				request.ValueType, requireNumeric: true),
			ValueScanComparison.Between => ValidateValue(request.Value, request.ValueType, requireNumeric: true) ??
										   ValidateValue(request.UpperValue, request.ValueType, requireNumeric: true),
			ValueScanComparison.UnknownInitialValue => IsNumeric(request.ValueType) && request.Value is null
				? null
				: "An unknown initial value scan requires a numeric value type and no value.",
			_ => "A first value scan compares an exact value, a range, a bound or an unknown initial value."
		};
		refusal ??= request.StopAddress > request.StartAddress
			? null
			: "A value scan range must be non-empty: the exclusive stop address must be greater than the start address.";
		refusal ??= IsValid(request.Protection)
			? null
			: "A value scan protection filter must use defined requirements.";
		refusal ??= IsValid(request.Alignment) ? null : "A value scan alignment must be created by a ScanAlignment factory.";
		return refusal is null;
	}

	private static bool TryValidateNext(ValueScanNextRequest request, ValueScanValueType? valueType,
		out string? refusal)
	{
		ValueScanValueType expected = valueType ?? request.Value?.Type ?? ValueScanValueType.Integer32;
		refusal = request.Comparison switch
		{
			ValueScanComparison.Exact => ValidateValue(request.Value, expected, requireNumeric: false),
			ValueScanComparison.BiggerThan or ValueScanComparison.SmallerThan or ValueScanComparison.IncreasedBy
				or ValueScanComparison.DecreasedBy => ValidateValue(request.Value, expected, requireNumeric: true),
			ValueScanComparison.Between => ValidateValue(request.Value, expected, requireNumeric: true) ??
										   ValidateValue(request.UpperValue, expected, requireNumeric: true),
			ValueScanComparison.Increased or ValueScanComparison.Decreased or ValueScanComparison.Changed
				or ValueScanComparison.Unchanged => request.Value is null && request.UpperValue is null
					? null
					: "A comparison with the previous scan carries no value.",
			_ => "A next value scan compares a value, a range, a bound or the previous scan."
		};
		return refusal is null;
	}

	private static string? ValidateValue(ValueScanValue? value, ValueScanValueType expected, bool requireNumeric)
	{
		if (value is not { Text: not null } present)
		{
			return "A value scan comparison requires a value created by a ValueScanValue factory.";
		}

		if (present.Type != expected)
		{
			return $"The scanned value is a {present.Type} value; the session scans {expected} values.";
		}

		return requireNumeric && !IsNumeric(present.Type) ? "This comparison accepts only a numeric value." : null;
	}

	private static bool IsNumeric(ValueScanValueType valueType)
	{
		return valueType is >= ValueScanValueType.Integer8 and <= ValueScanValueType.DoubleFloat;
	}

	private static bool IsValid(ScanProtectionFilter protection)
	{
		return Enum.IsDefined(protection.Executable) && Enum.IsDefined(protection.CopyOnWrite) &&
			   Enum.IsDefined(protection.Writable);
	}

	private static bool IsValid(ScanAlignment alignment)
	{
		return alignment.Kind switch
		{
			ScanAlignmentKind.None => alignment is { Divisor: 0, Digits: null },
			ScanAlignmentKind.AlignedTo => alignment is { Divisor: > 0, Digits: null },
			ScanAlignmentKind.LastDigits => alignment is { Divisor: 0, Digits.Length: > 0 },
			_ => false
		};
	}

	private static CheatEngineFailure Rejected(string operation, string? refusal)
	{
		return new CheatEngineFailure(CheatEngineFailureKind.OperationRejected, operation,
			refusal ?? "The value-scan request is not valid.", null, CheatEngineHostEffect.NotStarted);
	}

	/// <summary>Cheat Engine's variable type and the input flags of one scanned value type.</summary>
	/// <param name="VariableType">The Cheat Engine variable type.</param>
	/// <param name="IsHexadecimalInput">Whether Cheat Engine reads the input as hexadecimal bytes.</param>
	/// <param name="IsUnicodeScan">Whether a string scan uses UTF-16.</param>
	/// <param name="IsCaseSensitive">Whether a string scan matches case.</param>
	internal readonly record struct ScanValueFlags(
		VariableType VariableType,
		bool IsHexadecimalInput,
		bool IsUnicodeScan,
		bool IsCaseSensitive);
}
