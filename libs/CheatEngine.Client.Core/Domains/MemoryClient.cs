using System.Collections.Immutable;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text;

using CheatEngine.Client.Core.Dispatching;
using CheatEngine.Client.Core.Infrastructure;
using CheatEngine.Client.Dispatching;
using CheatEngine.Client.Memory;
using CheatEngine.Client.Results;
using CheatEngine.SDK.Engine.Memory;
using CheatEngine.SDK.Engine.Values;

namespace CheatEngine.Client.Core.Domains;

internal sealed class MemoryClient : IMemoryClient
{
	private readonly ICheatEngineDispatcher _dispatcher;

	private readonly CoreLifetime _lifetime;

	private readonly IMemoryCodecContextPort _codecContextPort;

	internal MemoryClient(ICheatEngineDispatcher dispatcher, CoreLifetime lifetime)
		: this(dispatcher, lifetime, SdkMemoryCodecContextPort.Instance)
	{
	}

	internal MemoryClient(
		ICheatEngineDispatcher dispatcher,
		CoreLifetime lifetime,
		IMemoryCodecContextPort codecContextPort)
	{
		_dispatcher = dispatcher ?? throw new ArgumentNullException(nameof(dispatcher));
		_lifetime = lifetime ?? throw new ArgumentNullException(nameof(lifetime));
		_codecContextPort = codecContextPort ?? throw new ArgumentNullException(nameof(codecContextPort));
	}

	public bool TryReadPrimitive<T>(Address address, [MaybeNullWhen(false)] out T value,
		out CheatEngineFailure failure, CancellationToken cancellationToken = default)
	{
		if (!TryInvoke(address, static current => PrimitiveMemoryCodec<T>.Read(current),
			    out PrimitiveReadOutcome<T> outcome, out failure, cancellationToken))
		{
			value = default;
			return false;
		}

		if (!outcome.Succeeded)
		{
			value = default;
			failure = !outcome.Handled
				? new CheatEngineFailure(CheatEngineFailureKind.Unsupported, "Memory.ReadPrimitive",
					$"'{typeof(T).FullName}' is not a built-in CheatEngine.Client memory type.")
				: new CheatEngineFailure(CheatEngineFailureKind.MemoryReadFailed, "Memory.ReadPrimitive",
					outcome.Failure ?? "Cheat Engine rejected the target-memory read.");
			return false;
		}

		value = outcome.Value;
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
		PrimitiveWriteInput<T> input = new(address, value);
		if (!TryInvoke(input, static current => PrimitiveMemoryCodec<T>.Write(current.Address, current.Value),
			    out PrimitiveWriteOutcome outcome, out failure, cancellationToken))
		{
			return false;
		}

		if (!outcome.Succeeded)
		{
			failure = !outcome.Handled
				? new CheatEngineFailure(CheatEngineFailureKind.Unsupported, "Memory.WritePrimitive",
					$"'{typeof(T).FullName}' is not a built-in CheatEngine.Client memory type.")
				: new CheatEngineFailure(CheatEngineFailureKind.MemoryWriteFailed, "Memory.WritePrimitive",
					outcome.Failure ?? "Cheat Engine rejected the target-memory write.");
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

	public bool TryReadPrimitiveBatch<T>(MemoryPrimitiveBatchReadRequest<T> request, out ImmutableArray<T> values,
		out CheatEngineFailure failure, CancellationToken cancellationToken = default)
	{
		ValidateBatch(request.Addresses, nameof(request));
		if (!TryInvoke(request, static current => PrimitiveMemoryCodec<T>.ReadBatch(current),
			    out PrimitiveBatchReadOutcome<T> outcome, out failure, cancellationToken))
		{
			values = [];
			return false;
		}

		if (!outcome.Succeeded)
		{
			values = [];
			failure = !outcome.Handled
				? new CheatEngineFailure(CheatEngineFailureKind.Unsupported, "Memory.ReadPrimitiveBatch",
					$"'{typeof(T).FullName}' is not a built-in CheatEngine.Client memory type.")
				: new CheatEngineFailure(CheatEngineFailureKind.MemoryReadFailed, "Memory.ReadPrimitiveBatch",
					$"The batch read failed at index {outcome.FailedIndex}: {outcome.Failure}");
			return false;
		}

		values = ImmutableCollectionsMarshal.AsImmutableArray(outcome.Values!);
		failure = default;
		return true;
	}

	public ImmutableArray<T> ReadPrimitiveBatch<T>(MemoryPrimitiveBatchReadRequest<T> request,
		CancellationToken cancellationToken = default)
	{
		if (TryReadPrimitiveBatch(request, out ImmutableArray<T> values, out CheatEngineFailure failure,
			    cancellationToken))
		{
			return values;
		}

		failure.Throw();
		return [];
	}

	public bool TryWritePrimitiveBatch<T>(MemoryPrimitiveBatchWriteRequest<T> request,
		out CheatEngineFailure failure, CancellationToken cancellationToken = default)
	{
		ValidateBatch(request.Values, nameof(request));
		if (!TryInvoke(request, static current => PrimitiveMemoryCodec<T>.WriteBatch(current),
			    out PrimitiveBatchWriteOutcome outcome, out failure, cancellationToken))
		{
			return false;
		}

		if (!outcome.Succeeded)
		{
			failure = !outcome.Handled
				? new CheatEngineFailure(CheatEngineFailureKind.Unsupported, "Memory.WritePrimitiveBatch",
					$"'{typeof(T).FullName}' is not a built-in CheatEngine.Client memory type.")
				: new CheatEngineFailure(CheatEngineFailureKind.MemoryWriteFailed, "Memory.WritePrimitiveBatch",
					$"The batch write failed at index {outcome.FailedIndex}: {outcome.Failure}");
			return false;
		}

		failure = default;
		return true;
	}

	public void WritePrimitiveBatch<T>(MemoryPrimitiveBatchWriteRequest<T> request,
		CancellationToken cancellationToken = default)
	{
		if (!TryWritePrimitiveBatch(request, out CheatEngineFailure failure, cancellationToken))
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
				    captured = ImmutableCollectionsMarshal.AsImmutableArray(buffer);
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
		if (request.MaximumLength > 0 && GetEncodedLength(request.Value, request.WideCharacter) > request.MaximumLength)
		{
			throw new ArgumentException("The encoded text exceeds the explicit maximum length.", nameof(request));
		}

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

	private bool TryInvoke<TState, TResult>(TState state, Func<TState, TResult> callback,
		[MaybeNullWhen(false)] out TResult result, out CheatEngineFailure failure,
		CancellationToken cancellationToken)
	{
		ArgumentNullException.ThrowIfNull(callback);
		if (_dispatcher is IStatefulCheatEngineDispatcher statefulDispatcher)
		{
			return statefulDispatcher.TryInvoke(state, callback, out result, out failure, cancellationToken);
		}

		return _dispatcher.TryInvoke(() => callback(state), out result, out failure, cancellationToken);
	}

	private bool TryReadCore<T>(MemoryReadRequest<T> request, [MaybeNullWhen(false)] out T value,
		out string? failure)
	{
		failure = null;
		TargetMemoryCodecContext context = TargetMemoryCodecContext.Create(_lifetime, _dispatcher, _codecContextPort);
		try
		{
			if (request.Codec.TryRead(context, request.Address, out value))
			{
				return true;
			}

			failure = context.Failure ?? $"The codec for '{typeof(T).Name}' rejected the target-memory read.";
			return false;
		}
		finally
		{
			context.Expire();
		}
	}

	private bool TryWriteCore<T>(MemoryWriteRequest<T> request, out string? failure)
	{
		failure = null;
		TargetMemoryCodecContext context = TargetMemoryCodecContext.Create(_lifetime, _dispatcher, _codecContextPort);
		try
		{
			T value = request.Value;
			if (request.Codec.TryWrite(context, request.Address, in value))
			{
				return true;
			}

			failure = context.Failure ?? $"The codec for '{typeof(T).Name}' rejected the target-memory write.";
			return false;
		}
		finally
		{
			context.Expire();
		}
	}

	private static void ValidateBatch<T>(ImmutableArray<T> values, string parameterName)
	{
		if (values.IsDefaultOrEmpty)
		{
			throw new ArgumentException("A memory batch requires at least one operation.", parameterName);
		}

		if (values.Length > MemoryBatchLimits.MaximumOperations)
		{
			throw new ArgumentOutOfRangeException(parameterName,
				$"A memory batch is limited to {MemoryBatchLimits.MaximumOperations} operations.");
		}
	}

	private static int GetEncodedLength(string value, bool wideCharacter)
	{
		return wideCharacter ? value.Length : Encoding.UTF8.GetByteCount(value);
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

	private static TTo Reinterpret<TFrom, TTo>(TFrom value)
	{
		return Unsafe.As<TFrom, TTo>(ref value);
	}

	private readonly record struct PrimitiveWriteInput<T>(Address Address, T Value);

	private readonly record struct PrimitiveReadOutcome<T>(bool Handled, bool Succeeded, T Value, string? Failure);

	private readonly record struct PrimitiveWriteOutcome(bool Handled, bool Succeeded, string? Failure);

	private readonly record struct PrimitiveBatchReadOutcome<T>(
		bool Handled,
		bool Succeeded,
		T[]? Values,
		int FailedIndex,
		string? Failure);

	private readonly record struct PrimitiveBatchWriteOutcome(
		bool Handled,
		bool Succeeded,
		int FailedIndex,
		string? Failure);

	private static class PrimitiveMemoryCodec<T>
	{
		internal static PrimitiveReadOutcome<T> Read(Address address)
		{
			if (typeof(T) == typeof(byte))
			{
				return Read<byte>(TargetMemory.TryReadUInt8, address);
			}

			if (typeof(T) == typeof(sbyte))
			{
				return Read<sbyte>(TargetMemory.TryReadInt8, address);
			}

			if (typeof(T) == typeof(ushort))
			{
				return Read<ushort>(TargetMemory.TryReadUInt16, address);
			}

			if (typeof(T) == typeof(short))
			{
				return Read<short>(TargetMemory.TryReadInt16, address);
			}

			if (typeof(T) == typeof(uint))
			{
				return Read<uint>(TargetMemory.TryReadUInt32, address);
			}

			if (typeof(T) == typeof(int))
			{
				return Read<int>(TargetMemory.TryReadInt32, address);
			}

			if (typeof(T) == typeof(ulong))
			{
				return Read<ulong>(TargetMemory.TryReadUInt64, address);
			}

			if (typeof(T) == typeof(long))
			{
				return Read<long>(TargetMemory.TryReadInt64, address);
			}

			if (typeof(T) == typeof(float))
			{
				return Read<float>(TargetMemory.TryReadSingle, address);
			}

			if (typeof(T) == typeof(double))
			{
				return Read<double>(TargetMemory.TryReadDouble, address);
			}

			if (typeof(T) == typeof(Address))
			{
				return Read<Address>(TargetMemory.TryReadPointer, address);
			}

			return new PrimitiveReadOutcome<T>(false, false, default!, null);
		}

		internal static PrimitiveWriteOutcome Write(Address address, T value)
		{
			if (typeof(T) == typeof(byte))
			{
				return Write<byte>(TargetMemory.TryWriteUInt8, address, value);
			}

			if (typeof(T) == typeof(sbyte))
			{
				return Write<sbyte>(TargetMemory.TryWriteInt8, address, value);
			}

			if (typeof(T) == typeof(ushort))
			{
				return Write<ushort>(TargetMemory.TryWriteUInt16, address, value);
			}

			if (typeof(T) == typeof(short))
			{
				return Write<short>(TargetMemory.TryWriteInt16, address, value);
			}

			if (typeof(T) == typeof(uint))
			{
				return Write<uint>(TargetMemory.TryWriteUInt32, address, value);
			}

			if (typeof(T) == typeof(int))
			{
				return Write<int>(TargetMemory.TryWriteInt32, address, value);
			}

			if (typeof(T) == typeof(ulong))
			{
				return Write<ulong>(TargetMemory.TryWriteUInt64, address, value);
			}

			if (typeof(T) == typeof(long))
			{
				return Write<long>(TargetMemory.TryWriteInt64, address, value);
			}

			if (typeof(T) == typeof(float))
			{
				return Write<float>(TargetMemory.TryWriteSingle, address, value);
			}

			if (typeof(T) == typeof(double))
			{
				return Write<double>(TargetMemory.TryWriteDouble, address, value);
			}

			if (typeof(T) == typeof(Address))
			{
				return Write<Address>(TargetMemory.TryWritePointer, address, value);
			}

			return new PrimitiveWriteOutcome(false, false, null);
		}

		internal static PrimitiveBatchReadOutcome<T> ReadBatch(MemoryPrimitiveBatchReadRequest<T> request)
		{
			T[] values = new T[request.Addresses.Length];
			for (int index = 0; index < request.Addresses.Length; index++)
			{
				PrimitiveReadOutcome<T> current = Read(request.Addresses[index]);
				if (!current.Succeeded)
				{
					return new PrimitiveBatchReadOutcome<T>(current.Handled, false, null, index, current.Failure);
				}

				values[index] = current.Value;
			}

			return new PrimitiveBatchReadOutcome<T>(true, true, values, -1, null);
		}

		internal static PrimitiveBatchWriteOutcome WriteBatch(MemoryPrimitiveBatchWriteRequest<T> request)
		{
			for (int index = 0; index < request.Values.Length; index++)
			{
				MemoryAddressValue<T> current = request.Values[index];
				PrimitiveWriteOutcome outcome = Write(current.Address, current.Value);
				if (!outcome.Succeeded)
				{
					return new PrimitiveBatchWriteOutcome(outcome.Handled, false, index, outcome.Failure);
				}
			}

			return new PrimitiveBatchWriteOutcome(true, true, -1, null);
		}

		private static PrimitiveReadOutcome<T> Read<TValue>(Reader<TValue> reader, Address address)
		{
			if (reader(address, out TValue value, out MemoryAccessFailure failure))
			{
				return new PrimitiveReadOutcome<T>(true, true, Reinterpret<TValue, T>(value), null);
			}

			return new PrimitiveReadOutcome<T>(true, false, default!, failure.ToString());
		}

		private static PrimitiveWriteOutcome Write<TValue>(Writer<TValue> writer, Address address, T value)
		{
			TValue targetValue = Reinterpret<T, TValue>(value);
			return writer(address, targetValue, out MemoryAccessFailure failure)
				? new PrimitiveWriteOutcome(true, true, null)
				: new PrimitiveWriteOutcome(true, false, failure.ToString());
		}
	}

	private delegate bool Reader<T>(Address address, out T value, out MemoryAccessFailure failure);

	private delegate bool Writer<in T>(Address address, T value, out MemoryAccessFailure failure);

	private sealed class TargetMemoryCodecContext : IMemoryReadContext, IMemoryWriteContext
	{
		private const string _operation = "Memory.CodecContext";

		private readonly long _activationEpoch;
		private readonly ICheatEngineDispatcher _dispatcher;
		private readonly CoreLifetime _lifetime;
		private readonly IMemoryCodecContextPort _port;
		private readonly int _threadId;
		private int _expired;
		private int _pointerSize;

		private TargetMemoryCodecContext(
			CoreLifetime lifetime,
			ICheatEngineDispatcher dispatcher,
			IMemoryCodecContextPort port)
		{
			_lifetime = lifetime ?? throw new ArgumentNullException(nameof(lifetime));
			_dispatcher = dispatcher ?? throw new ArgumentNullException(nameof(dispatcher));
			_port = port ?? throw new ArgumentNullException(nameof(port));
			_activationEpoch = lifetime.Epoch;
			_threadId = Environment.CurrentManagedThreadId;
		}

		internal string? Failure
		{
			get;
			private set;
		}

		public int PointerSize
		{
			get
			{
				ThrowIfUnusable();
				if (_pointerSize != 0)
				{
					return _pointerSize;
				}

				int pointerSize = _port.IsTarget64Bit() ? sizeof(ulong) : sizeof(uint);
				_pointerSize = pointerSize;
				return pointerSize;
			}
		}

		public bool TryReadBytes(Address address, Span<byte> destination)
		{
			ThrowIfUnusable();
			if (_port.TryReadBytes(address, destination, out string? failure))
			{
				Failure = null;
				return true;
			}

			Failure = failure;
			return false;
		}

		public bool TryWriteBytes(Address address, ReadOnlySpan<byte> source)
		{
			ThrowIfUnusable();
			if (_port.TryWriteBytes(address, source, out string? failure))
			{
				Failure = null;
				return true;
			}

			Failure = failure;
			return false;
		}

		internal static TargetMemoryCodecContext Create(
			CoreLifetime lifetime,
			ICheatEngineDispatcher dispatcher,
			IMemoryCodecContextPort port)
		{
			return new TargetMemoryCodecContext(lifetime, dispatcher, port);
		}

		internal void Expire()
		{
			Volatile.Write(ref _expired, 1);
		}

		private void ThrowIfUnusable()
		{
			if (Volatile.Read(ref _expired) != 0 ||
			    _activationEpoch != _lifetime.Epoch ||
			    !_lifetime.IsActivationCurrent ||
			    Environment.CurrentManagedThreadId != _threadId ||
			    !_dispatcher.IsMainThread)
			{
				throw new CheatEngineActivationExpiredException(
					_operation,
					"The memory codec context is no longer valid for the current Cheat Engine invocation.");
			}

			_lifetime.ThrowIfInactive(_operation);
		}
	}
}
