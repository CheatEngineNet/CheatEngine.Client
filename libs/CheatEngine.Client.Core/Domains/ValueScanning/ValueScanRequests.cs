using CheatEngine.Client.Results;
using CheatEngine.Client.Scanning;
using CheatEngine.SDK.Engine.Enums;
using CheatEngine.SDK.Engine.Scanning.Values;

namespace CheatEngine.Client.Core.Domains.ValueScanning;

/// <summary>Validates Client value-scan requests and builds the positional CheatEngine.SDK requests from them.</summary>
/// <remarks>
///     A request that its factories would refuse (the <see langword="default" /> request, or a tampered one) throws an
///     <see cref="ArgumentException" /> before the activation check and before any Cheat Engine work is dispatched.
///     The one refusal returned as a failure is a next-scan value of another type than the session's first scan:
///     <see cref="CheatEngineFailureKind.OperationRejected" /> with <see cref="CheatEngineHostEffect.NotStarted" />.
///     The protection filter and the alignment rule are written by <see cref="ScanOptionTranslation" />, the
///     translation the AOB options use; without alignment the positional SDK request carries an empty parameter.
/// </remarks>
internal static class ValueScanRequests
{
	/// <summary>Validates a first-scan request and builds the SDK first-scan request from it.</summary>
	/// <param name="request">The Client request.</param>
	/// <returns>The SDK request.</returns>
	/// <exception cref="ArgumentException">
	///     The request has no value (the <see langword="default" /> request) or a value its comparison refuses.
	/// </exception>
	/// <exception cref="ArgumentOutOfRangeException">
	///     Its comparison or value type is not a defined value, its range is empty, or its protection filter or
	///     alignment rule is not a defined value.
	/// </exception>
	internal static FirstScanRequest CreateFirst(ValueScanFirstRequest request)
	{
		ValidateFirst(request);
		ScanValueFlags flags = GetFlags(request.ValueType);
		(FastScanMethod alignmentMethod, string? alignmentParameter) =
			ScanOptionTranslation.ToFastScan(request.Alignment, string.Empty);
		return new FirstScanRequest(
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
	}

	/// <summary>Throws for a next-scan request that its factories would refuse, whatever the session.</summary>
	/// <param name="request">The Client request.</param>
	/// <exception cref="ArgumentException">
	///     The request has no value (the <see langword="default" /> request), a value its comparison refuses, or bounds
	///     of two types.
	/// </exception>
	/// <exception cref="ArgumentOutOfRangeException">Its comparison is not a defined value.</exception>
	internal static void ValidateNext(ValueScanNextRequest request)
	{
		if (!Enum.IsDefined(request.Comparison))
		{
			throw new ArgumentOutOfRangeException(nameof(request), request.Comparison,
				"A next value scan request has a comparison that is not a defined value.");
		}

		string? problem = request.Comparison switch
		{
			ValueScanComparison.Exact => ValidateValue(request.Value, requireNumeric: false),
			ValueScanComparison.BiggerThan or ValueScanComparison.SmallerThan or ValueScanComparison.IncreasedBy
				or ValueScanComparison.DecreasedBy => ValidateValue(request.Value, requireNumeric: true),
			ValueScanComparison.Between => ValidateValue(request.Value, requireNumeric: true) ??
										   ValidateValue(request.UpperValue, requireNumeric: true) ??
										   ValidateSameType(request.Value, request.UpperValue),
			ValueScanComparison.Increased or ValueScanComparison.Decreased or ValueScanComparison.Changed
				or ValueScanComparison.Unchanged => request.Value is null && request.UpperValue is null
					? null
					: "A comparison with the previous scan carries no value.",
			_ => "A next value scan compares a value, a range, a bound or the previous scan."
		};
		if (problem is not null)
		{
			throw new ArgumentException(problem, nameof(request));
		}
	}

	/// <summary>
	///     Builds the SDK next-scan request of a validated request (<see cref="ValidateNext" />) for a session whose
	///     first scan compared <paramref name="valueType" />.
	/// </summary>
	/// <param name="request">The validated Client request.</param>
	/// <param name="valueType">The value type of the session's first scan, or <see langword="null" /> without one.</param>
	/// <param name="operation">The public Client operation name.</param>
	/// <param name="sdkRequest">The SDK request when the method returns <see langword="true" />.</param>
	/// <param name="failure">The refusal when the method returns <see langword="false" />.</param>
	/// <returns>
	///     <see langword="true" /> unless the request carries a value of another type than the session's.
	/// </returns>
	/// <remarks>
	///     Without a first scan the request is built with the type of its value, or with integer flags: CheatEngine.SDK
	///     then refuses the next scan by the session state before any Cheat Engine call.
	/// </remarks>
	internal static bool TryCreateNext(ValueScanNextRequest request, ValueScanValueType? valueType, string operation,
		out NextScanRequest sdkRequest, out CheatEngineFailure failure)
	{
		sdkRequest = default;
		if (valueType is { } expected && request.Value is { } value && value.ValueType != expected)
		{
			// Both bounds of a validated range have the same type: the lower one stands for the pair.
			failure = new CheatEngineFailure(CheatEngineFailureKind.OperationRejected, operation,
				$"The scanned value is a {value.ValueType} value; the session scans {expected} values.", null,
				CheatEngineHostEffect.NotStarted);
			return false;
		}

		ScanValueFlags flags = GetFlags(valueType ?? request.Value?.ValueType ?? ValueScanValueType.Integer32);
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

	private static void ValidateFirst(ValueScanFirstRequest request)
	{
		if (!Enum.IsDefined(request.Comparison) || !Enum.IsDefined(request.ValueType))
		{
			throw new ArgumentOutOfRangeException(nameof(request),
				"A first value scan request has a comparison or a value type that is not a defined value.");
		}

		string? problem = request.Comparison switch
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
		if (problem is not null)
		{
			throw new ArgumentException(problem, nameof(request));
		}

		bool nonEmptyRange = request.StopAddress > request.StartAddress;
		if (!nonEmptyRange)
		{
			throw new ArgumentOutOfRangeException(nameof(request), request.StopAddress,
				"A value scan range must be non-empty: the exclusive stop address must be greater than the start " +
				"address.");
		}

		if (!ScanOptionTranslation.IsDefined(request.Protection))
		{
			throw new ArgumentOutOfRangeException(nameof(request),
				"A value scan protection filter must use defined requirements.");
		}

		if (!ScanOptionTranslation.IsDefined(request.Alignment))
		{
			throw new ArgumentOutOfRangeException(nameof(request),
				"A value scan alignment must be created by a ScanAlignment factory.");
		}
	}

	private static string? ValidateValue(ValueScanValue? value, ValueScanValueType expected, bool requireNumeric)
	{
		return ValidateValue(value, requireNumeric) ?? (value!.Value.ValueType == expected
			? null
			: $"The scanned value is a {value.Value.ValueType} value; the request scans {expected} values.");
	}

	private static string? ValidateValue(ValueScanValue? value, bool requireNumeric)
	{
		if (value is not { Text: not null } present)
		{
			return "A value scan comparison requires a value created by a ValueScanValue factory.";
		}

		return requireNumeric && !IsNumeric(present.ValueType) ? "This comparison accepts only a numeric value." : null;
	}

	private static string? ValidateSameType(ValueScanValue? lowest, ValueScanValue? highest)
	{
		return lowest?.ValueType == highest?.ValueType ? null : "Both bounds of a value scan must have the same type.";
	}

	private static bool IsNumeric(ValueScanValueType valueType)
	{
		return valueType is >= ValueScanValueType.Integer8 and <= ValueScanValueType.DoubleFloat;
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
