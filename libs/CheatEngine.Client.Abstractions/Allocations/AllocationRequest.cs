using System.Diagnostics.CodeAnalysis;

using CheatEngine.SDK.Engine.Values;

namespace CheatEngine.Client.Allocations;

/// <summary>Describes one allocation of memory in Cheat Engine's selected target.</summary>
/// <remarks>
///     <para>
///         <b>Experimental (<c>CECLIENT5002</c>).</b> The allocation API can change in a minor release until its live
///         scenarios pass; see the Abstractions README.
///     </para>
///     <para>
///         The <see langword="default" /> value has a size of zero: <see cref="IAllocationClient" /> throws an
///         <see cref="ArgumentOutOfRangeException" /> for it, as this constructor does, before the activation check and
///         before any Cheat Engine call.
///     </para>
/// </remarks>
[Experimental(ClientExperimentalDiagnostics.Allocations, UrlFormat = ClientExperimentalDiagnostics.UrlFormat)]
public readonly record struct AllocationRequest
{
	/// <summary>Creates an allocation request.</summary>
	/// <param name="size">The positive number of bytes to allocate; Cheat Engine may round it up to its page size.</param>
	/// <param name="protection">The page protection of the allocation.</param>
	/// <param name="preferredAddress">
	///     A nonzero target address near which Cheat Engine should allocate, or <see langword="null" /> to let Cheat Engine
	///     choose; Cheat Engine may allocate elsewhere.
	/// </param>
	/// <exception cref="ArgumentOutOfRangeException">
	///     <paramref name="size" /> is not positive, <paramref name="protection" /> is not a defined value, or
	///     <paramref name="preferredAddress" /> is the null address.
	/// </exception>
	public AllocationRequest(long size, AllocationProtection protection = AllocationProtection.ReadWrite,
		Address? preferredAddress = null)
	{
		ArgumentOutOfRangeException.ThrowIfNegativeOrZero(size);
		if (!Enum.IsDefined(protection))
		{
			throw new ArgumentOutOfRangeException(nameof(protection), protection,
				"The allocation protection must be a defined value.");
		}

		if (preferredAddress is { IsZero: true })
		{
			throw new ArgumentOutOfRangeException(nameof(preferredAddress), preferredAddress,
				"A preferred address must be nonzero; pass null to let Cheat Engine choose the address.");
		}

		Size = size;
		Protection = protection;
		PreferredAddress = preferredAddress;
	}

	/// <summary>Gets the positive number of bytes requested from Cheat Engine.</summary>
	public long Size
	{
		get;
	}

	/// <summary>Gets the page protection of the allocation.</summary>
	public AllocationProtection Protection
	{
		get;
	}

	/// <summary>Gets the nonzero target address near which Cheat Engine should allocate, if any.</summary>
	public Address? PreferredAddress
	{
		get;
	}
}
