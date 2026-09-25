using CheatEngine.Client.Results;
using CheatEngine.SDK.Engine.Processes;
using CheatEngine.SDK.Engine.Runtime;
using CheatEngine.SDK.Engine.Values;

namespace CheatEngine.Client.Core.Domains;

/// <summary>
///     The Client's policy for its own pointer-typed operations: an unknown target bitness, or a Cheat Engine configured
///     pointer size that differs from it, refuses the operation (audit A10-18, A12-24, AUD-07, CLI-MEM-1; spike C3 D3).
/// </summary>
/// <remarks>
///     <para>
///         Cheat Engine's <c>readPointer</c> follows the target bitness (<c>targetIs64Bit</c>), not the configured pointer
///         size (spike C3 D3(d), a Lua-only host observation). The Client therefore passes the observed bitness to the
///         width-qualified CheatEngine.SDK pointer overloads (<c>TargetMemory.TryReadPointer</c> and
///         <c>TryWritePointer</c> with a <see cref="PointerSize" />) on every pointer-typed path. Before any memory access
///         it refuses the operation when the bitness is unknown, and when CheatEngine.SDK reports that the configured
///         size differs from it (<c>TargetArchitectureObservation.ConfiguredPointerSizeDiffersFromBitness</c>) instead
///         of choosing a width silently. A configured size that could not be observed is no evidence of a mismatch: the
///         operation proceeds with the bitness. No width is ever taken from the plugin's own process (CESDK1020).
///     </para>
///     <para>
///         What the configured size affects besides the value Cheat Engine reports (pointer scanner, address parsing,
///         display) is not established; custom codecs receive the facts through
///         <see cref="CheatEngine.Client.Memory.IMemoryReadContext" /> and
///         <see cref="CheatEngine.Client.Memory.IMemoryWriteContext" /> and decide for themselves.
///     </para>
/// </remarks>
internal static class PointerWidthPolicy
{
	/// <summary>Gets whether a pointer-typed operation may proceed with the observed facts.</summary>
	/// <param name="facts">The target facts observed once for the operation.</param>
	/// <returns>
	///     <see langword="true" /> when the bitness is known and no observed configured pointer size differs from it.
	/// </returns>
	internal static bool IsAdmitted(ObservedTarget facts)
	{
		return facts.Bitness.IsKnown && !facts.ConfiguredPointerSizeDiffersFromBitness;
	}

	/// <summary>Creates the refusal of a pointer-typed operation that <see cref="IsAdmitted" /> did not admit.</summary>
	/// <param name="operation">The public Client operation name.</param>
	/// <param name="facts">The refused facts.</param>
	/// <returns>The unknown-width refusal, or the mismatch refusal; both are <c>NotStarted</c>.</returns>
	internal static CheatEngineFailure CreateRefusal(string operation, ObservedTarget facts)
	{
		return facts.Bitness.IsKnown
			? CreateMismatchFailure(operation, facts)
			: CreateUnknownWidthFailure(operation, facts);
	}

	/// <summary>Gets whether an address fits the observed pointer width of the target.</summary>
	/// <param name="address">The address the Client is about to access.</param>
	/// <param name="width">The observed target bitness.</param>
	/// <returns><see langword="false" /> only for an address above 4 GiB on a 32-bit target.</returns>
	internal static bool Fits(Address address, PointerSize width)
	{
		return width != PointerSize.Bit32 || address.Value <= uint.MaxValue;
	}

	/// <summary>
	///     Returns the kind of an unknown-width refusal. ADR-08: only an observed "no process selected" is
	///     <c>TargetNotAttached</c>; every other status the SDK reported (a target change, a file opened as a process, an
	///     absent, raising or malformed global) keeps its own kind, and a selected target without a bitness is
	///     <c>InvalidState</c>, like the SDK's own <c>PointerWidthUnknown</c>.
	/// </summary>
	internal static CheatEngineFailureKind GetUnknownWidthKind(ObservedTarget facts)
	{
		return facts.HasTarget
			? CheatEngineFailureKind.InvalidState
			: RuntimeObservationMapping.ToFailureKind(facts.Status.Kind);
	}

	/// <summary>Creates the stable message of an unknown-width refusal; it names the SDK status only.</summary>
	internal static string CreateUnknownWidthMessage(ObservedTarget facts)
	{
		return facts.Status.Kind switch
		{
			ProcessOperationStatusKind.Success =>
				"Cheat Engine did not report the process width of the selected target.",
			ProcessOperationStatusKind.TargetNotAttached =>
				"No target process is selected, so there is no process width.",
			ProcessOperationStatusKind.TargetChanged =>
				"The selected target changed while the Client observed its process width.",
			ProcessOperationStatusKind.FileAsProcessTarget =>
				"The selected target is a file opened as a process, so the process width is unobservable.",
			_ => $"Cheat Engine reported {facts.Status.Kind} for the selected target, so the process width is " +
				 "unobservable."
		};
	}

	/// <summary>Creates the refusal of a pointer-typed operation whose target bitness is unknown.</summary>
	internal static CheatEngineFailure CreateUnknownWidthFailure(string operation, ObservedTarget facts)
	{
		return new CheatEngineFailure(GetUnknownWidthKind(facts), operation, CreateUnknownWidthMessage(facts), null,
			CheatEngineHostEffect.NotStarted);
	}

	/// <summary>Creates the refusal of a pointer-typed operation on a configured size that differs from the bitness.</summary>
	internal static CheatEngineFailure CreateMismatchFailure(string operation, ObservedTarget facts)
	{
		return new CheatEngineFailure(CheatEngineFailureKind.OperationRejected, operation,
			CreateMismatchMessage(facts), null, CheatEngineHostEffect.NotStarted);
	}

	/// <summary>Creates the stable message of a configured/bitness mismatch; it names widths only, never addresses.</summary>
	internal static string CreateMismatchMessage(ObservedTarget facts)
	{
		return $"Cheat Engine's configured pointer size ({facts.ConfiguredPointerSizeBytes} bytes) differs from the " +
			   $"target process width ({facts.Bitness.Bytes} bytes). Cheat Engine's readPointer follows the " +
			   "process width, so the Client refuses pointer-typed operations instead of choosing a width. Restore the " +
			   "configured size or read explicit 32/64-bit integers.";
	}
}
