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

internal sealed class MemoryClient : IMemoryClient, IMemoryBatchClient
{
	private readonly IMemoryCodecContextPort _codecContextPort;
	private readonly ICheatEngineDispatcher _dispatcher;

	private readonly CoreLifetime _lifetime;
	private readonly MemoryResourceLimits _limits;

	internal MemoryClient(ICheatEngineDispatcher dispatcher, CoreLifetime lifetime)
		: this(dispatcher, lifetime, SdkMemoryCodecContextPort.Instance, new MemoryResourceLimits())
	{
	}

	internal MemoryClient(ICheatEngineDispatcher dispatcher, CoreLifetime lifetime, MemoryResourceLimits limits)
		: this(dispatcher, lifetime, SdkMemoryCodecContextPort.Instance, limits)
	{
	}

	internal MemoryClient(
		ICheatEngineDispatcher dispatcher,
		CoreLifetime lifetime,
		IMemoryCodecContextPort codecContextPort)
		: this(dispatcher, lifetime, codecContextPort, new MemoryResourceLimits())
	{
	}

	internal MemoryClient(
		ICheatEngineDispatcher dispatcher,
		CoreLifetime lifetime,
		IMemoryCodecContextPort codecContextPort,
		MemoryResourceLimits limits)
	{
		_dispatcher = dispatcher ?? throw new ArgumentNullException(nameof(dispatcher));
		_lifetime = lifetime ?? throw new ArgumentNullException(nameof(lifetime));
		_codecContextPort = codecContextPort ?? throw new ArgumentNullException(nameof(codecContextPort));
		_limits = (limits ?? throw new ArgumentNullException(nameof(limits))).CreateSnapshot();
	}

	public MemoryPrimitiveBatchReadOutcome<T> ReadPrimitiveBatchDetailed<T>(MemoryPrimitiveBatchReadRequest<T> request,
		CancellationToken cancellationToken = default)
	{
		ValidateBatch(request.Addresses, nameof(request));
		int attemptedCount = request.Addresses.Length;
		if (!TryAdmitBatch<T>(attemptedCount, false, "Memory.ReadPrimitiveBatch",
				out CheatEngineFailure admissionFailure))
		{
			return new MemoryPrimitiveBatchReadOutcome<T>(attemptedCount, 0, null, admissionFailure, []);
		}

		PrimitiveBatchReadInput<T> input = new(request, _codecContextPort);
		if (!TryInvoke(input, static current => PrimitiveMemoryCodec<T>.ReadBatch(current.Request, current.Port),
				out PrimitiveBatchReadOutcome<T> outcome, out CheatEngineFailure dispatchFailure, cancellationToken))
		{
			return new MemoryPrimitiveBatchReadOutcome<T>(attemptedCount, 0, null, dispatchFailure, []);
		}

		if (outcome.Succeeded)
		{
			return new MemoryPrimitiveBatchReadOutcome<T>(attemptedCount, attemptedCount, null, null, outcome.Values);
		}

		CheatEngineFailure failure =
			CreateBatchFailure<T>(outcome.Handled, false, outcome.FailedIndex, outcome.Failure);
		return new MemoryPrimitiveBatchReadOutcome<T>(attemptedCount, outcome.FailedIndex, outcome.FailedIndex,
			failure, outcome.Values);
	}

	public MemoryPrimitiveBatchWriteOutcome WritePrimitiveBatchDetailed<T>(MemoryPrimitiveBatchWriteRequest<T> request,
		CancellationToken cancellationToken = default)
	{
		ValidateBatch(request.Values, nameof(request));
		int attemptedCount = request.Values.Length;
		if (!TryAdmitBatch<T>(attemptedCount, true, "Memory.WritePrimitiveBatch",
				out CheatEngineFailure admissionFailure))
		{
			return new MemoryPrimitiveBatchWriteOutcome(attemptedCount, 0, null, admissionFailure,
				MemoryBatchWriteEffectState.NotStarted);
		}

		PrimitiveBatchWriteInput<T> input = new(request, _codecContextPort);
		if (!TryInvoke(input, static current => PrimitiveMemoryCodec<T>.WriteBatch(current.Request, current.Port),
				out PrimitiveBatchWriteOutcome outcome, out CheatEngineFailure dispatchFailure, cancellationToken))
		{
			return new MemoryPrimitiveBatchWriteOutcome(attemptedCount, 0, null, dispatchFailure,
				MemoryBatchWriteEffectState.Unknown);
		}

		if (outcome.Succeeded)
		{
			return new MemoryPrimitiveBatchWriteOutcome(attemptedCount, attemptedCount, null, null,
				MemoryBatchWriteEffectState.Complete);
		}

		CheatEngineFailure failure = CreateBatchFailure<T>(outcome.Handled, true, outcome.FailedIndex, outcome.Failure);
		MemoryBatchWriteEffectState effectState = outcome.FailedIndex == 0
			? MemoryBatchWriteEffectState.NotStarted
			: MemoryBatchWriteEffectState.Partial;
		return new MemoryPrimitiveBatchWriteOutcome(attemptedCount, outcome.FailedIndex, outcome.FailedIndex, failure,
			effectState);
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
		MemoryPrimitiveBatchReadOutcome<T> outcome = ReadPrimitiveBatchDetailed(request, cancellationToken);
		if (outcome.Succeeded)
		{
			values = outcome.ReadPrefix;
			failure = default;
			return true;
		}

		values = [];
		failure = outcome.Cause!.Value;
		return false;
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
		MemoryPrimitiveBatchWriteOutcome outcome = WritePrimitiveBatchDetailed(request, cancellationToken);
		if (outcome.Succeeded)
		{
			failure = default;
			return true;
		}

		failure = outcome.Cause!.Value;
		return false;
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
		if (!TryAdmitPayload(request.Length, _limits.MaximumReadBytes, false, "Memory.ReadBytes", "byte read",
				out failure))
		{
			bytes = [];
			return false;
		}

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

		if (!TryAdmitPayload(request.Bytes.Length, _limits.MaximumWriteBytes, true, "Memory.WriteBytes", "byte write",
				out failure))
		{
			return false;
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
		if (!TryAdmitPayload(GetEncodedByteLength(request.MaximumLength, request.WideCharacter),
				_limits.MaximumStringBytes, false, "Memory.ReadString", "string read", out failure))
		{
			value = null;
			return false;
		}

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
		int encodedLength = GetEncodedLength(request.Value, request.WideCharacter);
		if (request.MaximumLength > 0 && encodedLength > request.MaximumLength)
		{
			throw new ArgumentException("The encoded text exceeds the explicit maximum length.", nameof(request));
		}

		if (!TryAdmitPayload(GetEncodedByteLength(request.Value, request.WideCharacter), _limits.MaximumStringBytes,
				true, "Memory.WriteString", "string write", out failure))
		{
			return false;
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
		TargetMemoryCodecContext context = TargetMemoryCodecContext.Create(_lifetime, _dispatcher, _codecContextPort,
			_limits);
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
		TargetMemoryCodecContext context = TargetMemoryCodecContext.Create(_lifetime, _dispatcher, _codecContextPort,
			_limits);
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

	private bool TryAdmitBatch<T>(int attemptedCount, bool isWrite, string operation, out CheatEngineFailure failure)
	{
		if (attemptedCount > _limits.MaximumBatchOperationCount)
		{
			failure = CreateLimitFailure(isWrite, operation, "batch operation count", attemptedCount,
				_limits.MaximumBatchOperationCount);
			return false;
		}

		return TryAdmitPayload(PrimitiveMemoryCodec<T>.GetPayloadBytes(attemptedCount),
			_limits.MaximumBatchPayloadBytes, isWrite, operation, "batch payload", out failure);
	}

	private static bool TryAdmitPayload(long requestedBytes, int limit, bool isWrite, string operation, string resource,
		out CheatEngineFailure failure)
	{
		if (requestedBytes > limit)
		{
			failure = CreateLimitFailure(isWrite, operation, resource, requestedBytes, limit);
			return false;
		}

		failure = default;
		return true;
	}

	private static CheatEngineFailure CreateLimitFailure(bool isWrite, string operation, string resource,
		long requestedBytes, int limit)
	{
		string direction = isWrite ? "write" : "read";
		string unit = resource == "batch operation count" ? "operations" : "bytes";
		return new CheatEngineFailure(CheatEngineFailureKind.OperationRejected, operation,
			$"The requested {resource} of {requestedBytes} {unit} exceeds the activation {direction} budget of {limit} {unit}.");
	}

	private static CheatEngineFailure CreateBatchFailure<T>(bool handled, bool isWrite, int failedIndex,
		string? hostFailure)
	{
		string operation = isWrite ? "Memory.WritePrimitiveBatch" : "Memory.ReadPrimitiveBatch";
		if (!handled)
		{
			return new CheatEngineFailure(CheatEngineFailureKind.Unsupported, operation,
				$"'{typeof(T).FullName}' is not a built-in CheatEngine.Client memory type.");
		}

		CheatEngineFailureKind kind =
			isWrite ? CheatEngineFailureKind.MemoryWriteFailed : CheatEngineFailureKind.MemoryReadFailed;
		string action = isWrite ? "write" : "read";
		return new CheatEngineFailure(kind, operation,
			$"The batch {action} failed at index {failedIndex}: {hostFailure ?? "Cheat Engine rejected the target-memory operation."}");
	}

	private static int GetEncodedLength(string value, bool wideCharacter)
	{
		return wideCharacter ? value.Length : Encoding.UTF8.GetByteCount(value);
	}

	private static long GetEncodedByteLength(int length, bool wideCharacter)
	{
		return wideCharacter ? (long) length * sizeof(char) : length;
	}

	private static long GetEncodedByteLength(string value, bool wideCharacter)
	{
		return wideCharacter ? (long) value.Length * sizeof(char) : Encoding.UTF8.GetByteCount(value);
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

	private readonly record struct PrimitiveWriteInput<T>(Address Address, T Value);

	private readonly record struct PrimitiveReadOutcome<T>(bool Handled, bool Succeeded, T Value, string? Failure);

	private readonly record struct PrimitiveWriteOutcome(bool Handled, bool Succeeded, string? Failure);

	private readonly record struct PrimitiveBatchReadInput<T>(
		MemoryPrimitiveBatchReadRequest<T> Request,
		IMemoryCodecContextPort Port);

	private readonly record struct PrimitiveBatchWriteInput<T>(
		MemoryPrimitiveBatchWriteRequest<T> Request,
		IMemoryCodecContextPort Port);

	private readonly record struct PrimitiveBatchReadOutcome<T>(
		bool Handled,
		bool Succeeded,
		T[] Values,
		int FailedIndex,
		string? Failure);

	private readonly record struct PrimitiveBatchWriteOutcome(
		bool Handled,
		bool Succeeded,
		int FailedIndex,
		string? Failure);

	private static class PrimitiveMemoryCodec<T>
	{
		private static bool IsSupported =>
			typeof(T) == typeof(byte) ||
			typeof(T) == typeof(sbyte) ||
			typeof(T) == typeof(ushort) ||
			typeof(T) == typeof(short) ||
			typeof(T) == typeof(uint) ||
			typeof(T) == typeof(int) ||
			typeof(T) == typeof(ulong) ||
			typeof(T) == typeof(long) ||
			typeof(T) == typeof(float) ||
			typeof(T) == typeof(double) ||
			typeof(T) == typeof(Address);

		internal static PrimitiveReadOutcome<T> Read(Address address)
		{
			return Read(SdkMemoryCodecContextPort.Instance, address);
		}

		internal static PrimitiveReadOutcome<T> Read(IMemoryCodecContextPort port, Address address)
		{
			if (!IsSupported)
			{
				return new PrimitiveReadOutcome<T>(false, false, default!, null);
			}

			return port.TryReadPrimitive(address, out T value, out string? failure)
				? new PrimitiveReadOutcome<T>(true, true, value, null)
				: new PrimitiveReadOutcome<T>(true, false, default!, failure);
		}

		internal static PrimitiveWriteOutcome Write(Address address, T value)
		{
			return Write(SdkMemoryCodecContextPort.Instance, address, value);
		}

		internal static PrimitiveWriteOutcome Write(IMemoryCodecContextPort port, Address address, T value)
		{
			if (!IsSupported)
			{
				return new PrimitiveWriteOutcome(false, false, null);
			}

			return port.TryWritePrimitive(address, value, out string? failure)
				? new PrimitiveWriteOutcome(true, true, null)
				: new PrimitiveWriteOutcome(true, false, failure);
		}

		internal static long GetPayloadBytes(int operationCount)
		{
			return (long) Unsafe.SizeOf<T>() * operationCount;
		}

		internal static PrimitiveBatchReadOutcome<T> ReadBatch(MemoryPrimitiveBatchReadRequest<T> request,
			IMemoryCodecContextPort port)
		{
			if (!IsSupported)
			{
				return new PrimitiveBatchReadOutcome<T>(false, false, [], 0, null);
			}

			T[] values = new T[request.Addresses.Length];
			for (int index = 0; index < request.Addresses.Length; index++)
			{
				PrimitiveReadOutcome<T> current = Read(port, request.Addresses[index]);
				if (!current.Succeeded)
				{
					return new PrimitiveBatchReadOutcome<T>(current.Handled, false, values[..index], index,
						current.Failure);
				}

				values[index] = current.Value;
			}

			return new PrimitiveBatchReadOutcome<T>(true, true, values, -1, null);
		}

		internal static PrimitiveBatchWriteOutcome WriteBatch(MemoryPrimitiveBatchWriteRequest<T> request,
			IMemoryCodecContextPort port)
		{
			if (!IsSupported)
			{
				return new PrimitiveBatchWriteOutcome(false, false, 0, null);
			}

			for (int index = 0; index < request.Values.Length; index++)
			{
				MemoryAddressValue<T> current = request.Values[index];
				PrimitiveWriteOutcome outcome = Write(port, current.Address, current.Value);
				if (!outcome.Succeeded)
				{
					return new PrimitiveBatchWriteOutcome(outcome.Handled, false, index, outcome.Failure);
				}
			}

			return new PrimitiveBatchWriteOutcome(true, true, -1, null);
		}
	}

	private sealed class TargetMemoryCodecContext : IMemoryReadContext, IMemoryWriteContext
	{
		private const string _operation = "Memory.CodecContext";

		private readonly long _activationEpoch;
		private readonly ICheatEngineDispatcher _dispatcher;
		private readonly CoreLifetime _lifetime;
		private readonly MemoryResourceLimits _limits;
		private readonly IMemoryCodecContextPort _port;
		private readonly int _threadId;
		private int _expired;
		private int _pointerSize;
		private int _readBytesAdmitted;
		private int _writeBytesAdmitted;

		private TargetMemoryCodecContext(
			CoreLifetime lifetime,
			ICheatEngineDispatcher dispatcher,
			IMemoryCodecContextPort port,
			MemoryResourceLimits limits)
		{
			_lifetime = lifetime ?? throw new ArgumentNullException(nameof(lifetime));
			_dispatcher = dispatcher ?? throw new ArgumentNullException(nameof(dispatcher));
			_port = port ?? throw new ArgumentNullException(nameof(port));
			_limits = limits ?? throw new ArgumentNullException(nameof(limits));
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
			if (!TryAdmitCodecBytes(destination.Length, _limits.MaximumReadBytes, ref _readBytesAdmitted, "read"))
			{
				return false;
			}

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
			if (!TryAdmitCodecBytes(source.Length, _limits.MaximumWriteBytes, ref _writeBytesAdmitted, "write"))
			{
				return false;
			}

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
			IMemoryCodecContextPort port,
			MemoryResourceLimits limits)
		{
			return new TargetMemoryCodecContext(lifetime, dispatcher, port, limits);
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

		private bool TryAdmitCodecBytes(int requestedBytes, int limit, ref int admittedBytes, string direction)
		{
			if (requestedBytes > limit - admittedBytes)
			{
				Failure = $"The memory codec {direction} exceeds the activation {direction} budget of {limit} bytes.";
				return false;
			}

			admittedBytes += requestedBytes;
			return true;
		}
	}
}
