using CheatEngine.SDK.Engine.Values;

namespace CheatEngine.Client.Scanning;

/// <summary>An inclusive range of AOB match start addresses.</summary>
/// <remarks>
///     <see cref="End" /> is the last allowed match start, on every route. On the bounded route Cheat Engine scans only
///     <c>[Start, End + pattern length)</c>, so a match starting at <see cref="End" /> is found when it also fits entirely
///     inside the requested module, if any, and ends below the top of the 64-bit address space (the stop bound
///     saturates there). When the bounded route cannot run, the global <c>AOBScan</c> is not narrowed: Core applies the
///     same rule while copying each matching address, before it contributes to the caller's materialization limit, and
///     the range does not reduce Cheat Engine's scan time or memory.
/// </remarks>
public readonly record struct AobScanRange
{
	/// <summary>Creates an inclusive target-address range.</summary>
	/// <param name="start">The first included address.</param>
	/// <param name="end">The last included address.</param>
	/// <exception cref="ArgumentOutOfRangeException"><paramref name="end" /> precedes <paramref name="start" />.</exception>
	public AobScanRange(Address start, Address end)
	{
		if (end < start)
		{
			throw new ArgumentOutOfRangeException(nameof(end), end,
				"An AOB range end address must not precede its start address.");
		}

		Start = start;
		End = end;
	}

	/// <summary>Gets the first included target address.</summary>
	public Address Start
	{
		get;
	}

	/// <summary>Gets the last included target address.</summary>
	public Address End
	{
		get;
	}

	/// <summary>Gets whether <paramref name="address" /> is inside this inclusive range.</summary>
	/// <param name="address">The address to test.</param>
	/// <returns>
	///     <see langword="true" /> when <paramref name="address" /> is at or after <see cref="Start" /> and at or
	///     before <see cref="End" />.
	/// </returns>
	public bool Contains(Address address)
	{
		return address >= Start && address <= End;
	}
}
