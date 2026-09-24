using CheatEngine.SDK.Engine.Values;

namespace CheatEngine.Client.Scanning;

/// <summary>An inclusive range of AOB match start addresses.</summary>
/// <remarks>
///     <see cref="End" /> is the last allowed match start. On the bounded route Cheat Engine scans only
///     <c>[Start, End + pattern length)</c>, saturated at the top of the address space, so a match starting at
///     <see cref="End" /> is found. When the bounded route cannot run, the global <c>AOBScan</c> is not narrowed: Core
///     applies the range while copying each matching address, before it contributes to the caller's materialization
///     limit, and the range does not reduce Cheat Engine's scan time or memory.
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
	public bool Contains(Address address)
	{
		return address >= Start && address <= End;
	}
}
