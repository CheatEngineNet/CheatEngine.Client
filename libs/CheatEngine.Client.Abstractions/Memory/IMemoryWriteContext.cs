using CheatEngine.SDK.Engine.Values;

namespace CheatEngine.Client.Memory;

/// <summary>Provides the bounded raw-memory operations available to an application write codec.</summary>
public interface IMemoryWriteContext
{
	/// <summary>Gets the selected target's pointer size in bytes.</summary>
	public int PointerSize
	{
		get;
	}

	/// <summary>Tries to write the exact caller-provided bytes to target memory.</summary>
	public bool TryWriteBytes(Address address, ReadOnlySpan<byte> source);
}
