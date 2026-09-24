using CheatEngine.Client.Results;

namespace CheatEngine.Client.Core.Domains;

/// <summary>
///     The Client's policy for its own pointer-typed operations when Cheat Engine's configured pointer size differs
///     from the target bitness (audit A10-18, A12-24, CLI-MEM-1; spike C3 D3).
/// </summary>
/// <remarks>
///     <para>
///         Cheat Engine's <c>readPointer</c> follows the target bitness (<c>targetIs64Bit</c>), not the configured pointer
///         size (spike C3 D3(d), a Lua-only host observation). The Client therefore keeps the bitness for every
///         pointer-typed operation and, when CheatEngine.SDK reports that the configured size differs from it
///         (<c>TargetArchitectureObservation.ConfiguredPointerSizeDiffersFromBitness</c>), refuses the operation before any
///         memory access instead of choosing a width silently. A configured size that could not be observed is no
///         evidence of a mismatch: the operation proceeds with the bitness. No width is ever taken from the plugin's own
///         process (CESDK1020).
///     </para>
///     <para>
///         What the configured size affects besides the value Cheat Engine reports (pointer scanner, address parsing,
///         display) is not established; custom codecs receive the facts through
///         <see cref="CheatEngine.Client.Memory.IMemoryPointerWidthContext" /> and decide for themselves.
///     </para>
/// </remarks>
internal static class PointerWidthPolicy
{
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

/// <summary>
///     Lets a built-in pointer codec ask the Core codec context to admit a pointer-typed operation under
///     <see cref="PointerWidthPolicy" />.
/// </summary>
/// <remarks>
///     Implemented by the Core codec context and called by the built-in Address codec of the dependency-injection
///     package, which sees Core internals. A refusal is recorded on the context and reported with its own failure kind.
/// </remarks>
internal interface ICorePointerCodecPolicy
{
	/// <summary>
	///     Observes the target facts once and returns <see langword="false" /> when the bitness is unknown or when the
	///     configured pointer size is known and differs from it; the reason is recorded on the context.
	/// </summary>
	public bool TryAdmitPointerCodec();
}
