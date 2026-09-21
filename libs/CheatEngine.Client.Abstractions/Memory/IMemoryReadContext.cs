using CheatEngine.SDK.Engine.Values;

namespace CheatEngine.Client.Memory;

/// <summary>Provides the bounded raw-memory operations available to an application read codec.</summary>
/// <remarks>
///     The Client invalidates this context immediately when the codec invocation returns or throws. Codecs must not
///     retain the context; a later member access throws
///     <see cref="CheatEngine.Client.Results.CheatEngineActivationExpiredException" />.
/// </remarks>
public interface IMemoryReadContext
{
	/// <summary>Gets the selected target's pointer size in bytes.</summary>
	public int PointerSize
	{
		get;
	}

	/// <summary>Tries to fill the exact caller-provided buffer from target memory.</summary>
	public bool TryReadBytes(Address address, Span<byte> destination);
}
