using System.Diagnostics;
using System.Runtime.CompilerServices;

using CheatEngine.SDK.Engine.Memory;
using CheatEngine.SDK.Engine.Processes;
using CheatEngine.SDK.Engine.Runtime;
using CheatEngine.SDK.Engine.Values;

namespace CheatEngine.Client.Core.Domains;

/// <summary>Provides the SDK-backed operations available to a scoped application memory codec.</summary>
/// <remarks>
///     The port is internal so Core tests can prove that an expired context never reaches SDK statics. It is not a
///     replacement public memory abstraction. Every access reports the SDK's own <see cref="MemoryAccessFailure" />,
///     never its text; <see cref="MemoryAccessFailureMapping" /> classifies it. Its target facts are the read-only
///     CheatEngine.SDK observations of <see cref="ITargetObservationPort" />: every pointer access passes the target
///     bitness they report to the width-qualified SDK overload, never the plugin's own process width (CESDK1020).
/// </remarks>
internal interface IMemoryCodecContextPort : ITargetObservationPort
{
	public bool TryReadBytes(Address address, Span<byte> destination, out MemoryAccessFailure failure);

	public bool TryWriteBytes(Address address, ReadOnlySpan<byte> source, out MemoryAccessFailure failure);

	/// <summary>Tries one built-in scalar read after the Core client has admitted the operation.</summary>
	/// <remarks>Pointers never take this route: they go through <see cref="TryReadPointer" /> with the observed width.</remarks>
	public bool TryReadPrimitive<T>(Address address, out T value, out MemoryAccessFailure failure)
	{
		return SdkMemoryPrimitivePort.TryRead(address, out value, out failure);
	}

	/// <summary>Tries one built-in scalar write after the Core client has admitted the operation.</summary>
	/// <remarks>Pointers never take this route: they go through <see cref="TryWritePointer" /> with the observed width.</remarks>
	public bool TryWritePrimitive<T>(Address address, T value, out MemoryAccessFailure failure)
	{
		return SdkMemoryPrimitivePort.TryWrite(address, value, out failure);
	}

	/// <summary>
	///     Tries one pointer read qualified by the observed target bitness (<c>TargetMemory.TryReadPointer</c> with a
	///     <see cref="PointerSize" />): the SDK refuses an unknown width, and a returned value wider than the width.
	/// </summary>
	public bool TryReadPointer(Address address, PointerSize pointerSize, out Address value,
		out MemoryAccessFailure failure)
	{
		return TargetMemory.TryReadPointer(address, pointerSize, out value, out failure);
	}

	/// <summary>
	///     Tries one pointer write qualified by the observed target bitness (<c>TargetMemory.TryWritePointer</c> with a
	///     <see cref="PointerSize" />): the SDK refuses an unknown width, and a value wider than the width, before Cheat
	///     Engine is called.
	/// </summary>
	public bool TryWritePointer(Address address, Address value, PointerSize pointerSize,
		out MemoryAccessFailure failure)
	{
		return TargetMemory.TryWritePointer(address, value, pointerSize, out failure);
	}

	/// <summary>Tries one bounded string read after the Core client has admitted the operation.</summary>
	/// <remarks><paramref name="maximumLength" /> is passed unchanged as Cheat Engine's <c>readString</c> limit.</remarks>
	public bool TryReadString(Address address, int maximumLength, bool wideCharacter, out string? value,
		out MemoryAccessFailure failure)
	{
		return TargetMemory.TryReadString(address, maximumLength, wideCharacter, out value, out failure);
	}

	/// <summary>Tries one string write after the Core client has admitted the operation.</summary>
	public bool TryWriteString(Address address, ReadOnlySpan<char> value, bool wideCharacter,
		out MemoryAccessFailure failure)
	{
		return TargetMemory.TryWriteString(address, value, wideCharacter, out failure);
	}
}

/// <summary>Maps the Core's supported scalar set to the SDK while retaining an injectable port boundary.</summary>
/// <remarks>
///     The Core client admits the type before it calls the port and routes <see cref="Address" /> to the width-qualified
///     pointer overloads, so any other type reaching this class is a Client defect, never a host outcome.
/// </remarks>
internal static class SdkMemoryPrimitivePort
{
	internal static bool TryRead<T>(Address address, out T value, out MemoryAccessFailure failure)
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

		throw NotAScalar<T>();
	}

	internal static bool TryWrite<T>(Address address, T value, out MemoryAccessFailure failure)
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

		throw NotAScalar<T>();
	}

	private static UnreachableException NotAScalar<T>()
	{
		return new UnreachableException(
			$"'{typeof(T).FullName}' is not a built-in CheatEngine.Client scalar; the Core client admits the type first.");
	}

	private static bool TryRead<T, TValue>(Reader<TValue> reader, Address address, out T value,
		out MemoryAccessFailure failure)
	{
		if (reader(address, out TValue readValue, out failure))
		{
			value = Unsafe.As<TValue, T>(ref readValue);
			return true;
		}

		value = default!;
		return false;
	}

	private static bool TryWrite<T, TValue>(Writer<TValue> writer, Address address, T value,
		out MemoryAccessFailure failure)
	{
		TValue writeValue = Unsafe.As<T, TValue>(ref value);
		return writer(address, writeValue, out failure);
	}

	private delegate bool Reader<T>(Address address, out T value, out MemoryAccessFailure failure);

	private delegate bool Writer<in T>(Address address, T value, out MemoryAccessFailure failure);
}

/// <summary>
///     Calls the SDK memory primitives after the owning context has admitted the operation; its target facts are the
///     read-only observations of <see cref="SdkRuntimeObservationPort" />.
/// </summary>
internal sealed class SdkMemoryCodecContextPort : IMemoryCodecContextPort
{
	private SdkMemoryCodecContextPort()
	{
	}

	internal static SdkMemoryCodecContextPort Instance
	{
		get;
	} = new();

	public ProcessOperationStatus ObserveCurrent(out CurrentProcessObservation observation)
	{
		return SdkRuntimeObservationPort.Instance.ObserveCurrent(out observation);
	}

	public ProcessOperationStatus ObserveTargetArchitecture(out TargetArchitectureObservation observation)
	{
		return SdkRuntimeObservationPort.Instance.ObserveTargetArchitecture(out observation);
	}

	public ProcessOperationStatus TryGetConfiguredPointerSize(out int rawBytes, out PointerSize pointerSize)
	{
		return SdkRuntimeObservationPort.Instance.TryGetConfiguredPointerSize(out rawBytes, out pointerSize);
	}

	public bool TryReadBytes(Address address, Span<byte> destination, out MemoryAccessFailure failure)
	{
		return TargetMemory.TryReadBytes(address, destination, out failure);
	}

	public bool TryWriteBytes(Address address, ReadOnlySpan<byte> source, out MemoryAccessFailure failure)
	{
		return TargetMemory.TryWriteBytes(address, source, out failure);
	}
}
