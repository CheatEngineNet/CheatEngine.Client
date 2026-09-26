using System.Diagnostics.CodeAnalysis;

namespace CheatEngine.Client.Scanning;

/// <summary>What a first or next value scan compares.</summary>
/// <remarks>
///     A first scan accepts <see cref="Exact" />, <see cref="Between" />, <see cref="BiggerThan" />,
///     <see cref="SmallerThan" /> and <see cref="UnknownInitialValue" />; a next scan accepts every value except
///     <see cref="UnknownInitialValue" />. Build a request with the factories of <see cref="ValueScanFirstRequest" /> or
///     <see cref="ValueScanNextRequest" />, which choose the comparison.
/// </remarks>
[Experimental(ClientExperimentalDiagnostics.ValueScans, UrlFormat = ClientExperimentalDiagnostics.UrlFormat)]
public enum ValueScanComparison
{
	/// <summary>Equal to the value.</summary>
	Exact = 0,

	/// <summary>Between the value and the upper value, both included.</summary>
	Between = 1,

	/// <summary>Greater than the value.</summary>
	BiggerThan = 2,

	/// <summary>Less than the value.</summary>
	SmallerThan = 3,

	/// <summary>Every address of the value type, without a comparison (first scan only).</summary>
	UnknownInitialValue = 4,

	/// <summary>Greater than in the previous scan (next scan only).</summary>
	Increased = 5,

	/// <summary>Greater than in the previous scan by exactly the value (next scan only).</summary>
	IncreasedBy = 6,

	/// <summary>Less than in the previous scan (next scan only).</summary>
	Decreased = 7,

	/// <summary>Less than in the previous scan by exactly the value (next scan only).</summary>
	DecreasedBy = 8,

	/// <summary>Different from the previous scan (next scan only).</summary>
	Changed = 9,

	/// <summary>Equal to the previous scan (next scan only).</summary>
	Unchanged = 10
}
