using CheatEngine.Client.Results;
using CheatEngine.SDK.Engine.Memory;

namespace CheatEngine.Client.Core.Domains;

/// <summary>
///     Maps every <see cref="MemoryAccessFailure" /> that a CheatEngine.SDK 2.0.0 <c>TargetMemory</c> operation reports to
///     the Client vocabulary, value by value (audit AUD-08, ADR-08).
/// </summary>
/// <remarks>
///     <list type="table">
///         <listheader>
///             <term>SDK failure</term>
///             <description>Client failure kind and host effect</description>
///         </listheader>
///         <item>
///             <term><c>GlobalUnavailable</c></term>
///             <description><c>CapabilityUnavailable</c>, <c>NotStarted</c>: the Cheat Engine global was not called.</description>
///         </item>
///         <item><term><c>LuaError</c></term><description><c>LuaError</c>, <c>Unknown</c></description></item>
///         <item><term><c>ReadFailed</c></term><description><c>MemoryReadFailed</c>, <c>Unknown</c></description></item>
///         <item>
///             <term><c>PartialRead</c></term>
///             <description><c>MemoryReadFailed</c>, <c>Unknown</c>; the byte read reports the confirmed prefix length.</description>
///         </item>
///         <item><term><c>DestinationTooSmall</c></term><description><c>ResultLimitExceeded</c>, <c>Unknown</c></description></item>
///         <item>
///             <term><c>PointerWidthUnknown</c></term>
///             <description><c>InvalidState</c>, <c>NotStarted</c>: CheatEngine.SDK refuses before calling Cheat Engine.</description>
///         </item>
///         <item>
///             <term><c>PointerValueExceedsTargetWidth</c></term>
///             <description>
///                 <c>OperationRejected</c>. A pointer write is refused before Cheat Engine is called (<c>NotStarted</c>);
///                 a pointer read is refused after Cheat Engine returned a value wider than the observed target width
///                 (<c>Completed</c>), and nothing is published.
///             </description>
///         </item>
///         <item><term><c>WriteFailed</c></term><description><c>MemoryWriteFailed</c>, <c>Unknown</c></description></item>
///         <item><term><c>InvalidResult</c></term><description><c>InvalidHostResult</c>, <c>Unknown</c></description></item>
///         <item>
///             <term><c>None</c> on a failed call, or an undefined value</term>
///             <description>
///                 <c>IndeterminateHostResult</c>, <c>Unknown</c>: a failure without a recognized cause is never a success
///                 and never an established outcome.
///             </description>
///         </item>
///     </list>
///     <para>
///         Messages are stable and name the category only, never an address or a value. The mapping-totality tests fail
///         when the consumed SDK adds a value.
///     </para>
/// </remarks>
internal static class MemoryAccessFailureMapping
{
	/// <summary>Returns the Client failure kind of a failed SDK memory access.</summary>
	/// <param name="failure">The SDK failure; <see cref="MemoryAccessFailure.None" /> reports a failure without a cause.</param>
	/// <returns>The failure kind; <see cref="CheatEngineFailureKind.IndeterminateHostResult" /> for an unrecognized value.</returns>
	internal static CheatEngineFailureKind ToFailureKind(MemoryAccessFailure failure)
	{
		return failure switch
		{
			MemoryAccessFailure.None => CheatEngineFailureKind.IndeterminateHostResult,
			MemoryAccessFailure.GlobalUnavailable => CheatEngineFailureKind.CapabilityUnavailable,
			MemoryAccessFailure.LuaError => CheatEngineFailureKind.LuaError,
			MemoryAccessFailure.ReadFailed => CheatEngineFailureKind.MemoryReadFailed,
			MemoryAccessFailure.PartialRead => CheatEngineFailureKind.MemoryReadFailed,
			MemoryAccessFailure.DestinationTooSmall => CheatEngineFailureKind.ResultLimitExceeded,
			MemoryAccessFailure.PointerWidthUnknown => CheatEngineFailureKind.InvalidState,
			MemoryAccessFailure.PointerValueExceedsTargetWidth => CheatEngineFailureKind.OperationRejected,
			MemoryAccessFailure.WriteFailed => CheatEngineFailureKind.MemoryWriteFailed,
			MemoryAccessFailure.InvalidResult => CheatEngineFailureKind.InvalidHostResult,
			_ => CheatEngineFailureKind.IndeterminateHostResult
		};
	}

	/// <summary>Returns what a failed SDK memory access establishes about the Cheat Engine side effect.</summary>
	/// <param name="failure">The SDK failure.</param>
	/// <param name="isWrite">Whether the access was a write.</param>
	/// <returns>The host effect; <see cref="CheatEngineHostEffect.Unknown" /> unless the failure proves more.</returns>
	internal static CheatEngineHostEffect ToHostEffect(MemoryAccessFailure failure, bool isWrite)
	{
		return failure switch
		{
			MemoryAccessFailure.GlobalUnavailable => CheatEngineHostEffect.NotStarted,
			MemoryAccessFailure.PointerWidthUnknown => CheatEngineHostEffect.NotStarted,
			MemoryAccessFailure.PointerValueExceedsTargetWidth => isWrite
				? CheatEngineHostEffect.NotStarted
				: CheatEngineHostEffect.Completed,
			_ => CheatEngineHostEffect.Unknown
		};
	}

	/// <summary>Creates the failure of one failed SDK memory access.</summary>
	/// <param name="operation">The public Client operation name.</param>
	/// <param name="failure">The SDK failure.</param>
	/// <param name="isWrite">Whether the access was a write.</param>
	/// <returns>The classified failure, whose message names the category only.</returns>
	internal static CheatEngineFailure ToFailure(string operation, MemoryAccessFailure failure, bool isWrite)
	{
		return new CheatEngineFailure(ToFailureKind(failure), operation, Describe(failure, isWrite), null,
			ToHostEffect(failure, isWrite));
	}

	/// <summary>Describes a failed SDK memory access without naming an address or a value.</summary>
	/// <param name="failure">The SDK failure.</param>
	/// <param name="isWrite">Whether the access was a write.</param>
	/// <returns>A stable message.</returns>
	internal static string Describe(MemoryAccessFailure failure, bool isWrite)
	{
		return failure switch
		{
			MemoryAccessFailure.GlobalUnavailable =>
				"The Cheat Engine memory global is unavailable, so Cheat Engine was not called.",
			MemoryAccessFailure.LuaError => "The protected Cheat Engine memory call raised a Lua error.",
			MemoryAccessFailure.ReadFailed => "Cheat Engine could not read the target memory.",
			MemoryAccessFailure.PartialRead => "Cheat Engine returned only part of the requested target bytes.",
			MemoryAccessFailure.DestinationTooSmall =>
				"The value Cheat Engine returned is larger than the destination the Client provided.",
			MemoryAccessFailure.PointerWidthUnknown =>
				"The target pointer width was not observed, so the pointer operation was refused before Cheat Engine " +
				"was called.",
			MemoryAccessFailure.PointerValueExceedsTargetWidth => isWrite
				? "The pointer value does not fit the observed pointer width of the target; nothing was written."
				: "Cheat Engine returned a pointer that does not fit the observed pointer width of the target.",
			MemoryAccessFailure.WriteFailed => "Cheat Engine rejected the target-memory write.",
			MemoryAccessFailure.InvalidResult => "Cheat Engine returned a memory result outside its documented shape.",
			_ => "Cheat Engine reported a failed memory access without a recognized cause."
		};
	}
}
