using CheatEngine.SDK.Engine.Values;

namespace CheatEngine.Client.Scanning;

/// <summary>An inclusive target-address range used to filter copied AOB match addresses.</summary>
/// <remarks>
///     String-form <c>AOBScan</c> does not accept start and stop address arguments. The global scan is therefore not
///     narrowed by this range: Core applies it while copying each matching address from the SDK-owned result list and
///     before it contributes to the caller's materialization limit.
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
