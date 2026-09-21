using CheatEngine.Client.Core.Infrastructure;
using CheatEngine.SDK.Engine.Memory;
using CheatEngine.SDK.Engine.Values;

namespace CheatEngine.Client.Core.Domains;

/// <summary>Provides the SDK-backed operations available to a scoped application memory codec.</summary>
/// <remarks>
///     The port is internal so Core tests can prove that an expired context never reaches SDK statics. It is not a
///     replacement public memory abstraction.
/// </remarks>
internal interface IMemoryCodecContextPort
{
	public bool IsTarget64Bit();

	public bool TryReadBytes(Address address, Span<byte> destination, out string? failure);

	public bool TryWriteBytes(Address address, ReadOnlySpan<byte> source, out string? failure);
}

/// <summary>Calls the SDK memory primitives after the owning context has admitted the operation.</summary>
internal sealed class SdkMemoryCodecContextPort : IMemoryCodecContextPort
{
	internal static SdkMemoryCodecContextPort Instance
	{
		get;
	} = new();

	private SdkMemoryCodecContextPort()
	{
	}

	public bool IsTarget64Bit()
	{
		return ClientLuaGlobals.TargetIs64Bit();
	}

	public bool TryReadBytes(Address address, Span<byte> destination, out string? failure)
	{
		if (TargetMemory.TryReadBytes(address, destination, out MemoryAccessFailure sdkFailure))
		{
			failure = null;
			return true;
		}

		failure = sdkFailure.ToString();
		return false;
	}

	public bool TryWriteBytes(Address address, ReadOnlySpan<byte> source, out string? failure)
	{
		if (TargetMemory.TryWriteBytes(address, source, out MemoryAccessFailure sdkFailure))
		{
			failure = null;
			return true;
		}

		failure = sdkFailure.ToString();
		return false;
	}
}
