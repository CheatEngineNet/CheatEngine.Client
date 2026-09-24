using System.Diagnostics.CodeAnalysis;

using CheatEngine.SDK.Engine.Values;

namespace CheatEngine.Client.Scanning;

/// <summary>A first value scan: its comparison, its value type, the scanned range and the region filters.</summary>
/// <remarks>
///     <para>
///         Create a request with <see cref="Exact" />, <see cref="Between" />, <see cref="BiggerThan" />,
///         <see cref="SmallerThan" /> or <see cref="UnknownInitialValue" />; it scans the whole address space with no
///         protection filter and no alignment until <see cref="WithRange" />, <see cref="WithProtection" /> or
///         <see cref="WithAlignment" /> narrows it.
///     </para>
///     <para>
///         On the pinned Cheat Engine 7.7 profile the stop address is exclusive: a match is reported only when it fits
///         entirely below <see cref="StopAddress" />. The start address is not byte-exact: a match that begins slightly
///         before <see cref="StartAddress" /> can be reported, so check <see cref="ValueScanMatch.Address" /> when an exact
///         start matters. A <see langword="default" /> request has no value and an empty range; every session refuses it
///         with <see cref="Results.CheatEngineFailureKind.OperationRejected" />.
///     </para>
/// </remarks>
[Experimental(ClientExperimentalDiagnostics.ValueScans, UrlFormat = ClientExperimentalDiagnostics.UrlFormat)]
public readonly record struct ValueScanFirstRequest
{
	private ValueScanFirstRequest(ValueScanComparison comparison, ValueScanValueType valueType, ValueScanValue? value,
		ValueScanValue? upperValue, Address startAddress, Address stopAddress, ScanProtectionFilter protection,
		ScanAlignment alignment)
	{
		Comparison = comparison;
		ValueType = valueType;
		Value = value;
		UpperValue = upperValue;
		StartAddress = startAddress;
		StopAddress = stopAddress;
		Protection = protection;
		Alignment = alignment;
	}

	/// <summary>Gets the comparison of the scan.</summary>
	public ValueScanComparison Comparison
	{
		get;
	}

	/// <summary>Gets the type Cheat Engine compares; later next scans of the session compare the same type.</summary>
	public ValueScanValueType ValueType
	{
		get;
	}

	/// <summary>Gets the compared value, or the lower bound of <see cref="ValueScanComparison.Between" />.</summary>
	/// <remarks><see langword="null" /> for <see cref="ValueScanComparison.UnknownInitialValue" />.</remarks>
	public ValueScanValue? Value
	{
		get;
	}

	/// <summary>Gets the upper bound of <see cref="ValueScanComparison.Between" />, or <see langword="null" />.</summary>
	public ValueScanValue? UpperValue
	{
		get;
	}

	/// <summary>Gets the lower bound of the scanned range; not byte-exact on Cheat Engine 7.7.</summary>
	public Address StartAddress
	{
		get;
	}

	/// <summary>Gets the exclusive upper bound of the scanned range.</summary>
	public Address StopAddress
	{
		get;
	}

	/// <summary>Gets the protection attributes the scanned regions must have or lack.</summary>
	public ScanProtectionFilter Protection
	{
		get;
	}

	/// <summary>Gets the address-alignment rule of the scan.</summary>
	public ScanAlignment Alignment
	{
		get;
	}

	/// <summary>Scans for addresses whose value equals <paramref name="value" />.</summary>
	/// <param name="value">The compared value, of any type.</param>
	/// <returns>The request.</returns>
	/// <exception cref="ArgumentException"><paramref name="value" /> is a <see langword="default" /> value.</exception>
	public static ValueScanFirstRequest Exact(ValueScanValue value)
	{
		RequireValue(value, nameof(value));
		return Create(ValueScanComparison.Exact, value.Type, value, null);
	}

	/// <summary>Scans for addresses whose value is between two bounds, both included.</summary>
	/// <param name="lowest">The numeric lower bound.</param>
	/// <param name="highest">The upper bound, of the same type as <paramref name="lowest" />.</param>
	/// <returns>The request.</returns>
	/// <exception cref="ArgumentException">
	///     A bound is a <see langword="default" /> value or not numeric, or the bounds have different types.
	/// </exception>
	public static ValueScanFirstRequest Between(ValueScanValue lowest, ValueScanValue highest)
	{
		RequireNumeric(lowest, nameof(lowest));
		RequireNumeric(highest, nameof(highest));
		if (lowest.Type != highest.Type)
		{
			throw new ArgumentException("Both bounds of a value scan must have the same type.", nameof(highest));
		}

		return Create(ValueScanComparison.Between, lowest.Type, lowest, highest);
	}

	/// <summary>Scans for addresses whose value is greater than <paramref name="value" />.</summary>
	/// <param name="value">The numeric compared value.</param>
	/// <returns>The request.</returns>
	/// <exception cref="ArgumentException"><paramref name="value" /> is a <see langword="default" /> value or not numeric.</exception>
	public static ValueScanFirstRequest BiggerThan(ValueScanValue value)
	{
		RequireNumeric(value, nameof(value));
		return Create(ValueScanComparison.BiggerThan, value.Type, value, null);
	}

	/// <summary>Scans for addresses whose value is less than <paramref name="value" />.</summary>
	/// <param name="value">The numeric compared value.</param>
	/// <returns>The request.</returns>
	/// <exception cref="ArgumentException"><paramref name="value" /> is a <see langword="default" /> value or not numeric.</exception>
	public static ValueScanFirstRequest SmallerThan(ValueScanValue value)
	{
		RequireNumeric(value, nameof(value));
		return Create(ValueScanComparison.SmallerThan, value.Type, value, null);
	}

	/// <summary>Records every address of a numeric type without comparing, for a later next scan.</summary>
	/// <param name="valueType">A numeric value type.</param>
	/// <returns>The request.</returns>
	/// <exception cref="ArgumentOutOfRangeException"><paramref name="valueType" /> is not a numeric value type.</exception>
	public static ValueScanFirstRequest UnknownInitialValue(ValueScanValueType valueType)
	{
		if (valueType is < ValueScanValueType.Integer8 or > ValueScanValueType.DoubleFloat)
		{
			throw new ArgumentOutOfRangeException(nameof(valueType), valueType,
				"An unknown initial value scan requires a numeric value type.");
		}

		return Create(ValueScanComparison.UnknownInitialValue, valueType, null, null);
	}

	/// <summary>Returns the request narrowed to <c>[startAddress, stopAddress)</c>.</summary>
	/// <param name="startAddress">The lower bound; not byte-exact on Cheat Engine 7.7.</param>
	/// <param name="stopAddress">The exclusive upper bound.</param>
	/// <returns>The narrowed request.</returns>
	/// <exception cref="ArgumentOutOfRangeException">
	///     <paramref name="stopAddress" /> is not greater than <paramref name="startAddress" />.
	/// </exception>
	public ValueScanFirstRequest WithRange(Address startAddress, Address stopAddress)
	{
		if (stopAddress <= startAddress)
		{
			throw new ArgumentOutOfRangeException(nameof(stopAddress), stopAddress,
				"A value scan range must be non-empty: the exclusive stop address must be greater than the start address.");
		}

		return new ValueScanFirstRequest(Comparison, ValueType, Value, UpperValue, startAddress, stopAddress, Protection,
			Alignment);
	}

	/// <summary>Returns the request with a protection filter.</summary>
	/// <param name="protection">The protection attributes the scanned regions must have or lack.</param>
	/// <returns>The filtered request.</returns>
	public ValueScanFirstRequest WithProtection(ScanProtectionFilter protection)
	{
		return new ValueScanFirstRequest(Comparison, ValueType, Value, UpperValue, StartAddress, StopAddress, protection,
			Alignment);
	}

	/// <summary>Returns the request with an address-alignment rule.</summary>
	/// <param name="alignment">The alignment rule.</param>
	/// <returns>The aligned request.</returns>
	public ValueScanFirstRequest WithAlignment(ScanAlignment alignment)
	{
		return new ValueScanFirstRequest(Comparison, ValueType, Value, UpperValue, StartAddress, StopAddress, Protection,
			alignment);
	}

	private static ValueScanFirstRequest Create(ValueScanComparison comparison, ValueScanValueType valueType,
		ValueScanValue? value, ValueScanValue? upperValue)
	{
		return new ValueScanFirstRequest(comparison, valueType, value, upperValue, Address.Zero,
			new Address(ulong.MaxValue), default, ScanAlignment.None);
	}

	private static void RequireValue(ValueScanValue value, string parameterName)
	{
		if (value.Text is null)
		{
			throw new ArgumentException("A scanned value must be created by a ValueScanValue factory.",
				parameterName);
		}
	}

	private static void RequireNumeric(ValueScanValue value, string parameterName)
	{
		RequireValue(value, parameterName);
		if (!value.IsNumeric)
		{
			throw new ArgumentException("This comparison accepts only a numeric value.", parameterName);
		}
	}
}
