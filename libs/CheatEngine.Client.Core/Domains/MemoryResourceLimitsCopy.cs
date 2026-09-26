using CheatEngine.Client.Memory;

namespace CheatEngine.Client.Core.Domains;

/// <summary>
///     Copies the mutable <see cref="MemoryResourceLimits" /> options into the activation's own validated value.
/// </summary>
/// <remarks>
///     An activation-bound client keeps its own copy, so a later change to the options object never changes a running
///     client. The copy goes through the public five-argument constructor, which validates every bound.
/// </remarks>
internal static class MemoryResourceLimitsCopy
{
	/// <summary>Creates an independently validated copy of <paramref name="limits" />.</summary>
	/// <param name="limits">The configured limits.</param>
	/// <returns>A copy that no caller holds.</returns>
	/// <exception cref="ArgumentNullException"><paramref name="limits" /> is <see langword="null" />.</exception>
	/// <exception cref="ArgumentOutOfRangeException">A limit is outside its documented range.</exception>
	internal static MemoryResourceLimits CreateValidated(MemoryResourceLimits limits)
	{
		ArgumentNullException.ThrowIfNull(limits);
		return new MemoryResourceLimits(limits.MaximumReadBytes, limits.MaximumWriteBytes, limits.MaximumStringBytes,
			limits.MaximumBatchPayloadBytes, limits.MaximumBatchOperationCount);
	}
}
