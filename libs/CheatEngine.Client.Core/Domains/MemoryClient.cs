using System.Collections.Immutable;
using System.Diagnostics.CodeAnalysis;

using CheatEngine.Client.Core.Infrastructure;
using CheatEngine.Client.Dispatching;
using CheatEngine.Client.Memory;
using CheatEngine.Client.Results;
using CheatEngine.SDK.Engine.Memory;
using CheatEngine.SDK.Engine.Values;

namespace CheatEngine.Client.Core.Domains;

internal sealed class MemoryClient(ICheatEngineDispatcher dispatcher) : IMemoryClient
{
	private readonly ICheatEngineDispatcher _dispatcher =
		dispatcher ?? throw new ArgumentNullException(nameof(dispatcher));

	public bool TryReadPrimitive<T>(Address address, [MaybeNullWhen(false)] out T value,
		out CheatEngineFailure failure, CancellationToken cancellationToken = default)
	{
		T? captured = default;
		string? hostFailure = null;
		bool succeeded = false;
		if (!_dispatcher.TryInvoke(() => succeeded = TryReadKnown(address, out captured, out hostFailure),
			    out failure, cancellationToken))
		{
			value = default;
			return false;
		}

		if (!succeeded)
		{
			value = default;
			failure = hostFailure is null
				? new CheatEngineFailure(CheatEngineFailureKind.Unsupported, "Memory.ReadPrimitive",
					$"'{typeof(T).FullName}' is not a built-in CheatEngine.Client memory type.")
				: new CheatEngineFailure(CheatEngineFailureKind.MemoryReadFailed, "Memory.ReadPrimitive", hostFailure);
			return false;
		}

		value = captured!;
		failure = default;
		return true;
	}

	public T ReadPrimitive<T>(Address address, CancellationToken cancellationToken = default)
	{
		if (TryReadPrimitive(address, out T? value, out CheatEngineFailure failure, cancellationToken))
		{
			return value!;
		}

		failure.Throw();
		return default!;
	}

	public bool TryWritePrimitive<T>(Address address, T value, out CheatEngineFailure failure,
		CancellationToken cancellationToken = default)
	{
		string? hostFailure = null;
		bool handled = false;
		bool succeeded = false;
		if (!_dispatcher.TryInvoke(
			    () => succeeded = TryWriteKnown(address, value, out handled, out hostFailure),
			    out failure, cancellationToken))
		{
			return false;
		}

		if (!succeeded)
		{
			failure = !handled
				? new CheatEngineFailure(CheatEngineFailureKind.Unsupported, "Memory.WritePrimitive",
					$"'{typeof(T).FullName}' is not a built-in CheatEngine.Client memory type.")
				: new CheatEngineFailure(CheatEngineFailureKind.MemoryWriteFailed, "Memory.WritePrimitive",
					hostFailure ?? "Cheat Engine rejected the target-memory write.");
			return false;
		}

		failure = default;
		return true;
	}

	public void WritePrimitive<T>(Address address, T value, CancellationToken cancellationToken = default)
	{
		if (!TryWritePrimitive(address, value, out CheatEngineFailure failure, cancellationToken))
		{
			failure.Throw();
		}
	}

	public bool TryRead<T>(MemoryReadRequest<T> request, [MaybeNullWhen(false)] out T value,
		out CheatEngineFailure failure, CancellationToken cancellationToken = default)
	{
		T? captured = default;
		string? hostFailure = null;
		bool succeeded = false;
		if (!_dispatcher.TryInvoke(() => succeeded = TryReadCore(request, out captured, out hostFailure),
			    out failure, cancellationToken))
		{
			value = default;
			return false;
		}

		if (!succeeded)
		{
			value = default;
			failure = new CheatEngineFailure(CheatEngineFailureKind.MemoryReadFailed, "Memory.Read",
				hostFailure ?? "Cheat Engine rejected the target-memory read.");
			return false;
		}

		value = captured!;
		failure = default;
		return true;
	}

	public T Read<T>(MemoryReadRequest<T> request, CancellationToken cancellationToken = default)
	{
		if (TryRead(request, out T? value, out CheatEngineFailure failure, cancellationToken))
		{
			return value;
		}

		failure.Throw();
		return default!;
	}

	public bool TryWrite<T>(MemoryWriteRequest<T> request, out CheatEngineFailure failure,
		CancellationToken cancellationToken = default)
	{
		string? hostFailure = null;
		bool succeeded = false;
		if (!_dispatcher.TryInvoke(() => succeeded = TryWriteCore(request, out hostFailure),
			    out failure, cancellationToken))
		{
			return false;
		}

		if (!succeeded)
		{
			failure = new CheatEngineFailure(CheatEngineFailureKind.MemoryWriteFailed, "Memory.Write",
				hostFailure ?? "Cheat Engine rejected the target-memory write.");
			return false;
		}

		failure = default;
		return true;
	}

	public void Write<T>(MemoryWriteRequest<T> request, CancellationToken cancellationToken = default)
	{
		if (!TryWrite(request, out CheatEngineFailure failure, cancellationToken))
		{
			failure.Throw();
		}
	}

	public bool TryReadBytes(MemoryBytesReadRequest request, out ImmutableArray<byte> bytes,
		out CheatEngineFailure failure, CancellationToken cancellationToken = default)
	{
		ArgumentOutOfRangeException.ThrowIfNegativeOrZero(request.Length);
		ImmutableArray<byte> captured = ImmutableArray<byte>.Empty;
		string? hostFailure = null;
		bool succeeded = false;
		if (!_dispatcher.TryInvoke(() =>
		    {
			    byte[] buffer = new byte[request.Length];
			    succeeded = TargetMemory.TryReadBytes(request.Address, buffer, out MemoryAccessFailure sdkFailure);
			    if (succeeded)
			    {
				    captured = ImmutableArray.CreateRange(buffer);
			    }
			    else
			    {
				    hostFailure = sdkFailure.ToString();
			    }
		    }, out failure, cancellationToken))
		{
			bytes = [];
			return false;
		}

		bytes = captured;
		return TryMapMemoryFailure(succeeded, false, "Memory.ReadBytes", hostFailure, out failure);
	}

	public ImmutableArray<byte> ReadBytes(MemoryBytesReadRequest request,
		CancellationToken cancellationToken = default)
	{
		if (TryReadBytes(request, out ImmutableArray<byte> bytes, out CheatEngineFailure failure, cancellationToken))
		{
			return bytes;
		}

		failure.Throw();
		return [];
	}

	public bool TryWriteBytes(MemoryBytesWriteRequest request, out CheatEngineFailure failure,
		CancellationToken cancellationToken = default)
	{
		if (request.Bytes.IsDefaultOrEmpty)
		{
			throw new ArgumentException("At least one byte is required.", nameof(request));
		}

		string? hostFailure = null;
		bool succeeded = false;
		if (!_dispatcher.TryInvoke(() =>
		    {
			    succeeded = TargetMemory.TryWriteBytes(request.Address, request.Bytes.AsSpan(),
				    out MemoryAccessFailure sdkFailure);
			    if (!succeeded)
			    {
				    hostFailure = sdkFailure.ToString();
			    }
		    }, out failure, cancellationToken))
		{
			return false;
		}

		return TryMapMemoryFailure(succeeded, true, "Memory.WriteBytes", hostFailure, out failure);
	}

	public void WriteBytes(MemoryBytesWriteRequest request, CancellationToken cancellationToken = default)
	{
		if (!TryWriteBytes(request, out CheatEngineFailure failure, cancellationToken))
		{
			failure.Throw();
		}
	}

	public bool TryReadString(MemoryStringReadRequest request, [NotNullWhen(true)] out string? value,
		out CheatEngineFailure failure, CancellationToken cancellationToken = default)
	{
		ArgumentOutOfRangeException.ThrowIfNegativeOrZero(request.MaximumLength);
		string? captured = null;
		string? hostFailure = null;
		bool succeeded = false;
		if (!_dispatcher.TryInvoke(() =>
		    {
			    succeeded = TargetMemory.TryReadString(request.Address, request.MaximumLength,
				    request.WideCharacter, out captured, out MemoryAccessFailure sdkFailure);
			    if (!succeeded)
			    {
				    hostFailure = sdkFailure.ToString();
			    }
		    }, out failure, cancellationToken))
		{
			value = null;
			return false;
		}

		value = captured;
		return TryMapMemoryFailure(succeeded, false, "Memory.ReadString", hostFailure, out failure);
	}

	public string ReadString(MemoryStringReadRequest request, CancellationToken cancellationToken = default)
	{
		if (TryReadString(request, out string? value, out CheatEngineFailure failure, cancellationToken))
		{
			return value;
		}

		failure.Throw();
		return string.Empty;
	}

	public bool TryWriteString(MemoryStringWriteRequest request, out CheatEngineFailure failure,
		CancellationToken cancellationToken = default)
	{
		ArgumentNullException.ThrowIfNull(request.Value);
		string? hostFailure = null;
		bool succeeded = false;
		if (!_dispatcher.TryInvoke(() =>
		    {
			    succeeded = TargetMemory.TryWriteString(request.Address, request.Value.AsSpan(),
				    request.WideCharacter, out MemoryAccessFailure sdkFailure);
			    if (!succeeded)
			    {
				    hostFailure = sdkFailure.ToString();
			    }
		    }, out failure, cancellationToken))
		{
			return false;
		}

		return TryMapMemoryFailure(succeeded, true, "Memory.WriteString", hostFailure, out failure);
	}

	public void WriteString(MemoryStringWriteRequest request, CancellationToken cancellationToken = default)
	{
		if (!TryWriteString(request, out CheatEngineFailure failure, cancellationToken))
		{
			failure.Throw();
		}
	}

	public bool TryResolvePointerChain(PointerChainRequest request, out Address address,
		out CheatEngineFailure failure, CancellationToken cancellationToken = default)
	{
		if (request.Offsets.IsDefaultOrEmpty)
		{
			throw new ArgumentException("A pointer chain requires at least one offset.", nameof(request));
		}

		if (request.Offsets.Length > 64)
		{
			throw new ArgumentOutOfRangeException(nameof(request), "A pointer chain is limited to 64 hops.");
		}

		Address captured = request.BaseAddress;
		string? hostFailure = null;
		bool succeeded = false;
		if (!_dispatcher.TryInvoke(() =>
		    {
			    Address current = request.BaseAddress;
			    foreach (long offset in request.Offsets)
			    {
				    if (!TargetMemory.TryReadPointer(current, out Address pointer, out MemoryAccessFailure sdkFailure))
				    {
					    hostFailure = sdkFailure.ToString();
					    return;
				    }

				    current = pointer + offset;
			    }

			    captured = current;
			    succeeded = true;
		    }, out failure, cancellationToken))
		{
			address = default;
			return false;
		}

		address = captured;
		return TryMapMemoryFailure(succeeded, false, "Memory.ResolvePointerChain", hostFailure, out failure);
	}

	public Address ResolvePointerChain(PointerChainRequest request, CancellationToken cancellationToken = default)
	{
		if (TryResolvePointerChain(request, out Address address, out CheatEngineFailure failure, cancellationToken))
		{
			return address;
		}

		failure.Throw();
		return default;
	}

	private static bool TryReadCore<T>(MemoryReadRequest<T> request, [MaybeNullWhen(false)] out T value,
		out string? failure)
	{
		failure = null;
		TargetMemoryCodecContext context = TargetMemoryCodecContext.Create();
		if (request.Codec.TryRead(context, request.Address, out value))
		{
			return true;
		}

		failure = context.Failure ?? $"The codec for '{typeof(T).Name}' rejected the target-memory read.";
		return false;
	}

	private static bool TryWriteCore<T>(MemoryWriteRequest<T> request, out string? failure)
	{
		failure = null;
		TargetMemoryCodecContext context = TargetMemoryCodecContext.Create();
		T value = request.Value;
		if (request.Codec.TryWrite(context, request.Address, in value))
		{
			return true;
		}

		failure = context.Failure ?? $"The codec for '{typeof(T).Name}' rejected the target-memory write.";
		return false;
	}

	private static bool TryReadKnown<T>(Address address, [MaybeNullWhen(false)] out T value, out string? failure)
	{
		object? boxed = null;
		bool succeeded;
		if (typeof(T) == typeof(byte))
		{
			succeeded = Read<byte>(TargetMemory.TryReadUInt8, address, out boxed, out failure);
		}
		else if (typeof(T) == typeof(sbyte))
		{
			succeeded = Read<sbyte>(TargetMemory.TryReadInt8, address, out boxed, out failure);
		}
		else if (typeof(T) == typeof(ushort))
		{
			succeeded = Read<ushort>(TargetMemory.TryReadUInt16, address, out boxed, out failure);
		}
		else if (typeof(T) == typeof(short))
		{
			succeeded = Read<short>(TargetMemory.TryReadInt16, address, out boxed, out failure);
		}
		else if (typeof(T) == typeof(uint))
		{
			succeeded = Read<uint>(TargetMemory.TryReadUInt32, address, out boxed, out failure);
		}
		else if (typeof(T) == typeof(int))
		{
			succeeded = Read<int>(TargetMemory.TryReadInt32, address, out boxed, out failure);
		}
		else if (typeof(T) == typeof(ulong))
		{
			succeeded = Read<ulong>(TargetMemory.TryReadUInt64, address, out boxed, out failure);
		}
		else if (typeof(T) == typeof(long))
		{
			succeeded = Read<long>(TargetMemory.TryReadInt64, address, out boxed, out failure);
		}
		else if (typeof(T) == typeof(float))
		{
			succeeded = Read<float>(TargetMemory.TryReadSingle, address, out boxed, out failure);
		}
		else if (typeof(T) == typeof(double))
		{
			succeeded = Read<double>(TargetMemory.TryReadDouble, address, out boxed, out failure);
		}
		else if (typeof(T) == typeof(Address))
		{
			succeeded = Read<Address>(TargetMemory.TryReadPointer, address, out boxed, out failure);
		}
		else
		{
			value = default;
			failure = null;
			return false;
		}

		if (succeeded && boxed is T typed)
		{
			value = typed;
			return true;
		}

		value = default;
		return false;
	}

	private static bool TryWriteKnown<T>(Address address, T value, out bool handled, out string? failure)
	{
		handled = true;
		if (typeof(T) == typeof(byte))
		{
			return Write(TargetMemory.TryWriteUInt8, address, (byte) (object) value!, out failure);
		}

		if (typeof(T) == typeof(sbyte))
		{
			return Write(TargetMemory.TryWriteInt8, address, (sbyte) (object) value!, out failure);
		}

		if (typeof(T) == typeof(ushort))
		{
			return Write(TargetMemory.TryWriteUInt16, address, (ushort) (object) value!, out failure);
		}

		if (typeof(T) == typeof(short))
		{
			return Write(TargetMemory.TryWriteInt16, address, (short) (object) value!, out failure);
		}

		if (typeof(T) == typeof(uint))
		{
			return Write(TargetMemory.TryWriteUInt32, address, (uint) (object) value!, out failure);
		}

		if (typeof(T) == typeof(int))
		{
			return Write(TargetMemory.TryWriteInt32, address, (int) (object) value!, out failure);
		}

		if (typeof(T) == typeof(ulong))
		{
			return Write(TargetMemory.TryWriteUInt64, address, (ulong) (object) value!, out failure);
		}

		if (typeof(T) == typeof(long))
		{
			return Write(TargetMemory.TryWriteInt64, address, (long) (object) value!, out failure);
		}

		if (typeof(T) == typeof(float))
		{
			return Write(TargetMemory.TryWriteSingle, address, (float) (object) value!, out failure);
		}

		if (typeof(T) == typeof(double))
		{
			return Write(TargetMemory.TryWriteDouble, address, (double) (object) value!, out failure);
		}

		if (typeof(T) == typeof(Address))
		{
			return Write(TargetMemory.TryWritePointer, address, (Address) (object) value!, out failure);
		}

		handled = false;
		failure = null;
		return false;
	}

	private static bool Read<T>(Reader<T> reader, Address address, out object? value, out string? failure)
	{
		if (reader(address, out T result, out MemoryAccessFailure sdkFailure))
		{
			value = result;
			failure = null;
			return true;
		}

		value = null;
		failure = sdkFailure.ToString();
		return false;
	}

	private static bool Write<T>(Writer<T> writer, Address address, T value, out string? failure)
	{
		if (writer(address, value, out MemoryAccessFailure sdkFailure))
		{
			failure = null;
			return true;
		}

		failure = sdkFailure.ToString();
		return false;
	}

	private static bool TryMapMemoryFailure(bool succeeded, bool isWrite, string operation, string? hostFailure,
		out CheatEngineFailure failure)
	{
		if (succeeded)
		{
			failure = default;
			return true;
		}

		failure = new CheatEngineFailure(
			isWrite ? CheatEngineFailureKind.MemoryWriteFailed : CheatEngineFailureKind.MemoryReadFailed,
			operation,
			hostFailure ?? "Cheat Engine rejected the target-memory operation.");
		return false;
	}

	private delegate bool Reader<T>(Address address, out T value, out MemoryAccessFailure failure);

	private delegate bool Writer<in T>(Address address, T value, out MemoryAccessFailure failure);

	private sealed class TargetMemoryCodecContext : IMemoryReadContext, IMemoryWriteContext
	{
		private int _pointerSize;

		internal string? Failure
		{
			get;
			private set;
		}

		public int PointerSize
		{
			get
			{
				if (_pointerSize != 0)
				{
					return _pointerSize;
				}

				int pointerSize = ClientLuaGlobals.TargetIs64Bit() ? sizeof(ulong) : sizeof(uint);
				_pointerSize = pointerSize;
				return pointerSize;
			}
		}

		public bool TryReadBytes(Address address, Span<byte> destination)
		{
			if (TargetMemory.TryReadBytes(address, destination, out MemoryAccessFailure sdkFailure))
			{
				Failure = null;
				return true;
			}

			Failure = sdkFailure.ToString();
			return false;
		}

		public bool TryWriteBytes(Address address, ReadOnlySpan<byte> source)
		{
			if (TargetMemory.TryWriteBytes(address, source, out MemoryAccessFailure sdkFailure))
			{
				Failure = null;
				return true;
			}

			Failure = sdkFailure.ToString();
			return false;
		}

		internal static TargetMemoryCodecContext Create()
		{
			return new TargetMemoryCodecContext();
		}
	}
}
