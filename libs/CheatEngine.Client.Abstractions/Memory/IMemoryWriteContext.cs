using CheatEngine.Client.Results;
using CheatEngine.SDK.Engine.Runtime;
using CheatEngine.SDK.Engine.Values;

namespace CheatEngine.Client.Memory;

/// <summary>
///     Provides the bounded raw-memory operations and target width facts available to an application write codec.
/// </summary>
/// <remarks>
///     <para>
///         <b>Call-only.</b> The Client implements this interface and applications call it. A minor release can add
///         members to it, so implement it only in a test double.
///     </para>
///     <para>
///         The Client invalidates this context immediately when the codec invocation returns or throws. Codecs must not
///         retain the context; a later member access throws
///         <see cref="CheatEngine.Client.Results.CheatEngineActivationExpiredException" />. From a deactivation
///         callback, where the memory client still admits the codec write itself (see
///         <see cref="ICheatEngineClient" />), every member throws <see cref="CheatEngineInvalidStateException" />,
///         since the activation is stopping.
///     </para>
///     <para>
///         The width facts are observed once per codec invocation and expire with the context. Cheat Engine's
///         <c>readPointer</c> follows the bitness, not the configured pointer size (a Lua-only host observation; not
///         host-qualified); what the configured size affects besides the value Cheat Engine reports is not established.
///         The Client's own pointer-typed operations are refused when the bitness is unknown or the two differ; an
///         application codec decides for itself. The Client ships no codec, so a codec also chooses the byte order it
///         writes; the local x86/x64 host profile is little-endian.
///     </para>
/// </remarks>
public interface IMemoryWriteContext
{
	/// <summary>
	///     Gets the bitness of the selected target: the process width Cheat Engine's <c>readPointer</c> follows, never the
	///     plugin's own width, or <see cref="PointerSize.Unknown" /> when no target is selected or the width could not
	///     be observed.
	/// </summary>
	/// <remarks>
	///     When the bitness is unknown and the codec then returns <see langword="false" />, the Client reports why:
	///     <see cref="CheatEngine.Client.Results.CheatEngineFailureKind.TargetNotAttached" /> when no target is selected,
	///     otherwise the kind of the status Cheat Engine reported.
	/// </remarks>
	/// <exception cref="CheatEngineActivationExpiredException">
	///     The context is used after its codec invocation returned or threw, or on another thread, or the activation
	///     ended, or it is stopping and the codec does not run from a deactivation callback.
	/// </exception>
	/// <exception cref="CheatEngineInvalidStateException">
	///     The activation is stopping and the codec runs from a deactivation callback.
	/// </exception>
	/// <exception cref="CheatEngine.Client.Results.CheatEngineClientException">
	///     Cheat Engine could not be asked for the target facts; the exception type follows the kind of the failure. The
	///     Client reports this exception as the codec operation's failure when the codec lets it propagate.
	/// </exception>
	public PointerSize Bitness
	{
		get;
	}

	/// <summary>
	///     Gets Cheat Engine's configured pointer size as a width when it is 4 or 8 bytes, otherwise
	///     <see cref="PointerSize.Unknown" />. It is per-attachment Cheat Engine state, independent of the bitness.
	/// </summary>
	/// <exception cref="CheatEngineActivationExpiredException">
	///     The context is used after its codec invocation returned or threw, or on another thread, or the activation
	///     ended, or it is stopping and the codec does not run from a deactivation callback.
	/// </exception>
	/// <exception cref="CheatEngineInvalidStateException">
	///     The activation is stopping and the codec runs from a deactivation callback.
	/// </exception>
	/// <exception cref="CheatEngine.Client.Results.CheatEngineClientException">
	///     Cheat Engine could not be asked for the target facts.
	/// </exception>
	public PointerSize ConfiguredPointerSize
	{
		get;
	}

	/// <summary>
	///     Gets the raw value of Cheat Engine's configured pointer size, or <see langword="null" /> when it was not
	///     observed. It can hold a value other than 4 or 8.
	/// </summary>
	/// <exception cref="CheatEngineActivationExpiredException">
	///     The context is used after its codec invocation returned or threw, or on another thread, or the activation
	///     ended, or it is stopping and the codec does not run from a deactivation callback.
	/// </exception>
	/// <exception cref="CheatEngineInvalidStateException">
	///     The activation is stopping and the codec runs from a deactivation callback.
	/// </exception>
	/// <exception cref="CheatEngine.Client.Results.CheatEngineClientException">
	///     Cheat Engine could not be asked for the target facts.
	/// </exception>
	public int? ConfiguredPointerSizeBytes
	{
		get;
	}

	/// <summary>
	///     Gets whether the observed configured pointer size differs from a known bitness; <see langword="null" /> when
	///     either value is unknown, which is no evidence of a mismatch.
	/// </summary>
	/// <exception cref="CheatEngineActivationExpiredException">
	///     The context is used after its codec invocation returned or threw, or on another thread, or the activation
	///     ended, or it is stopping and the codec does not run from a deactivation callback.
	/// </exception>
	/// <exception cref="CheatEngineInvalidStateException">
	///     The activation is stopping and the codec runs from a deactivation callback.
	/// </exception>
	/// <exception cref="CheatEngine.Client.Results.CheatEngineClientException">
	///     Cheat Engine could not be asked for the target facts.
	/// </exception>
	public bool? ConfiguredPointerSizeDiffersFromBitness
	{
		get;
	}

	/// <summary>Tries to write the exact caller-provided bytes to target memory.</summary>
	/// <param name="address">The first target address.</param>
	/// <param name="source">The bytes to write.</param>
	/// <param name="failure">
	///     The classified failure of this write when the method returns <see langword="false" />; the default value on
	///     success. A codec that fails because of this write can return it unchanged.
	/// </param>
	/// <returns><see langword="true" /> when every byte was written.</returns>
	/// <exception cref="CheatEngineActivationExpiredException">
	///     The context is used after its codec invocation returned or threw, or on another thread, or the activation
	///     ended, or it is stopping and the codec does not run from a deactivation callback.
	/// </exception>
	/// <exception cref="CheatEngineInvalidStateException">
	///     The activation is stopping and the codec runs from a deactivation callback.
	/// </exception>
	public bool TryWriteBytes(Address address, ReadOnlySpan<byte> source, out CheatEngineFailure failure);
}
