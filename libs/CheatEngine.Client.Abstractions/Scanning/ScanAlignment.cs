namespace CheatEngine.Client.Scanning;

/// <summary>The alignment rule of a scan: none, a divisor, or required trailing hexadecimal digits.</summary>
/// <remarks>
///     <para>
///         Values come only from <see cref="None" />, <see cref="AlignedTo" /> and <see cref="LastDigits" />, which
///         validate and normalize their argument before any scan; the default value is <see cref="None" />.
///     </para>
///     <para>
///         The rule is a Client value; CheatEngine.SDK's fast-scan method type never appears in a public signature.
///     </para>
/// </remarks>
public readonly record struct ScanAlignment
{
	private const int MaximumDigits = sizeof(ulong) * 2;

	private ScanAlignment(ScanAlignmentMode mode, int divisor, string? digits)
	{
		Mode = mode;
		Divisor = divisor;
		Digits = digits;
	}

	/// <summary>Gets the rule that checks every address.</summary>
	public static ScanAlignment None => default;

	/// <summary>Gets the kind of rule.</summary>
	public ScanAlignmentMode Mode
	{
		get;
	}

	/// <summary>Gets the divisor of <see cref="ScanAlignmentMode.AlignedTo" />; zero for any other rule.</summary>
	public int Divisor
	{
		get;
	}

	/// <summary>
	///     Gets the upper-case hexadecimal digits of <see cref="ScanAlignmentMode.LastDigits" />;
	///     <see langword="null" /> for any other rule.
	/// </summary>
	public string? Digits
	{
		get;
	}

	/// <summary>Returns the rule that checks only addresses divisible by <paramref name="divisor" />.</summary>
	/// <param name="divisor">The positive divisor, for example 4 for 4-byte aligned values.</param>
	/// <returns>The alignment rule.</returns>
	/// <exception cref="ArgumentOutOfRangeException"><paramref name="divisor" /> is zero or negative.</exception>
	public static ScanAlignment AlignedTo(int divisor)
	{
		ArgumentOutOfRangeException.ThrowIfNegativeOrZero(divisor);
		return new ScanAlignment(ScanAlignmentMode.AlignedTo, divisor, null);
	}

	/// <summary>Returns the rule that checks only addresses whose hexadecimal text ends with <paramref name="digits" />.</summary>
	/// <param name="digits">One to sixteen hexadecimal digits, in either case.</param>
	/// <returns>The alignment rule, with the digits in upper case.</returns>
	/// <exception cref="ArgumentNullException"><paramref name="digits" /> is <see langword="null" />.</exception>
	/// <exception cref="ArgumentException"><paramref name="digits" /> is empty, too long or not hexadecimal.</exception>
	public static ScanAlignment LastDigits(string digits)
	{
		ArgumentNullException.ThrowIfNull(digits);
		if (digits.Length is 0 or > MaximumDigits)
		{
			throw new ArgumentException("A last-digits scan requires one to sixteen hexadecimal digits.",
				nameof(digits));
		}

		Span<char> normalized = stackalloc char[digits.Length];
		for (int index = 0; index < digits.Length; index++)
		{
			char character = digits[index];
			normalized[index] = character switch
			{
				(>= '0' and <= '9') or (>= 'A' and <= 'F') => character,
				>= 'a' and <= 'f' => (char) (character - ('a' - 'A')),
				_ => throw new ArgumentException("A last-digits scan requires one to sixteen hexadecimal digits.",
					nameof(digits))
			};
		}

		return new ScanAlignment(ScanAlignmentMode.LastDigits, 0, new string(normalized));
	}
}
