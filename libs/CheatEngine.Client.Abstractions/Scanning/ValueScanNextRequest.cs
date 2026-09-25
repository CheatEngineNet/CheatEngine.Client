using System.Diagnostics.CodeAnalysis;

namespace CheatEngine.Client.Scanning;

/// <summary>A next value scan: a comparison over the results of the previous scan of the same session.</summary>
/// <remarks>
///     A next scan compares the value type of the session's first scan. A value it carries must have that type: another
///     type is refused with <see cref="Results.CheatEngineFailureKind.OperationRejected" /> before any Cheat Engine call.
///     A <see langword="default" /> request has no value and is refused the same way.
/// </remarks>
[Experimental(ClientExperimentalDiagnostics.ValueScans, UrlFormat = ClientExperimentalDiagnostics.UrlFormat)]
public readonly record struct ValueScanNextRequest
{
	private ValueScanNextRequest(ValueScanComparison comparison, ValueScanValue? value, ValueScanValue? upperValue)
	{
		Comparison = comparison;
		Value = value;
		UpperValue = upperValue;
	}

	/// <summary>Gets the comparison of the scan.</summary>
	public ValueScanComparison Comparison
	{
		get;
	}

	/// <summary>Gets the compared value, the lower bound of a range, or <see langword="null" />.</summary>
	/// <remarks>
	///     <see langword="null" /> for <see cref="ValueScanComparison.Increased" />,
	///     <see cref="ValueScanComparison.Decreased" />, <see cref="ValueScanComparison.Changed" /> and
	///     <see cref="ValueScanComparison.Unchanged" />, which compare with the previous scan.
	/// </remarks>
	public ValueScanValue? Value
	{
		get;
	}

	/// <summary>Gets the upper bound of <see cref="ValueScanComparison.Between" />, or <see langword="null" />.</summary>
	public ValueScanValue? UpperValue
	{
		get;
	}

	/// <summary>Keeps the results whose value equals <paramref name="value" />.</summary>
	/// <param name="value">The compared value.</param>
	/// <returns>The request.</returns>
	/// <exception cref="ArgumentException"><paramref name="value" /> is a <see langword="default" /> value.</exception>
	public static ValueScanNextRequest Exact(ValueScanValue value)
	{
		RequireValue(value, nameof(value));
		return new ValueScanNextRequest(ValueScanComparison.Exact, value, null);
	}

	/// <summary>Keeps the results whose value is between two bounds, both included.</summary>
	/// <param name="lowest">The numeric lower bound.</param>
	/// <param name="highest">The upper bound, of the same type as <paramref name="lowest" />.</param>
	/// <returns>The request.</returns>
	/// <exception cref="ArgumentException">
	///     A bound is a <see langword="default" /> value or not numeric, or the bounds have different types.
	/// </exception>
	public static ValueScanNextRequest Between(ValueScanValue lowest, ValueScanValue highest)
	{
		RequireNumeric(lowest, nameof(lowest));
		RequireNumeric(highest, nameof(highest));
		if (lowest.ValueType != highest.ValueType)
		{
			throw new ArgumentException("Both bounds of a value scan must have the same type.", nameof(highest));
		}

		return new ValueScanNextRequest(ValueScanComparison.Between, lowest, highest);
	}

	/// <summary>Keeps the results whose value is greater than <paramref name="value" />.</summary>
	/// <param name="value">The numeric compared value.</param>
	/// <returns>The request.</returns>
	/// <exception cref="ArgumentException"><paramref name="value" /> is a <see langword="default" /> value or not numeric.</exception>
	public static ValueScanNextRequest BiggerThan(ValueScanValue value)
	{
		RequireNumeric(value, nameof(value));
		return new ValueScanNextRequest(ValueScanComparison.BiggerThan, value, null);
	}

	/// <summary>Keeps the results whose value is less than <paramref name="value" />.</summary>
	/// <param name="value">The numeric compared value.</param>
	/// <returns>The request.</returns>
	/// <exception cref="ArgumentException"><paramref name="value" /> is a <see langword="default" /> value or not numeric.</exception>
	public static ValueScanNextRequest SmallerThan(ValueScanValue value)
	{
		RequireNumeric(value, nameof(value));
		return new ValueScanNextRequest(ValueScanComparison.SmallerThan, value, null);
	}

	/// <summary>Keeps the results whose value grew since the previous scan.</summary>
	/// <returns>The request.</returns>
	public static ValueScanNextRequest Increased()
	{
		return new ValueScanNextRequest(ValueScanComparison.Increased, null, null);
	}

	/// <summary>Keeps the results whose value grew by exactly <paramref name="value" /> since the previous scan.</summary>
	/// <param name="value">The numeric difference.</param>
	/// <returns>The request.</returns>
	/// <exception cref="ArgumentException"><paramref name="value" /> is a <see langword="default" /> value or not numeric.</exception>
	public static ValueScanNextRequest IncreasedBy(ValueScanValue value)
	{
		RequireNumeric(value, nameof(value));
		return new ValueScanNextRequest(ValueScanComparison.IncreasedBy, value, null);
	}

	/// <summary>Keeps the results whose value shrank since the previous scan.</summary>
	/// <returns>The request.</returns>
	public static ValueScanNextRequest Decreased()
	{
		return new ValueScanNextRequest(ValueScanComparison.Decreased, null, null);
	}

	/// <summary>Keeps the results whose value shrank by exactly <paramref name="value" /> since the previous scan.</summary>
	/// <param name="value">The numeric difference.</param>
	/// <returns>The request.</returns>
	/// <exception cref="ArgumentException"><paramref name="value" /> is a <see langword="default" /> value or not numeric.</exception>
	public static ValueScanNextRequest DecreasedBy(ValueScanValue value)
	{
		RequireNumeric(value, nameof(value));
		return new ValueScanNextRequest(ValueScanComparison.DecreasedBy, value, null);
	}

	/// <summary>Keeps the results whose value differs from the previous scan.</summary>
	/// <returns>The request.</returns>
	public static ValueScanNextRequest Changed()
	{
		return new ValueScanNextRequest(ValueScanComparison.Changed, null, null);
	}

	/// <summary>Keeps the results whose value equals the previous scan.</summary>
	/// <returns>The request.</returns>
	public static ValueScanNextRequest Unchanged()
	{
		return new ValueScanNextRequest(ValueScanComparison.Unchanged, null, null);
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
