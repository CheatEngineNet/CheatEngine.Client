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
using CheatEngine.SDK.Engine.Runtime;
using CheatEngine.SDK.Engine.Values;

namespace CheatEngine.Client.Core.Domains;

internal sealed class MemoryClient : IMemoryClient
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
		where T : unmanaged
	{
		MemoryPrimitiveBatchReadOutcome<T> outcome = ReadPrimitiveBatchCore(request, cancellationToken);
		// Counts only, never an address or a value (A24-17); a read batch has no target effect.
		_lifetime.Diagnostics.MemoryBatchCompleted("Memory.ReadPrimitiveBatch", outcome.AttemptedCount,
			outcome.CompletedCount, ReadBatchEffectState);
		return outcome;
	}

	public MemoryPrimitiveBatchWriteOutcome WritePrimitiveBatchDetailed<T>(MemoryPrimitiveBatchWriteRequest<T> request,
		CancellationToken cancellationToken = default)
		where T : unmanaged
	{
		MemoryPrimitiveBatchWriteOutcome outcome = WritePrimitiveBatchCore(request, cancellationToken);
		_lifetime.Diagnostics.MemoryBatchCompleted("Memory.WritePrimitiveBatch", outcome.AttemptedCount,
			outcome.CompletedCount, outcome.EffectState.ToString());
		return outcome;
	}

	private MemoryPrimitiveBatchReadOutcome<T> ReadPrimitiveBatchCore<T>(MemoryPrimitiveBatchReadRequest<T> request,
		CancellationToken cancellationToken)
		where T : unmanaged
	{
		ValidateBatch(request.Addresses, nameof(request));
		int attemptedCount = request.Addresses.Length;
		if (!PrimitiveMemoryCodec<T>.IsSupported)
		{
			return new MemoryPrimitiveBatchReadOutcome<T>(attemptedCount, 0, null,
				UnsupportedPrimitive<T>("Memory.ReadPrimitiveBatch"), []);
		}

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

		if (outcome.Fault is { } admissionFault && outcome.FailedIndex < 0)
		{
			// The target observation of an Address batch faulted before its first read.
			return new MemoryPrimitiveBatchReadOutcome<T>(attemptedCount, 0, null,
				SdkBoundary.Translate("Memory.ReadPrimitiveBatch", admissionFault, CheatEngineHostEffect.Unknown,
					_lifetime), []);
		}

		CheatEngineFailure failure = outcome.Fault is { } fault
			? SdkBoundary.Translate("Memory.ReadPrimitiveBatch", fault, CheatEngineHostEffect.Unknown, _lifetime)
			: CreateBatchFailure(false, outcome.FailedIndex, outcome.Failure);
		return new MemoryPrimitiveBatchReadOutcome<T>(attemptedCount, outcome.FailedIndex, outcome.FailedIndex,
			failure, outcome.Values);
	}

	private MemoryPrimitiveBatchWriteOutcome WritePrimitiveBatchCore<T>(MemoryPrimitiveBatchWriteRequest<T> request,
		CancellationToken cancellationToken)
		where T : unmanaged
	{
		ValidateBatch(request.Values, nameof(request));
		int attemptedCount = request.Values.Length;
		if (!PrimitiveMemoryCodec<T>.IsSupported)
		{
			return new MemoryPrimitiveBatchWriteOutcome(attemptedCount, 0, null,
				UnsupportedPrimitive<T>("Memory.WritePrimitiveBatch"), MemoryBatchWriteEffectState.NotStarted);
		}

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

		if (outcome.Fault is { } admissionFault && outcome.FailedIndex < 0)
		{
			// The target observation of an Address batch faulted before its first write: nothing was written.
			return new MemoryPrimitiveBatchWriteOutcome(attemptedCount, 0, null,
				SdkBoundary.Translate("Memory.WritePrimitiveBatch", admissionFault, CheatEngineHostEffect.NotStarted,
					_lifetime), MemoryBatchWriteEffectState.NotStarted);
		}

		if (outcome.Fault is { } fault)
		{
			// The write at FailedIndex threw inside the SDK: whether it reached the target cannot be established.
			CheatEngineFailure faultFailure = SdkBoundary.Translate("Memory.WritePrimitiveBatch", fault,
				CheatEngineHostEffect.Unknown, _lifetime);
			return new MemoryPrimitiveBatchWriteOutcome(attemptedCount, outcome.FailedIndex, outcome.FailedIndex,
				faultFailure, MemoryBatchWriteEffectState.Unknown);
		}

		CheatEngineFailure failure = CreateBatchFailure(true, outcome.FailedIndex, outcome.Failure);
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
		where T : unmanaged
	{
		if (!PrimitiveMemoryCodec<T>.IsSupported)
		{
			value = default;
			failure = UnsupportedPrimitive<T>("Memory.ReadPrimitive");
			return false;
		}

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
					: MemoryAccessFailureMapping.ToFailure("Memory.ReadPrimitive", outcome.Failure, false);
			return false;
		}

		value = outcome.Value;
		failure = default;
		return true;
	}

	public T ReadPrimitive<T>(Address address, CancellationToken cancellationToken = default)
		where T : unmanaged
	{
		if (TryReadPrimitive(address, out T value, out CheatEngineFailure failure, cancellationToken))
		{
			return value;
		}

		failure.Throw(cancellationToken);
		return default;
	}

	public bool TryWritePrimitive<T>(Address address, T value, out CheatEngineFailure failure,
		CancellationToken cancellationToken = default)
		where T : unmanaged
	{
		if (!PrimitiveMemoryCodec<T>.IsSupported)
		{
			failure = UnsupportedPrimitive<T>("Memory.WritePrimitive");
			return false;
		}

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
					: MemoryAccessFailureMapping.ToFailure("Memory.WritePrimitive", outcome.Failure, true);
			return false;
		}

		failure = default;
		return true;
	}

	public void WritePrimitive<T>(Address address, T value, CancellationToken cancellationToken = default)
		where T : unmanaged
	{
		if (!TryWritePrimitive(address, value, out CheatEngineFailure failure, cancellationToken))
		{
			failure.Throw(cancellationToken);
		}
	}

	public bool TryReadPrimitiveBatch<T>(MemoryPrimitiveBatchReadRequest<T> request, out ImmutableArray<T> values,
		out CheatEngineFailure failure, CancellationToken cancellationToken = default)
		where T : unmanaged
	{
		MemoryPrimitiveBatchReadOutcome<T> outcome = ReadPrimitiveBatchDetailed(request, cancellationToken);
		if (outcome.Failure is { } readFailure)
		{
			values = [];
			failure = readFailure;
			return false;
		}

		values = outcome.ReadPrefix;
		failure = default;
		return true;
	}

	public ImmutableArray<T> ReadPrimitiveBatch<T>(MemoryPrimitiveBatchReadRequest<T> request,
		CancellationToken cancellationToken = default)
		where T : unmanaged
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
		where T : unmanaged
	{
		MemoryPrimitiveBatchWriteOutcome outcome = WritePrimitiveBatchDetailed(request, cancellationToken);
		if (outcome.Failure is { } writeFailure)
		{
			failure = writeFailure;
			return false;
		}

		failure = default;
		return true;
	}

	public void WritePrimitiveBatch<T>(MemoryPrimitiveBatchWriteRequest<T> request,
		CancellationToken cancellationToken = default)
		where T : unmanaged
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
		MemoryBytesReadOutcome outcome = ReadBytesDetailed(request, cancellationToken);
		if (outcome.Failure is { } readFailure)
		{
			// The Try form publishes all or nothing; ReadBytesDetailed keeps the confirmed prefix.
			bytes = [];
			failure = readFailure;
			return false;
		}

		bytes = outcome.Bytes;
		failure = default;
		return true;
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

	/// <summary>
	///     Reads through CheatEngine.SDK's counted <c>TargetMemory.TryReadBytes</c>: every byte it verified before a
	///     shorter or malformed result is kept as the confirmed prefix, and a <c>PartialRead</c> names its length.
	/// </summary>
	public MemoryBytesReadOutcome ReadBytesDetailed(MemoryBytesReadRequest request,
		CancellationToken cancellationToken = default)
	{
		ArgumentOutOfRangeException.ThrowIfNegativeOrZero(request.Length);
		if (!TryAdmitPayload(request.Length, _limits.MaximumReadBytes, false, "Memory.ReadBytes", "byte read",
				out CheatEngineFailure failure))
		{
			return new MemoryBytesReadOutcome(request.Length, [], failure);
		}

		byte[] buffer = [];
		int written = 0;
		HostCall call = default;
		if (!_dispatcher.TryInvoke(() =>
			{
				buffer = new byte[request.Length];
				call = HostCall.Run(_codecContextPort, buffer, request.Address,
					(port, destination, address, out hostFailure) =>
						port.TryReadBytes(address, destination, out written, out hostFailure));
			}, out failure, cancellationToken))
		{
			return new MemoryBytesReadOutcome(request.Length, [], failure);
		}

		if (call.Succeeded)
		{
			return new MemoryBytesReadOutcome(request.Length, ImmutableCollectionsMarshal.AsImmutableArray(buffer),
				null);
		}

		if (call.Fault is { } fault)
		{
			return new MemoryBytesReadOutcome(request.Length, [],
				SdkBoundary.Translate("Memory.ReadBytes", fault, CheatEngineHostEffect.Unknown, _lifetime));
		}

		// A count outside [0, Length) cannot be a verified prefix of a failed read: publish none of it.
		int confirmed = written > 0 && written < request.Length ? written : 0;
		return new MemoryBytesReadOutcome(request.Length, ImmutableArray.Create(buffer, 0, confirmed),
			MemoryAccessFailureMapping.ToByteReadFailure("Memory.ReadBytes", call.HostFailure, confirmed,
				request.Length));
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
		if (!TryAdmitPayload(GetEncodedByteLength(request.MaximumLength, request.Encoding == MemoryStringEncoding.Utf16),
				_limits.MaximumStringBytes, false, "Memory.ReadString", "string read", out failure))
		{
			value = null;
			return false;
		}

		string? captured = null;
		HostCall call = default;
		if (!_dispatcher.TryInvoke(() => call = HostCall.Run(_codecContextPort, request, request.Address,
				(port, current, address, out hostFailure) => port.TryReadString(address,
					current.MaximumLength, current.Encoding == MemoryStringEncoding.Utf16, out captured, out hostFailure)),
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
		bool wideCharacter = request.Encoding == MemoryStringEncoding.Utf16;
		int encodedLength = GetEncodedLength(request.Value, wideCharacter);
		if (encodedLength > request.MaximumLength)
		{
			throw new ArgumentException("The encoded text exceeds the explicit maximum length.", nameof(request));
		}

		if (!TryAdmitPayload(GetEncodedByteLength(request.Value, wideCharacter), _limits.MaximumStringBytes,
				true, "Memory.WriteString", "string write", out failure))
		{
			return false;
		}

		HostCall call = default;
		if (!_dispatcher.TryInvoke(() => call = HostCall.Run(_codecContextPort, request, request.Address,
				static (port, current, address, out hostFailure) => port.TryWriteString(address,
					current.Value.AsSpan(), current.Encoding == MemoryStringEncoding.Utf16, out hostFailure)),
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
		int failedHop = 0;
		HostCall call = default;
		if (!_dispatcher.TryInvoke(() => call = HostCall.Run(_codecContextPort, request, request.BaseAddress,
				(port, current, baseAddress, out hostFailure) =>
				{
					hostFailure = MemoryAccessFailure.None;
					// Observe once, before the first hop: an unknown bitness or a configured/process width mismatch refuses
					// the whole chain; every hop then reads through the SDK overload qualified by the observed bitness.
					ObservedTarget facts = TargetArchitectureObserver.Observe(port);
					if (!PointerWidthPolicy.IsAdmitted(facts))
					{
						widthRefusal = facts;
						return false;
					}

					PointerSize width = facts.Bitness;
					Address resolved = baseAddress;
					if (!PointerWidthPolicy.Fits(resolved, width))
					{
						chainRefusal = CreateChainAddressFailure(0, current.Offsets.Length);
						return false;
					}

					for (int hop = 0; hop < current.Offsets.Length; hop++)
					{
						if (!port.TryReadPointer(resolved, width, out Address pointer, out hostFailure))
						{
							failedHop = hop + 1;
							return false;
						}

						// The SDK qualified the pointer value; the address the Client computes from it by adding the
						// offset must still fit a 32-bit target, which would wrap it instead.
						resolved = pointer + current.Offsets[hop];
						if (!PointerWidthPolicy.Fits(resolved, width))
						{
							chainRefusal = CreateChainAddressFailure(hop + 1, current.Offsets.Length);
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

		address = default;
		if (widthRefusal is { } refusal)
		{
			failure = RefuseWidth("Memory.ResolvePointerChain", refusal);
			return false;
		}

		if (chainRefusal is { } chainFailure)
		{
			failure = chainFailure;
			return false;
		}

		if (call.Succeeded)
		{
			address = captured;
			failure = default;
			return true;
		}

		if (call.Fault is { } fault)
		{
			failure = SdkBoundary.Translate("Memory.ResolvePointerChain", fault, CheatEngineHostEffect.Unknown,
				_lifetime);
			return false;
		}

		failure = CreateChainHopFailure(failedHop, request.Offsets.Length, call.HostFailure);
		return false;
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

		if (outcome.AccessFailure is { } access)
		{
			// The codec returned false after a context access that CheatEngine.SDK refused: keep the mapped kind. A codec
			// may have made earlier accesses, so the host effect stays unknown.
			return new CheatEngineFailure(MemoryAccessFailureMapping.ToFailureKind(access), operation, message);
		}

		return new CheatEngineFailure(
			isWrite ? CheatEngineFailureKind.MemoryWriteFailed : CheatEngineFailureKind.MemoryReadFailed,
			operation,
			message);
	}

	/// <summary>
	///     Creates the pointer-width refusal of an operation: an unknown bitness, or a configured/bitness mismatch that is
	///     reported with the two widths only (EventId 1200).
	/// </summary>
	private CheatEngineFailure RefuseWidth(string operation, ObservedTarget facts)
	{
		if (facts.Bitness.IsKnown)
		{
			ReportWidthRefusal(operation, facts);
		}

		return PointerWidthPolicy.CreateRefusal(operation, facts);
	}

	private void ReportWidthRefusal(string operation, ObservedTarget facts)
	{
		_lifetime.Diagnostics.PointerWidthMismatchRefused(operation, facts.Bitness.Bytes,
			facts.ConfiguredPointerSizeBytes ?? 0);
	}

	/// <summary>
	///     Creates the refusal of a chain address that does not fit a 32-bit target: the base address before any hop
	///     (<c>NotStarted</c>), or the address computed after hop <paramref name="completedHops" />, whose reads all
	///     returned (<c>Completed</c>; reads leave no effect in the target).
	/// </summary>
	private static CheatEngineFailure CreateChainAddressFailure(int completedHops, int hopCount)
	{
		return completedHops == 0
			? new CheatEngineFailure(CheatEngineFailureKind.OperationRejected, "Memory.ResolvePointerChain",
				$"The pointer chain base address exceeds the 32-bit process width of the target; hop 1 of {hopCount} " +
				"was not read.", null, CheatEngineHostEffect.NotStarted)
			: new CheatEngineFailure(CheatEngineFailureKind.OperationRejected, "Memory.ResolvePointerChain",
				$"The address computed after pointer hop {completedHops} of {hopCount} exceeds the 32-bit process " +
				"width of the target; the chain stopped before the next read.", null, CheatEngineHostEffect.Completed);
	}

	/// <summary>
	///     Creates the failure of the pointer read of hop <paramref name="hop" /> (1-based) with its mapped kind; the hop
	///     index is part of the message, never an address or a value.
	/// </summary>
	private static CheatEngineFailure CreateChainHopFailure(int hop, int hopCount, MemoryAccessFailure access)
	{
		CheatEngineFailure mapped = MemoryAccessFailureMapping.ToFailure("Memory.ResolvePointerChain", access, false);
		return new CheatEngineFailure(mapped.Kind, mapped.Operation,
			$"The pointer read of hop {hop} of {hopCount} failed: {mapped.Message}", null, mapped.HostEffect);
	}

	/// <summary>
	///     Creates the refusal of a primitive type outside the supported set (A5): <c>OperationRejected</c> and
	///     <c>NotStarted</c>, before dispatch and without a Cheat Engine call.
	/// </summary>
	private static CheatEngineFailure UnsupportedPrimitive<T>(string operation)
		where T : unmanaged
	{
		return new CheatEngineFailure(CheatEngineFailureKind.OperationRejected, operation,
			$"'{typeof(T).FullName}' is not a primitive the Client supports: use an 8- to 64-bit integer, float, double " +
			"or Address, or pass a codec through a MemoryReadRequest or MemoryWriteRequest.", null,
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
		where T : unmanaged
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

	private static CheatEngineFailure CreateBatchFailure(bool isWrite, int failedIndex, MemoryAccessFailure hostFailure)
	{
		string operation = isWrite ? "Memory.WritePrimitiveBatch" : "Memory.ReadPrimitiveBatch";
		CheatEngineFailure mapped = MemoryAccessFailureMapping.ToFailure(operation, hostFailure, isWrite);
		string action = isWrite ? "write" : "read";
		return new CheatEngineFailure(mapped.Kind, operation,
			$"The batch {action} failed at index {failedIndex}: {mapped.Message}", null, mapped.HostEffect);
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

		failure = MemoryAccessFailureMapping.ToFailure(operation, call.HostFailure, isWrite);
		return false;
	}

	private readonly record struct PrimitiveReadInput(Address Address, IMemoryCodecContextPort Port);

	private readonly record struct PrimitiveWriteInput<T>(Address Address, T Value, IMemoryCodecContextPort Port)
		where T : unmanaged;

	private readonly record struct PrimitiveReadOutcome<T>(
		bool Succeeded,
		T Value,
		MemoryAccessFailure Failure,
		Exception? Fault = null,
		ObservedTarget? WidthRefusal = null)
		where T : unmanaged;

	private readonly record struct PrimitiveWriteOutcome(
		bool Succeeded,
		MemoryAccessFailure Failure,
		Exception? Fault = null,
		ObservedTarget? WidthRefusal = null);

	/// <summary>
	///     The pointer-width admission of an Address primitive path, observed once before any memory access: an unknown
	///     bitness and a configured/bitness mismatch are refused, and an admitted path uses <see cref="Width" />.
	/// </summary>
	private readonly record struct PointerWidthAdmission(PointerSize Width, ObservedTarget? Refusal, Exception? Fault)
	{
		internal bool IsAdmitted => Refusal is null && Fault is null;

		internal static PointerWidthAdmission Observe(IMemoryCodecContextPort port)
		{
			try
			{
				ObservedTarget facts = TargetArchitectureObserver.Observe(port);
				return PointerWidthPolicy.IsAdmitted(facts)
					? new PointerWidthAdmission(facts.Bitness, null, null)
					: new PointerWidthAdmission(PointerSize.Unknown, facts, null);
			}
			catch (Exception exception) when (SdkBoundary.IsSdkFault(exception))
			{
				return new PointerWidthAdmission(PointerSize.Unknown, null, exception);
			}
		}
	}

	/// <summary>The result of one Client-internal host call, with any SDK fault captured instead of thrown.</summary>
	private readonly record struct HostCall(bool Succeeded, MemoryAccessFailure HostFailure, Exception? Fault)
	{
		internal static HostCall Run<TState>(IMemoryCodecContextPort port, TState state, Address address,
			HostOperation<TState> operation)
		{
			try
			{
				return operation(port, state, address, out MemoryAccessFailure hostFailure)
					? new HostCall(true, MemoryAccessFailure.None, null)
					: new HostCall(false, hostFailure, null);
			}
			catch (Exception exception) when (SdkBoundary.IsSdkFault(exception))
			{
				return new HostCall(false, MemoryAccessFailure.None, exception);
			}
		}
	}

	private delegate bool HostOperation<in TState>(IMemoryCodecContextPort port, TState state, Address address,
		out MemoryAccessFailure hostFailure);

	/// <summary>
	///     The result of a consumer codec call, distinguishing a codec refusal, an SDK fault, a refusal recorded by the
	///     codec context itself (<paramref name="Kind" />) and a context access that CheatEngine.SDK refused
	///     (<paramref name="AccessFailure" />).
	/// </summary>
	private readonly record struct CodecOutcome(
		bool Succeeded,
		string? Message,
		Exception? Fault,
		CheatEngineFailureKind? Kind = null,
		ObservedTarget? WidthRefusal = null,
		MemoryAccessFailure? AccessFailure = null)
	{
		internal static CodecOutcome Success => new(true, null, null);
	}

	private readonly record struct PrimitiveBatchReadInput<T>(
		MemoryPrimitiveBatchReadRequest<T> Request,
		IMemoryCodecContextPort Port)
		where T : unmanaged;

	private readonly record struct PrimitiveBatchWriteInput<T>(
		MemoryPrimitiveBatchWriteRequest<T> Request,
		IMemoryCodecContextPort Port)
		where T : unmanaged;

	/// <summary>
	///     The outcome of a primitive batch inside the dispatched call; <paramref name="FailedIndex" /> is negative when no
	///     element failed (success, or a refusal or fault before the first element).
	/// </summary>
	private readonly record struct PrimitiveBatchReadOutcome<T>(
		bool Succeeded,
		T[] Values,
		int FailedIndex,
		MemoryAccessFailure Failure,
		Exception? Fault = null,
		ObservedTarget? WidthRefusal = null)
		where T : unmanaged;

	/// <summary>
	///     The outcome of a primitive batch inside the dispatched call; <paramref name="FailedIndex" /> is negative when no
	///     element failed (success, or a refusal or fault before the first element).
	/// </summary>
	private readonly record struct PrimitiveBatchWriteOutcome(
		bool Succeeded,
		int FailedIndex,
		MemoryAccessFailure Failure,
		Exception? Fault = null,
		ObservedTarget? WidthRefusal = null);

	/// <summary>The supported primitive set (A5) and its SDK route, admitted before dispatch.</summary>
	private static class PrimitiveMemoryCodec<T>
		where T : unmanaged
	{
		/// <summary>
		///     Gets whether <typeparamref name="T" /> is one of the supported primitives: the 8- to 64-bit integers,
		///     <see cref="float" />, <see cref="double" /> and <see cref="Address" />.
		/// </summary>
		internal static bool IsSupported =>
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
		///     is first admitted by <see cref="PointerWidthPolicy" /> and then qualified by the observed bitness.
		/// </summary>
		internal static PrimitiveReadOutcome<T> Read(IMemoryCodecContextPort port, Address address)
		{
			PointerSize width = PointerSize.Unknown;
			if (IsPointer)
			{
				PointerWidthAdmission admission = PointerWidthAdmission.Observe(port);
				if (!admission.IsAdmitted)
				{
					return new PrimitiveReadOutcome<T>(false, default!, MemoryAccessFailure.None,
						admission.Fault, admission.Refusal);
				}

				width = admission.Width;
			}

			return ReadElement(port, address, width);
		}

		private static PrimitiveReadOutcome<T> ReadElement(IMemoryCodecContextPort port, Address address,
			PointerSize width)
		{
			try
			{
				bool succeeded;
				T value;
				MemoryAccessFailure failure;
				if (IsPointer)
				{
					succeeded = port.TryReadPointer(address, width, out Address pointer, out failure);
					value = Unsafe.As<Address, T>(ref pointer);
				}
				else
				{
					succeeded = port.TryReadPrimitive(address, out value, out failure);
				}

				return succeeded
					? new PrimitiveReadOutcome<T>(true, value, MemoryAccessFailure.None)
					: new PrimitiveReadOutcome<T>(false, default!, failure);
			}
			catch (Exception exception) when (SdkBoundary.IsSdkFault(exception))
			{
				return new PrimitiveReadOutcome<T>(false, default!, MemoryAccessFailure.None, exception);
			}
		}

		/// <summary>
		///     Writes one supported primitive; an SDK fault is captured, never thrown across a Try method. An Address
		///     write is first admitted by <see cref="PointerWidthPolicy" /> and then qualified by the observed bitness.
		/// </summary>
		internal static PrimitiveWriteOutcome Write(IMemoryCodecContextPort port, Address address, T value)
		{
			PointerSize width = PointerSize.Unknown;
			if (IsPointer)
			{
				PointerWidthAdmission admission = PointerWidthAdmission.Observe(port);
				if (!admission.IsAdmitted)
				{
					return new PrimitiveWriteOutcome(false, MemoryAccessFailure.None, admission.Fault,
						admission.Refusal);
				}

				width = admission.Width;
			}

			return WriteElement(port, address, value, width);
		}

		private static PrimitiveWriteOutcome WriteElement(IMemoryCodecContextPort port, Address address, T value,
			PointerSize width)
		{
			try
			{
				MemoryAccessFailure failure;
				bool succeeded = IsPointer
					? port.TryWritePointer(address, Unsafe.As<T, Address>(ref value), width, out failure)
					: port.TryWritePrimitive(address, value, out failure);
				return succeeded
					? new PrimitiveWriteOutcome(true, MemoryAccessFailure.None)
					: new PrimitiveWriteOutcome(false, failure);
			}
			catch (Exception exception) when (SdkBoundary.IsSdkFault(exception))
			{
				return new PrimitiveWriteOutcome(false, MemoryAccessFailure.None, exception);
			}
		}

		internal static long GetPayloadBytes(int operationCount)
		{
			return (long) Unsafe.SizeOf<T>() * operationCount;
		}

		internal static PrimitiveBatchReadOutcome<T> ReadBatch(MemoryPrimitiveBatchReadRequest<T> request,
			IMemoryCodecContextPort port)
		{
			PointerSize width = PointerSize.Unknown;
			if (IsPointer)
			{
				PointerWidthAdmission admission = PointerWidthAdmission.Observe(port);
				if (!admission.IsAdmitted)
				{
					return new PrimitiveBatchReadOutcome<T>(false, [], -1, MemoryAccessFailure.None,
						admission.Fault, admission.Refusal);
				}

				width = admission.Width;
			}

			T[] values = new T[request.Addresses.Length];
			for (int index = 0; index < request.Addresses.Length; index++)
			{
				PrimitiveReadOutcome<T> current = ReadElement(port, request.Addresses[index], width);
				if (!current.Succeeded)
				{
					return new PrimitiveBatchReadOutcome<T>(false, values[..index], index,
						current.Failure, current.Fault);
				}

				values[index] = current.Value;
			}

			return new PrimitiveBatchReadOutcome<T>(true, values, -1, MemoryAccessFailure.None);
		}

		internal static PrimitiveBatchWriteOutcome WriteBatch(MemoryPrimitiveBatchWriteRequest<T> request,
			IMemoryCodecContextPort port)
		{
			PointerSize width = PointerSize.Unknown;
			if (IsPointer)
			{
				PointerWidthAdmission admission = PointerWidthAdmission.Observe(port);
				if (!admission.IsAdmitted)
				{
					return new PrimitiveBatchWriteOutcome(false, -1, MemoryAccessFailure.None, admission.Fault,
						admission.Refusal);
				}

				width = admission.Width;
			}

			for (int index = 0; index < request.Values.Length; index++)
			{
				MemoryAddressValue<T> current = request.Values[index];
				PrimitiveWriteOutcome outcome = WriteElement(port, current.Address, current.Value, width);
				if (!outcome.Succeeded)
				{
					return new PrimitiveBatchWriteOutcome(false, index, outcome.Failure, outcome.Fault);
				}
			}

			return new PrimitiveBatchWriteOutcome(true, -1, MemoryAccessFailure.None);
		}
	}

	private sealed class TargetMemoryCodecContext
		: IMemoryReadContext, IMemoryWriteContext, ICorePointerCodecPolicy
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

		/// <summary>Gets the SDK failure of the most recent context access that CheatEngine.SDK refused, if any.</summary>
		internal MemoryAccessFailure? AccessFailure
		{
			get;
			private set;
		}

		/// <summary>
		///     Gets the bitness of the selected target (the width Cheat Engine's readPointer uses), never the plugin's own
		///     process width and never Cheat Engine's configured pointer size. An unknown bitness is recorded as the reason
		///     a codec that then returns <see langword="false" /> failed, without a throw.
		/// </summary>
		public PointerSize Bitness
		{
			get
			{
				ThrowIfUnusable();
				ObservedTarget facts = ObserveFacts();
				if (!facts.Bitness.IsKnown)
				{
					RecordRefusal(PointerWidthPolicy.GetUnknownWidthKind(facts),
						PointerWidthPolicy.CreateUnknownWidthMessage(facts));
				}

				return facts.Bitness;
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

		public bool ConfiguredPointerSizeDiffersFromBitness
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
				RecordRefusal(PointerWidthPolicy.GetUnknownWidthKind(facts),
					PointerWidthPolicy.CreateUnknownWidthMessage(facts));
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
				if (_port.TryReadBytes(address, destination, out int written, out MemoryAccessFailure failure))
				{
					ClearFailure();
					return true;
				}

				// The codec asked for the exact buffer: a confirmed prefix is reported in the failure, never left behind
				// as if it were data.
				destination.Clear();
				SetAccessFailure(failure, false);
				if (failure == MemoryAccessFailure.PartialRead)
				{
					Failure = MemoryAccessFailureMapping.ToByteReadFailure(Operation, failure,
						Math.Clamp(written, 0, destination.Length), destination.Length).Message;
				}

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
				if (_port.TryWriteBytes(address, source, out MemoryAccessFailure failure))
				{
					ClearFailure();
					return true;
				}

				SetAccessFailure(failure, true);
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
			return new CodecOutcome(false, Failure ?? defaultMessage, Fault, FailureKind, WidthRefusal, AccessFailure);
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
			AccessFailure = null;
		}

		private void SetFailure(string? failure, Exception? fault)
		{
			Failure = failure ?? (fault is null ? null : "Cheat Engine raised an SDK fault during the codec operation.");
			Fault = fault;
			FailureKind = null;
			WidthRefusal = null;
			AccessFailure = null;
		}

		/// <summary>Records a context access that CheatEngine.SDK refused with its own category, never its text.</summary>
		private void SetAccessFailure(MemoryAccessFailure failure, bool isWrite)
		{
			SetFailure(MemoryAccessFailureMapping.Describe(failure, isWrite), null);
			AccessFailure = failure;
		}

		private void ClearFailure()
		{
			Failure = null;
			Fault = null;
			FailureKind = null;
			WidthRefusal = null;
			AccessFailure = null;
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
