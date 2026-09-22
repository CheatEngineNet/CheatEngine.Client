using System.Runtime.CompilerServices;

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

	/// <summary>Tries one built-in primitive read after the Core client has admitted the operation.</summary>
	public bool TryReadPrimitive<T>(Address address, out T value, out string? failure)
	{
		return SdkMemoryPrimitivePort.TryRead(address, out value, out failure);
	}

	/// <summary>Tries one built-in primitive write after the Core client has admitted the operation.</summary>
	public bool TryWritePrimitive<T>(Address address, T value, out string? failure)
	{
		return SdkMemoryPrimitivePort.TryWrite(address, value, out failure);
	}
}

/// <summary>Maps the Core's supported primitive set to the SDK while retaining an injectable port boundary.</summary>
internal static class SdkMemoryPrimitivePort
{
	internal static bool TryRead<T>(Address address, out T value, out string? failure)
	{
		if (typeof(T) == typeof(byte))
		{
			return TryRead<T, byte>(TargetMemory.TryReadUInt8, address, out value, out failure);
		}

		if (typeof(T) == typeof(sbyte))
		{
			return TryRead<T, sbyte>(TargetMemory.TryReadInt8, address, out value, out failure);
		}

		if (typeof(T) == typeof(ushort))
		{
			return TryRead<T, ushort>(TargetMemory.TryReadUInt16, address, out value, out failure);
		}

		if (typeof(T) == typeof(short))
		{
			return TryRead<T, short>(TargetMemory.TryReadInt16, address, out value, out failure);
		}

		if (typeof(T) == typeof(uint))
		{
			return TryRead<T, uint>(TargetMemory.TryReadUInt32, address, out value, out failure);
		}

		if (typeof(T) == typeof(int))
		{
			return TryRead<T, int>(TargetMemory.TryReadInt32, address, out value, out failure);
		}

		if (typeof(T) == typeof(ulong))
		{
			return TryRead<T, ulong>(TargetMemory.TryReadUInt64, address, out value, out failure);
		}

		if (typeof(T) == typeof(long))
		{
			return TryRead<T, long>(TargetMemory.TryReadInt64, address, out value, out failure);
		}

		if (typeof(T) == typeof(float))
		{
			return TryRead<T, float>(TargetMemory.TryReadSingle, address, out value, out failure);
		}

		if (typeof(T) == typeof(double))
		{
			return TryRead<T, double>(TargetMemory.TryReadDouble, address, out value, out failure);
		}

		if (typeof(T) == typeof(Address))
		{
			return TryRead<T, Address>(TargetMemory.TryReadPointer, address, out value, out failure);
		}

		value = default!;
		failure = $"'{typeof(T).FullName}' is not a built-in CheatEngine.Client memory type.";
		return false;
	}

	internal static bool TryWrite<T>(Address address, T value, out string? failure)
	{
		if (typeof(T) == typeof(byte))
		{
			return TryWrite<T, byte>(TargetMemory.TryWriteUInt8, address, value, out failure);
		}

		if (typeof(T) == typeof(sbyte))
		{
			return TryWrite<T, sbyte>(TargetMemory.TryWriteInt8, address, value, out failure);
		}

		if (typeof(T) == typeof(ushort))
		{
			return TryWrite<T, ushort>(TargetMemory.TryWriteUInt16, address, value, out failure);
		}

		if (typeof(T) == typeof(short))
		{
			return TryWrite<T, short>(TargetMemory.TryWriteInt16, address, value, out failure);
		}

		if (typeof(T) == typeof(uint))
		{
			return TryWrite<T, uint>(TargetMemory.TryWriteUInt32, address, value, out failure);
		}

		if (typeof(T) == typeof(int))
		{
			return TryWrite<T, int>(TargetMemory.TryWriteInt32, address, value, out failure);
		}

		if (typeof(T) == typeof(ulong))
		{
			return TryWrite<T, ulong>(TargetMemory.TryWriteUInt64, address, value, out failure);
		}

		if (typeof(T) == typeof(long))
		{
			return TryWrite<T, long>(TargetMemory.TryWriteInt64, address, value, out failure);
		}

		if (typeof(T) == typeof(float))
		{
			return TryWrite<T, float>(TargetMemory.TryWriteSingle, address, value, out failure);
		}

		if (typeof(T) == typeof(double))
		{
			return TryWrite<T, double>(TargetMemory.TryWriteDouble, address, value, out failure);
		}

		if (typeof(T) == typeof(Address))
		{
			return TryWrite<T, Address>(TargetMemory.TryWritePointer, address, value, out failure);
		}

		failure = $"'{typeof(T).FullName}' is not a built-in CheatEngine.Client memory type.";
		return false;
	}

	private static bool TryRead<T, TValue>(Reader<TValue> reader, Address address, out T value, out string? failure)
	{
		if (reader(address, out TValue readValue, out MemoryAccessFailure sdkFailure))
		{
			value = Unsafe.As<TValue, T>(ref readValue);
			failure = null;
			return true;
		}

		value = default!;
		failure = sdkFailure.ToString();
		return false;
	}

	private static bool TryWrite<T, TValue>(Writer<TValue> writer, Address address, T value, out string? failure)
	{
		TValue writeValue = Unsafe.As<T, TValue>(ref value);
		if (writer(address, writeValue, out MemoryAccessFailure sdkFailure))
		{
			failure = null;
			return true;
		}

		failure = sdkFailure.ToString();
		return false;
	}

	private delegate bool Reader<T>(Address address, out T value, out MemoryAccessFailure failure);

	private delegate bool Writer<in T>(Address address, T value, out MemoryAccessFailure failure);
}

/// <summary>Calls the SDK memory primitives after the owning context has admitted the operation.</summary>
internal sealed class SdkMemoryCodecContextPort : IMemoryCodecContextPort
{
	private SdkMemoryCodecContextPort()
	{
	}

	internal static SdkMemoryCodecContextPort Instance
	{
		get;
	} = new();

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
