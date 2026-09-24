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
using CheatEngine.SDK.Engine.Processes;
using CheatEngine.SDK.Engine.Runtime;
using CheatEngine.SDK.Engine.Values;

namespace CheatEngine.Client.Core.Domains;

internal sealed class MemoryClient : IMemoryClient, IMemoryBatchClient
{
	/// <summary>The effect state reported for a read batch, which never changes the target.</summary>
	private const string ReadBatchEffectState = "ReadOnly";

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
		MemoryPrimitiveBatchReadOutcome<T> outcome = ReadPrimitiveBatchCore(request, cancellationToken);
		// Counts only, never an address or a value (A24-17); a read batch has no target effect.
		_lifetime.Diagnostics.MemoryBatchCompleted("Memory.ReadPrimitiveBatch", outcome.AttemptedCount,
			outcome.CompletedCount, ReadBatchEffectState);
		return outcome;
	}

	public MemoryPrimitiveBatchWriteOutcome WritePrimitiveBatchDetailed<T>(MemoryPrimitiveBatchWriteRequest<T> request,
		CancellationToken cancellationToken = default)
	{
		MemoryPrimitiveBatchWriteOutcome outcome = WritePrimitiveBatchCore(request, cancellationToken);
		_lifetime.Diagnostics.MemoryBatchCompleted("Memory.WritePrimitiveBatch", outcome.AttemptedCount,
			outcome.CompletedCount, outcome.EffectState.ToString());
		return outcome;
	}

	private MemoryPrimitiveBatchReadOutcome<T> ReadPrimitiveBatchCore<T>(MemoryPrimitiveBatchReadRequest<T> request,
		CancellationToken cancellationToken)
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

		if (outcome.WidthRefusal is { } widthRefusal)
		{
			// The whole Address batch is refused before its first read (PointerWidthPolicy).
			return new MemoryPrimitiveBatchReadOutcome<T>(attemptedCount, 0, null,
				RefuseWidth("Memory.ReadPrimitiveBatch", widthRefusal), []);
		}

		if (outcome.Succeeded)
		{
			return new MemoryPrimitiveBatchReadOutcome<T>(attemptedCount, attemptedCount, null, null, outcome.Values);
		}

		CheatEngineFailure failure = outcome.Fault is { } fault
			? SdkBoundary.Translate("Memory.ReadPrimitiveBatch", fault, CheatEngineHostEffect.Unknown, _lifetime)
			: CreateBatchFailure<T>(outcome.Handled, false, outcome.FailedIndex, outcome.Failure);
		return new MemoryPrimitiveBatchReadOutcome<T>(attemptedCount, outcome.FailedIndex, outcome.FailedIndex,
			failure, outcome.Values);
	}

	private MemoryPrimitiveBatchWriteOutcome WritePrimitiveBatchCore<T>(MemoryPrimitiveBatchWriteRequest<T> request,
		CancellationToken cancellationToken)
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
			return CreateDispatchFailureOutcome(attemptedCount, dispatchFailure);
		}

		if (outcome.WidthRefusal is { } widthRefusal)
		{
			// The whole Address batch is refused before its first write (PointerWidthPolicy): nothing was written.
			return new MemoryPrimitiveBatchWriteOutcome(attemptedCount, 0, null,
				RefuseWidth("Memory.WritePrimitiveBatch", widthRefusal),
				MemoryBatchWriteEffectState.NotStarted);
		}

		if (outcome.Succeeded)
		{
			return new MemoryPrimitiveBatchWriteOutcome(attemptedCount, attemptedCount, null, null,
				MemoryBatchWriteEffectState.Complete);
		}

		if (outcome.Fault is { } fault)
		{
			// The write at FailedIndex threw inside the SDK: whether it reached the target cannot be established.
			CheatEngineFailure faultFailure = SdkBoundary.Translate("Memory.WritePrimitiveBatch", fault,
				CheatEngineHostEffect.Unknown, _lifetime);
			return new MemoryPrimitiveBatchWriteOutcome(attemptedCount, outcome.FailedIndex, outcome.FailedIndex,
				faultFailure, MemoryBatchWriteEffectState.Unknown);
		}

		CheatEngineFailure failure = CreateBatchFailure<T>(outcome.Handled, true, outcome.FailedIndex, outcome.Failure);
		MemoryBatchWriteEffectState effectState = outcome.FailedIndex == 0
			? MemoryBatchWriteEffectState.NotStarted
			: MemoryBatchWriteEffectState.Partial;
		if (effectState == MemoryBatchWriteEffectState.Partial)
		{
			// A completed prefix persists and is never rolled back.
			failure = CoreFailureFactory.WithHostEffect(failure, CheatEngineHostEffect.Started);
		}

		return new MemoryPrimitiveBatchWriteOutcome(attemptedCount, outcome.FailedIndex, outcome.FailedIndex, failure,
			effectState);
	}

	public bool TryReadPrimitive<T>(Address address, [MaybeNullWhen(false)] out T value,
		out CheatEngineFailure failure, CancellationToken cancellationToken = default)
	{
		PrimitiveReadInput input = new(address, _codecContextPort);
		if (!TryInvoke(input, static current => PrimitiveMemoryCodec<T>.Read(current.Port, current.Address),
				out PrimitiveReadOutcome<T> outcome, out failure, cancellationToken))
		{
			value = default;
			return false;
		}

		if (!outcome.Succeeded)
		{
			value = default;
			failure = outcome.WidthRefusal is { } widthRefusal
				? RefuseWidth("Memory.ReadPrimitive", widthRefusal)
				: outcome.Fault is { } fault
					? SdkBoundary.Translate("Memory.ReadPrimitive", fault, CheatEngineHostEffect.Unknown, _lifetime)
					: !outcome.Handled
						? UnsupportedPrimitive<T>("Memory.ReadPrimitive")
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

		failure.Throw(cancellationToken);
		return default!;
	}

	public bool TryWritePrimitive<T>(Address address, T value, out CheatEngineFailure failure,
		CancellationToken cancellationToken = default)
	{
		PrimitiveWriteInput<T> input = new(address, value, _codecContextPort);
		if (!TryInvoke(input,
				static current => PrimitiveMemoryCodec<T>.Write(current.Port, current.Address, current.Value),
				out PrimitiveWriteOutcome outcome, out failure, cancellationToken))
		{
			return false;
		}

		if (!outcome.Succeeded)
		{
			failure = outcome.WidthRefusal is { } widthRefusal
				? RefuseWidth("Memory.WritePrimitive", widthRefusal)
				: outcome.Fault is { } fault
					? SdkBoundary.Translate("Memory.WritePrimitive", fault, CheatEngineHostEffect.Unknown, _lifetime)
					: !outcome.Handled
						? UnsupportedPrimitive<T>("Memory.WritePrimitive")
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
			failure.Throw(cancellationToken);
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

		failure.Throw(cancellationToken);
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
			failure.Throw(cancellationToken);
		}
	}

	public bool TryRead<T>(MemoryReadRequest<T> request, [MaybeNullWhen(false)] out T value,
		out CheatEngineFailure failure, CancellationToken cancellationToken = default)
	{
		T? captured = default;
		CodecOutcome outcome = default;
		if (!_dispatcher.TryInvoke(() => outcome = TryReadCore(request, out captured), out failure,
				cancellationToken))
		{
			value = default;
			return false;
		}

		if (!outcome.Succeeded)
		{
			value = default;
			failure = CreateCodecFailure(outcome, false, "Memory.Read");
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

		failure.Throw(cancellationToken);
		return default!;
	}

	public bool TryWrite<T>(MemoryWriteRequest<T> request, out CheatEngineFailure failure,
		CancellationToken cancellationToken = default)
	{
		CodecOutcome outcome = default;
		if (!_dispatcher.TryInvoke(() => outcome = TryWriteCore(request), out failure, cancellationToken))
		{
			return false;
		}

		if (!outcome.Succeeded)
		{
			failure = CreateCodecFailure(outcome, true, "Memory.Write");
			return false;
		}

		failure = default;
		return true;
	}

	public void Write<T>(MemoryWriteRequest<T> request, CancellationToken cancellationToken = default)
	{
		if (!TryWrite(request, out CheatEngineFailure failure, cancellationToken))
		{
			failure.Throw(cancellationToken);
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
		HostCall call = default;
		if (!_dispatcher.TryInvoke(() =>
			{
				byte[] buffer = new byte[request.Length];
				call = HostCall.Run(_codecContextPort, buffer, request.Address,
					static (port, destination, address, out hostFailure) =>
						port.TryReadBytes(address, destination, out hostFailure));
				if (call.Succeeded)
				{
					captured = ImmutableCollectionsMarshal.AsImmutableArray(buffer);
				}
			}, out failure, cancellationToken))
		{
			bytes = [];
			return false;
		}

		bytes = call.Succeeded ? captured : [];
		return TryMapMemoryFailure(call, false, "Memory.ReadBytes", out failure);
	}

	public ImmutableArray<byte> ReadBytes(MemoryBytesReadRequest request,
		CancellationToken cancellationToken = default)
	{
		if (TryReadBytes(request, out ImmutableArray<byte> bytes, out CheatEngineFailure failure, cancellationToken))
		{
			return bytes;
		}

		failure.Throw(cancellationToken);
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

		HostCall call = default;
		if (!_dispatcher.TryInvoke(() => call = HostCall.Run(_codecContextPort, request, request.Address,
				static (port, current, address, out hostFailure) =>
					port.TryWriteBytes(address, current.Bytes.AsSpan(), out hostFailure)),
				out failure, cancellationToken))
		{
			return false;
		}

		return TryMapMemoryFailure(call, true, "Memory.WriteBytes", out failure);
	}

	public void WriteBytes(MemoryBytesWriteRequest request, CancellationToken cancellationToken = default)
	{
		if (!TryWriteBytes(request, out CheatEngineFailure failure, cancellationToken))
		{
			failure.Throw(cancellationToken);
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
		HostCall call = default;
		if (!_dispatcher.TryInvoke(() => call = HostCall.Run(_codecContextPort, request, request.Address,
				(port, current, address, out hostFailure) => port.TryReadString(address,
					current.MaximumLength, current.WideCharacter, out captured, out hostFailure)),
				out failure, cancellationToken))
		{
			value = null;
			return false;
		}

		value = call.Succeeded ? captured : null;
		return TryMapMemoryFailure(call, false, "Memory.ReadString", out failure);
	}

	public string ReadString(MemoryStringReadRequest request, CancellationToken cancellationToken = default)
	{
		if (TryReadString(request, out string? value, out CheatEngineFailure failure, cancellationToken))
		{
			return value;
		}

		failure.Throw(cancellationToken);
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

		HostCall call = default;
		if (!_dispatcher.TryInvoke(() => call = HostCall.Run(_codecContextPort, request, request.Address,
				static (port, current, address, out hostFailure) => port.TryWriteString(address,
					current.Value.AsSpan(), current.WideCharacter, out hostFailure)),
				out failure, cancellationToken))
		{
			return false;
		}

		return TryMapMemoryFailure(call, true, "Memory.WriteString", out failure);
	}

	public void WriteString(MemoryStringWriteRequest request, CancellationToken cancellationToken = default)
	{
		if (!TryWriteString(request, out CheatEngineFailure failure, cancellationToken))
		{
			failure.Throw(cancellationToken);
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
		ObservedTarget? widthRefusal = null;
		CheatEngineFailure? chainRefusal = null;
		HostCall call = default;
		if (!_dispatcher.TryInvoke(() => call = HostCall.Run(_codecContextPort, request, request.BaseAddress,
				(port, current, baseAddress, out hostFailure) =>
				{
					hostFailure = null;
					// Observe once, before the first hop: a configured/process width mismatch refuses the whole chain.
					ObservedTarget facts =
						TargetArchitectureObserver.Observe(port);
					if (facts.ConfiguredPointerSizeDiffersFromBitness)
					{
						widthRefusal = facts;
						return false;
					}

					bool isThirtyTwoBit = facts.Bitness.Bytes == sizeof(uint);
					Address resolved = baseAddress;
					if (isThirtyTwoBit && resolved.Value > uint.MaxValue)
					{
						chainRefusal = CreateChainWidthFailure(0);
						return false;
					}

					for (int hop = 0; hop < current.Offsets.Length; hop++)
					{
						if (!port.TryReadPrimitive(resolved, out Address pointer, out hostFailure))
						{
							return false;
						}

						resolved = pointer + current.Offsets[hop];
						if (isThirtyTwoBit && resolved.Value > uint.MaxValue)
						{
							chainRefusal = CreateChainWidthFailure(hop + 1);
							return false;
						}
					}

					captured = resolved;
					return true;
				}), out failure, cancellationToken))
		{
			address = default;
			return false;
		}

		if (widthRefusal is { } refusal)
		{
			address = default;
			failure = RefuseWidth("Memory.ResolvePointerChain", refusal);
			return false;
		}

		if (chainRefusal is { } chainFailure)
		{
			address = default;
			failure = chainFailure;
			return false;
		}

		address = call.Succeeded ? captured : default;
		return TryMapMemoryFailure(call, false, "Memory.ResolvePointerChain", out failure);
	}

	public Address ResolvePointerChain(PointerChainRequest request, CancellationToken cancellationToken = default)
	{
		if (TryResolvePointerChain(request, out Address address, out CheatEngineFailure failure, cancellationToken))
		{
			return address;
		}

		failure.Throw(cancellationToken);
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

	/// <summary>Runs a consumer codec read inside the dispatched callback.</summary>
	/// <remarks>
	///     Exceptions thrown by the consumer codec are rethrown unchanged by the dispatcher. SDK faults raised by the
	///     context's Client-internal port calls are captured by the context and reported as classified failures.
	/// </remarks>
	private CodecOutcome TryReadCore<T>(MemoryReadRequest<T> request, out T? value)
	{
		TargetMemoryCodecContext context = TargetMemoryCodecContext.Create(_lifetime, _dispatcher, _codecContextPort,
			_limits);
		try
		{
			if (request.Codec.TryRead(context, request.Address, out value))
			{
				return CodecOutcome.Success;
			}

			return context.CreateFailureOutcome(
				$"The codec for '{typeof(T).Name}' rejected the target-memory read.");
		}
		catch (CheatEngineOperationException exception) when (context.IsContextFault(exception))
		{
			// Only the exact exception instance this context threw is converted; any other codec exception, including
			// an application-owned CheatEngineOperationException, is rethrown unchanged by the dispatcher.
			value = default;
			return context.CreateFailureOutcome(exception.Message);
		}
		finally
		{
			context.Expire();
		}
	}

	private CodecOutcome TryWriteCore<T>(MemoryWriteRequest<T> request)
	{
		TargetMemoryCodecContext context = TargetMemoryCodecContext.Create(_lifetime, _dispatcher, _codecContextPort,
			_limits);
		try
		{
			T value = request.Value;
			if (request.Codec.TryWrite(context, request.Address, in value))
			{
				return CodecOutcome.Success;
			}

			return context.CreateFailureOutcome(
				$"The codec for '{typeof(T).Name}' rejected the target-memory write.");
		}
		catch (CheatEngineOperationException exception) when (context.IsContextFault(exception))
		{
			return context.CreateFailureOutcome(exception.Message);
		}
		finally
		{
			context.Expire();
		}
	}

	private CheatEngineFailure CreateCodecFailure(CodecOutcome outcome, bool isWrite, string operation)
	{
		if (outcome.Fault is { } fault)
		{
			return SdkBoundary.Translate(operation, fault, CheatEngineHostEffect.Unknown, _lifetime);
		}

		string message = outcome.Message ?? (isWrite
			? "Cheat Engine rejected the target-memory write."
			: "Cheat Engine rejected the target-memory read.");
		if (outcome.Kind is { } kind)
		{
			// A refusal recorded by the codec context itself (pointer-width policy, no target): nothing was accessed.
			if (outcome.WidthRefusal is { } facts)
			{
				ReportWidthRefusal(operation, facts);
			}

			return new CheatEngineFailure(kind, operation, message, null, CheatEngineHostEffect.NotStarted);
		}

		return new CheatEngineFailure(
			isWrite ? CheatEngineFailureKind.MemoryWriteFailed : CheatEngineFailureKind.MemoryReadFailed,
			operation,
			message);
	}

	/// <summary>
	///     Creates the width-mismatch refusal of an operation and reports it with the two widths only (EventId 1200).
	/// </summary>
	private CheatEngineFailure RefuseWidth(string operation, ObservedTarget facts)
	{
		ReportWidthRefusal(operation, facts);
		return PointerWidthPolicy.CreateMismatchFailure(operation, facts);
	}

	private void ReportWidthRefusal(string operation, ObservedTarget facts)
	{
		_lifetime.Diagnostics.PointerWidthMismatchRefused(operation, facts.Bitness.Bytes,
			facts.ConfiguredPointerSizeBytes ?? 0);
	}

	private static CheatEngineFailure CreateChainWidthFailure(int completedHops)
	{
		return completedHops == 0
			? new CheatEngineFailure(CheatEngineFailureKind.OperationRejected, "Memory.ResolvePointerChain",
				"The pointer chain base address exceeds the 32-bit process width of the target; no hop was read.", null,
				CheatEngineHostEffect.NotStarted)
			: new CheatEngineFailure(CheatEngineFailureKind.OperationRejected, "Memory.ResolvePointerChain",
				$"The address computed after pointer hop {completedHops} exceeds the 32-bit process width of the " +
				"target; the chain stopped before the next read.", null, CheatEngineHostEffect.Started);
	}

	private static CheatEngineFailure UnsupportedPrimitive<T>(string operation)
	{
		return new CheatEngineFailure(CheatEngineFailureKind.Unsupported, operation,
			$"'{typeof(T).FullName}' is not a built-in CheatEngine.Client memory type.", null,
			CheatEngineHostEffect.NotStarted);
	}

	private static MemoryPrimitiveBatchWriteOutcome CreateDispatchFailureOutcome(int attemptedCount,
		CheatEngineFailure dispatchFailure)
	{
		// ICheatEngineDispatcher observes cancellation before admission only, so a Cancelled dispatch failure proves that
		// no callback ran and no write was attempted. Every other dispatch failure leaves the effect unknown.
		if (dispatchFailure.Kind == CheatEngineFailureKind.Cancelled)
		{
			return new MemoryPrimitiveBatchWriteOutcome(attemptedCount, 0, null,
				CoreFailureFactory.WithHostEffect(dispatchFailure, CheatEngineHostEffect.NotStarted),
				MemoryBatchWriteEffectState.NotStarted);
		}

		return new MemoryPrimitiveBatchWriteOutcome(attemptedCount, 0, null, dispatchFailure,
			MemoryBatchWriteEffectState.Unknown);
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
			$"The requested {resource} of {requestedBytes} {unit} exceeds the activation {direction} budget of {limit} {unit}.",
			null, CheatEngineHostEffect.NotStarted);
	}

	private static CheatEngineFailure CreateBatchFailure<T>(bool handled, bool isWrite, int failedIndex,
		string? hostFailure)
	{
		string operation = isWrite ? "Memory.WritePrimitiveBatch" : "Memory.ReadPrimitiveBatch";
		if (!handled)
		{
			return UnsupportedPrimitive<T>(operation);
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

	private bool TryMapMemoryFailure(HostCall call, bool isWrite, string operation, out CheatEngineFailure failure)
	{
		if (call.Succeeded)
		{
			failure = default;
			return true;
		}

		if (call.Fault is { } fault)
		{
			failure = SdkBoundary.Translate(operation, fault, CheatEngineHostEffect.Unknown, _lifetime);
			return false;
		}

		failure = new CheatEngineFailure(
			isWrite ? CheatEngineFailureKind.MemoryWriteFailed : CheatEngineFailureKind.MemoryReadFailed,
			operation,
			call.HostFailure ?? "Cheat Engine rejected the target-memory operation.");
		return false;
	}

	private readonly record struct PrimitiveReadInput(Address Address, IMemoryCodecContextPort Port);

	private readonly record struct PrimitiveWriteInput<T>(Address Address, T Value, IMemoryCodecContextPort Port);

	private readonly record struct PrimitiveReadOutcome<T>(
		bool Handled,
		bool Succeeded,
		T Value,
		string? Failure,
		Exception? Fault = null,
		ObservedTarget? WidthRefusal = null);

	private readonly record struct PrimitiveWriteOutcome(
		bool Handled,
		bool Succeeded,
		string? Failure,
		Exception? Fault = null,
		ObservedTarget? WidthRefusal = null);

	/// <summary>The pointer-width admission of an Address primitive path, observed once before any memory access.</summary>
	private readonly record struct PointerWidthAdmission(ObservedTarget? Refusal, Exception? Fault)
	{
		internal bool IsAdmitted => Refusal is null && Fault is null;

		internal static PointerWidthAdmission Observe(IMemoryCodecContextPort port)
		{
			try
			{
				ObservedTarget facts =
					TargetArchitectureObserver.Observe(port);
				return new PointerWidthAdmission(facts.ConfiguredPointerSizeDiffersFromBitness ? facts : null,
					null);
			}
			catch (Exception exception) when (SdkBoundary.IsSdkFault(exception))
			{
				return new PointerWidthAdmission(null, exception);
			}
		}
	}

	/// <summary>The result of one Client-internal host call, with any SDK fault captured instead of thrown.</summary>
	private readonly record struct HostCall(bool Succeeded, string? HostFailure, Exception? Fault)
	{
		internal static HostCall Run<TState>(IMemoryCodecContextPort port, TState state, Address address,
			HostOperation<TState> operation)
		{
			try
			{
				return operation(port, state, address, out string? hostFailure)
					? new HostCall(true, null, null)
					: new HostCall(false, hostFailure, null);
			}
			catch (Exception exception) when (SdkBoundary.IsSdkFault(exception))
			{
				return new HostCall(false, null, exception);
			}
		}
	}

	private delegate bool HostOperation<in TState>(IMemoryCodecContextPort port, TState state, Address address,
		out string? hostFailure);

	/// <summary>
	///     The result of a consumer codec call, distinguishing a codec refusal, an SDK fault and a refusal recorded by the
	///     codec context itself (<paramref name="Kind" />).
	/// </summary>
	private readonly record struct CodecOutcome(
		bool Succeeded,
		string? Message,
		Exception? Fault,
		CheatEngineFailureKind? Kind = null,
		ObservedTarget? WidthRefusal = null)
	{
		internal static CodecOutcome Success => new(true, null, null);
	}

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
		string? Failure,
		Exception? Fault = null,
		ObservedTarget? WidthRefusal = null);

	private readonly record struct PrimitiveBatchWriteOutcome(
		bool Handled,
		bool Succeeded,
		int FailedIndex,
		string? Failure,
		Exception? Fault = null,
		ObservedTarget? WidthRefusal = null);

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

		private static bool IsPointer => typeof(T) == typeof(Address);

		/// <summary>
		///     Reads one supported primitive; an SDK fault is captured, never thrown across a Try method. An Address read
		///     is first admitted by <see cref="PointerWidthPolicy" />.
		/// </summary>
		internal static PrimitiveReadOutcome<T> Read(IMemoryCodecContextPort port, Address address)
		{
			if (!IsSupported)
			{
				return new PrimitiveReadOutcome<T>(false, false, default!, null);
			}

			if (IsPointer)
			{
				PointerWidthAdmission admission = PointerWidthAdmission.Observe(port);
				if (!admission.IsAdmitted)
				{
					return new PrimitiveReadOutcome<T>(true, false, default!, null, admission.Fault, admission.Refusal);
				}
			}

			return ReadElement(port, address);
		}

		private static PrimitiveReadOutcome<T> ReadElement(IMemoryCodecContextPort port, Address address)
		{
			try
			{
				return port.TryReadPrimitive(address, out T value, out string? failure)
					? new PrimitiveReadOutcome<T>(true, true, value, null)
					: new PrimitiveReadOutcome<T>(true, false, default!, failure);
			}
			catch (Exception exception) when (SdkBoundary.IsSdkFault(exception))
			{
				return new PrimitiveReadOutcome<T>(true, false, default!, null, exception);
			}
		}

		/// <summary>
		///     Writes one supported primitive; an SDK fault is captured, never thrown across a Try method. An Address
		///     write is first admitted by <see cref="PointerWidthPolicy" />.
		/// </summary>
		internal static PrimitiveWriteOutcome Write(IMemoryCodecContextPort port, Address address, T value)
		{
			if (!IsSupported)
			{
				return new PrimitiveWriteOutcome(false, false, null);
			}

			if (IsPointer)
			{
				PointerWidthAdmission admission = PointerWidthAdmission.Observe(port);
				if (!admission.IsAdmitted)
				{
					return new PrimitiveWriteOutcome(true, false, null, admission.Fault, admission.Refusal);
				}
			}

			return WriteElement(port, address, value);
		}

		private static PrimitiveWriteOutcome WriteElement(IMemoryCodecContextPort port, Address address, T value)
		{
			try
			{
				return port.TryWritePrimitive(address, value, out string? failure)
					? new PrimitiveWriteOutcome(true, true, null)
					: new PrimitiveWriteOutcome(true, false, failure);
			}
			catch (Exception exception) when (SdkBoundary.IsSdkFault(exception))
			{
				return new PrimitiveWriteOutcome(true, false, null, exception);
			}
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

			if (IsPointer)
			{
				PointerWidthAdmission admission = PointerWidthAdmission.Observe(port);
				if (!admission.IsAdmitted)
				{
					return new PrimitiveBatchReadOutcome<T>(true, false, [], 0, null, admission.Fault, admission.Refusal);
				}
			}

			T[] values = new T[request.Addresses.Length];
			for (int index = 0; index < request.Addresses.Length; index++)
			{
				PrimitiveReadOutcome<T> current = ReadElement(port, request.Addresses[index]);
				if (!current.Succeeded)
				{
					return new PrimitiveBatchReadOutcome<T>(current.Handled, false, values[..index], index,
						current.Failure, current.Fault);
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

			if (IsPointer)
			{
				PointerWidthAdmission admission = PointerWidthAdmission.Observe(port);
				if (!admission.IsAdmitted)
				{
					return new PrimitiveBatchWriteOutcome(true, false, 0, null, admission.Fault, admission.Refusal);
				}
			}

			for (int index = 0; index < request.Values.Length; index++)
			{
				MemoryAddressValue<T> current = request.Values[index];
				PrimitiveWriteOutcome outcome = WriteElement(port, current.Address, current.Value);
				if (!outcome.Succeeded)
				{
					return new PrimitiveBatchWriteOutcome(outcome.Handled, false, index, outcome.Failure, outcome.Fault);
				}
			}

			return new PrimitiveBatchWriteOutcome(true, true, -1, null);
		}
	}

	private sealed class TargetMemoryCodecContext
		: IMemoryReadContext, IMemoryWriteContext, IMemoryPointerWidthContext, ICorePointerCodecPolicy
	{
		private const string Operation = "Memory.CodecContext";

		private readonly long _activationEpoch;
		private readonly ICheatEngineDispatcher _dispatcher;
		private readonly CoreLifetime _lifetime;
		private readonly MemoryResourceLimits _limits;
		private readonly IMemoryCodecContextPort _port;
		private readonly int _threadId;
		private CheatEngineOperationException? _contextFault;
		private int _expired;
		private ObservedTarget? _facts;
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

		/// <summary>Gets the SDK fault captured by the most recent failed context operation, if any.</summary>
		internal Exception? Fault
		{
			get;
			private set;
		}

		/// <summary>Gets the failure kind of a refusal recorded by this context itself, if any.</summary>
		internal CheatEngineFailureKind? FailureKind
		{
			get;
			private set;
		}

		/// <summary>Gets the facts of a pointer-width mismatch refusal recorded by this context, if any.</summary>
		internal ObservedTarget? WidthRefusal
		{
			get;
			private set;
		}

		/// <summary>
		///     Gets the process width of the selected target (the width Cheat Engine's readPointer uses), never the
		///     plugin's own process width and never Cheat Engine's configured pointer size.
		/// </summary>
		public int PointerSize
		{
			get
			{
				ThrowIfUnusable();
				ObservedTarget facts = ObserveFacts();
				return facts.Bitness.IsKnown
					? facts.Bitness.Bytes
					: ThrowProcessWidthUnavailable(facts);
			}
		}

		public PointerSize ProcessPointerSize
		{
			get
			{
				ThrowIfUnusable();
				return ObserveFacts().Bitness;
			}
		}

		public int? ConfiguredPointerSizeBytes
		{
			get
			{
				ThrowIfUnusable();
				return ObserveFacts().ConfiguredPointerSizeBytes;
			}
		}

		public PointerSize ConfiguredPointerSize
		{
			get
			{
				ThrowIfUnusable();
				return ObserveFacts().ConfiguredPointerSize;
			}
		}

		public bool ConfiguredPointerSizeDiffersFromProcessWidth
		{
			get
			{
				ThrowIfUnusable();
				return ObserveFacts().ConfiguredPointerSizeDiffersFromBitness;
			}
		}

		/// <inheritdoc />
		public bool TryAdmitPointerCodec()
		{
			ThrowIfUnusable();
			ObservedTarget facts = ObserveFacts();
			if (!facts.Bitness.IsKnown)
			{
				RecordRefusal(GetUnavailableWidthKind(facts), GetUnavailableWidthMessage(facts));
				return false;
			}

			if (facts.ConfiguredPointerSizeDiffersFromBitness)
			{
				RecordRefusal(CheatEngineFailureKind.OperationRejected, PointerWidthPolicy.CreateMismatchMessage(facts));
				WidthRefusal = facts;
				return false;
			}

			return true;
		}

		public bool TryReadBytes(Address address, Span<byte> destination)
		{
			ThrowIfUnusable();
			if (!TryAdmitCodecBytes(destination.Length, _limits.MaximumReadBytes, ref _readBytesAdmitted, "read"))
			{
				return false;
			}

			try
			{
				if (_port.TryReadBytes(address, destination, out string? failure))
				{
					ClearFailure();
					return true;
				}

				SetFailure(failure, null);
				return false;
			}
			catch (Exception exception) when (SdkBoundary.IsSdkFault(exception))
			{
				SetFailure(null, exception);
				return false;
			}
		}

		public bool TryWriteBytes(Address address, ReadOnlySpan<byte> source)
		{
			ThrowIfUnusable();
			if (!TryAdmitCodecBytes(source.Length, _limits.MaximumWriteBytes, ref _writeBytesAdmitted, "write"))
			{
				return false;
			}

			try
			{
				if (_port.TryWriteBytes(address, source, out string? failure))
				{
					ClearFailure();
					return true;
				}

				SetFailure(failure, null);
				return false;
			}
			catch (Exception exception) when (SdkBoundary.IsSdkFault(exception))
			{
				SetFailure(null, exception);
				return false;
			}
		}

		/// <summary>Gets whether <paramref name="exception" /> is the exact exception instance this context threw.</summary>
		internal bool IsContextFault(Exception exception)
		{
			return _contextFault is not null && ReferenceEquals(exception, _contextFault);
		}

		/// <summary>Creates the failure outcome of a codec that returned <see langword="false" /> or threw a context fault.</summary>
		internal CodecOutcome CreateFailureOutcome(string defaultMessage)
		{
			return new CodecOutcome(false, Failure ?? defaultMessage, Fault, FailureKind, WidthRefusal);
		}

		private static CheatEngineFailureKind GetUnavailableWidthKind(ObservedTarget facts)
		{
			// ADR-08: only an observed "no process selected" is TargetNotAttached. Every other status the SDK reports
			// (a target change, a file opened as a process, an absent, raising or malformed global) keeps its own kind.
			return facts.HasTarget
				? CheatEngineFailureKind.IndeterminateHostResult
				: RuntimeObservationMapping.ToFailureKind(facts.Status.Kind);
		}

		private static string GetUnavailableWidthMessage(ObservedTarget facts)
		{
			return facts.Status.Kind switch
			{
				ProcessOperationStatusKind.Success =>
					"Cheat Engine did not report the process width of the selected target.",
				ProcessOperationStatusKind.TargetNotAttached =>
					"No target process is selected, so the codec context has no process width.",
				ProcessOperationStatusKind.TargetChanged =>
					"The selected target changed while the codec context observed its process width.",
				ProcessOperationStatusKind.FileAsProcessTarget =>
					"The selected target is a file opened as a process, so the process width is unobservable.",
				_ => $"Cheat Engine reported {facts.Status.Kind} for the selected target, so the process width is " +
					 "unobservable."
			};
		}

		/// <summary>
		///     Observes the target facts once per codec invocation, PID first. The observer classifies SDK Engine and Lua
		///     exceptions itself; any other SDK fault becomes a context fault that Core reports after the codec returns.
		/// </summary>
		private ObservedTarget ObserveFacts()
		{
			if (_facts is { } facts)
			{
				return facts;
			}

			try
			{
				facts = TargetArchitectureObserver.Observe(_port);
			}
			catch (Exception exception) when (SdkBoundary.IsSdkFault(exception))
			{
				Fault = exception;
				Failure = "Cheat Engine could not report the target pointer width.";
				throw CreateContextFault(CoreFailureFactory.GetKind(exception), Failure, exception);
			}

			_facts = facts;
			return facts;
		}

		/// <summary>
		///     A property cannot return a failure: throw a Client exception that Core recognizes by identity and converts
		///     back into a classified failure after the consumer codec returns or rethrows it.
		/// </summary>
		private int ThrowProcessWidthUnavailable(ObservedTarget facts)
		{
			CheatEngineFailureKind kind = GetUnavailableWidthKind(facts);
			string message = GetUnavailableWidthMessage(facts);
			RecordRefusal(kind, message);
			throw CreateContextFault(kind, message, null);
		}

		private CheatEngineOperationException CreateContextFault(CheatEngineFailureKind kind, string message,
			Exception? exception)
		{
			_contextFault = new CheatEngineOperationException(new CheatEngineFailure(kind, Operation, message,
				exception, CheatEngineHostEffect.NotStarted));
			return _contextFault;
		}

		private void RecordRefusal(CheatEngineFailureKind kind, string message)
		{
			Failure = message;
			Fault = null;
			FailureKind = kind;
			WidthRefusal = null;
		}

		private void SetFailure(string? failure, Exception? fault)
		{
			Failure = failure ?? (fault is null ? null : "Cheat Engine raised an SDK fault during the codec operation.");
			Fault = fault;
			FailureKind = null;
			WidthRefusal = null;
		}

		private void ClearFailure()
		{
			Failure = null;
			Fault = null;
			FailureKind = null;
			WidthRefusal = null;
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
					Operation,
					"The memory codec context is no longer valid for the current Cheat Engine invocation.");
			}

			_lifetime.ThrowIfInactive(Operation);
		}

		private bool TryAdmitCodecBytes(int requestedBytes, int limit, ref int admittedBytes, string direction)
		{
			if (requestedBytes > limit - admittedBytes)
			{
				SetFailure($"The memory codec {direction} exceeds the activation {direction} budget of {limit} bytes.",
					null);
				return false;
			}

			admittedBytes += requestedBytes;
			return true;
		}
	}
}
