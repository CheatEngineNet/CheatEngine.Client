using CheatEngine.SDK.Engine.Values;

namespace CheatEngine.Client.Memory;

/// <summary>Provides the bounded raw-memory operations available to an application write codec.</summary>
/// <remarks>
///     <para>
///         The Client invalidates this context immediately when the codec invocation returns or throws. Codecs must not
///         retain the context; a later member access throws
///         <see cref="CheatEngine.Client.Results.CheatEngineActivationExpiredException" />.
///     </para>
///     <para>
///         The Client's context also implements <see cref="IMemoryPointerWidthContext" />. The built-in codecs write
///         little-endian values, an assumption of the local x86/x64 host profile.
///     </para>
/// </remarks>
public interface IMemoryWriteContext
{
	/// <summary>
	///     Gets the process width of the selected target in bytes (the width Cheat Engine's <c>readPointer</c> uses). See
	///     <see cref="IMemoryPointerWidthContext" /> for Cheat Engine's configured pointer size.
	/// </summary>
	/// <exception cref="CheatEngine.Client.Results.CheatEngineOperationException">
	///     No target is selected, or its process width could not be observed. The Client reports this exception as the
	///     codec operation's failure when the codec lets it propagate.
	/// </exception>
	public int PointerSize
	{
		get;
	}

	/// <summary>Tries to write the exact caller-provided bytes to target memory.</summary>
	public bool TryWriteBytes(Address address, ReadOnlySpan<byte> source);
}
