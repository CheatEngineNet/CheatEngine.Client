using System.Collections.Immutable;
using System.Runtime.InteropServices;

using CheatEngine.Client.Assembly;
using CheatEngine.Client.Core.Dispatching;
using CheatEngine.Client.Core.Infrastructure;
using CheatEngine.Client.Dispatching;
using CheatEngine.Client.Memory;
using CheatEngine.Client.Results;
using CheatEngine.SDK.Engine.Assembly;
using CheatEngine.SDK.Engine.Memory;
using CheatEngine.SDK.Engine.Values;

namespace CheatEngine.Client.Core.Domains.Assembly;

/// <summary>The instruction client over CheatEngine.SDK's instruction assembler, disassembler and navigator.</summary>
/// <remarks>
///     <para>
///         Every call follows the Client order: request validation, activation admission, cancellation observed before
///         dispatch, then one dispatched callback on Cheat Engine's main thread. Inside it the port asks CheatEngine.SDK
///         for one Lua admission, the instruction profile is observed once, the address is checked against the profile's
///         width, and the operation runs with that same profile. A cancellation is never observed after the dispatch:
///         no instruction operation changes the target.
///     </para>
///     <para>
///         <see cref="TryAssemble" /> assembles into a buffer of <see cref="InitialAssemblyCapacity" /> bytes, bounded by
///         <see cref="MemoryResourceLimits.MaximumReadBytes" />. When Cheat Engine's result is longer, it retries once
///         with the exact length CheatEngine.SDK reported, if that length is within the bound. An empty result is an
///         invalid host result: one instruction is never zero bytes long.
///     </para>
///     <para>
///         <see cref="TryDisassemble" /> gets the instruction length, reads that many bytes from target memory, then
///         disassembles: the byte read sits between two of CheatEngine.SDK's selected-process checks, and the bytes are
///         never parsed from the disassembler's byte column. Statuses are mapped by <see cref="InstructionMapping" />,
///         memory failures by <see cref="MemoryAccessFailureMapping" />, and SDK faults are translated by
///         <see cref="SdkBoundary" />. A step that follows an earlier instruction call (the retry, the byte read, the
///         disassembly) never reports <see cref="CheatEngineHostEffect.NotStarted" />.
///     </para>
/// </remarks>
internal sealed class AssemblyClient : IAssemblyClient
{
	/// <summary>The operation name of an assembly.</summary>
	internal const string AssembleOperation = "Assembly.Assemble";

	/// <summary>The operation name of a disassembly.</summary>
	internal const string DisassembleOperation = "Assembly.Disassemble";

	/// <summary>The operation name of an instruction-length query.</summary>
	internal const string GetInstructionLengthOperation = "Assembly.GetInstructionLength";

	/// <summary>The operation name of a previous-instruction query.</summary>
	internal const string GetPreviousInstructionAddressOperation = "Assembly.GetPreviousInstructionAddress";

	/// <summary>The size of the first assembly buffer: the longest x86 instruction takes 15 bytes.</summary>
	internal const int InitialAssemblyCapacity = 16;

	private readonly ICheatEngineDispatcher _dispatcher;
	private readonly CoreLifetime _lifetime;
	private readonly MemoryResourceLimits _limits;
	private readonly IInstructionPort _port;

	internal AssemblyClient(SdkMainThreadDispatcher dispatcher, CoreLifetime lifetime, MemoryResourceLimits limits)
		: this(dispatcher, lifetime, limits, SdkInstructionPort.Instance)
	{
	}

	/// <summary>Creates the client over an explicit port; tests supply a fake port and dispatcher.</summary>
	internal AssemblyClient(ICheatEngineDispatcher dispatcher, CoreLifetime lifetime, MemoryResourceLimits limits,
		IInstructionPort port)
	{
		_dispatcher = dispatcher ?? throw new ArgumentNullException(nameof(dispatcher));
		_lifetime = lifetime ?? throw new ArgumentNullException(nameof(lifetime));
		_limits = MemoryResourceLimitsCopy.CreateValidated(limits);
		_port = port ?? throw new ArgumentNullException(nameof(port));
	}

	public bool TryAssemble(AssemblyInstructionRequest request, out ImmutableArray<byte> bytes,
		out CheatEngineFailure failure, CancellationToken cancellationToken = default)
	{
		string instruction = request.Instruction ??
							 throw new ArgumentException("The default instruction request has no instruction.",
								 nameof(request));
		AssemblePreference preference = ToSdk(request.Preference);
		return TryRun(AssembleOperation, request.Address,
			profile => AssembleOnMainThread(profile, instruction, request.Address, preference, request.SkipRangeCheck),
			out bytes, out failure, cancellationToken);
	}

	public ImmutableArray<byte> Assemble(AssemblyInstructionRequest request,
		CancellationToken cancellationToken = default)
	{
		if (TryAssemble(request, out ImmutableArray<byte> bytes, out CheatEngineFailure failure, cancellationToken))
		{
			return bytes;
		}

		failure.Throw(cancellationToken);
		return default;
	}

	public bool TryDisassemble(Address address, out AssemblyInstructionSnapshot instruction,
		out CheatEngineFailure failure, CancellationToken cancellationToken = default)
	{
		return TryRun(DisassembleOperation, address, profile => DisassembleOnMainThread(profile, address),
			out instruction, out failure, cancellationToken);
	}

	public AssemblyInstructionSnapshot Disassemble(Address address, CancellationToken cancellationToken = default)
	{
		if (TryDisassemble(address, out AssemblyInstructionSnapshot instruction, out CheatEngineFailure failure,
				cancellationToken))
		{
			return instruction;
		}

		failure.Throw(cancellationToken);
		return default;
	}

	public bool TryGetInstructionLength(Address address, out int length, out CheatEngineFailure failure,
		CancellationToken cancellationToken = default)
	{
		return TryRun(GetInstructionLengthOperation, address, profile => GetLengthOnMainThread(profile, address),
			out length, out failure, cancellationToken);
	}

	public int GetInstructionLength(Address address, CancellationToken cancellationToken = default)
	{
		if (TryGetInstructionLength(address, out int length, out CheatEngineFailure failure, cancellationToken))
		{
			return length;
		}

		failure.Throw(cancellationToken);
		return default;
	}

	public bool TryGetPreviousInstructionAddress(Address address, out Address previousAddress,
		out CheatEngineFailure failure, CancellationToken cancellationToken = default)
	{
		return TryRun(GetPreviousInstructionAddressOperation, address,
			profile => GetPreviousOnMainThread(profile, address), out previousAddress, out failure,
			cancellationToken);
	}

	public Address GetPreviousInstructionAddress(Address address, CancellationToken cancellationToken = default)
	{
		if (TryGetPreviousInstructionAddress(address, out Address previous, out CheatEngineFailure failure,
				cancellationToken))
		{
			return previous;
		}

		failure.Throw(cancellationToken);
		return default;
	}

	/// <summary>Maps the Client encoding preference to CheatEngine.SDK's, value by value.</summary>
	private static AssemblePreference ToSdk(InstructionEncodingPreference preference)
	{
		return preference switch
		{
			InstructionEncodingPreference.None => AssemblePreference.None,
			InstructionEncodingPreference.Short => AssemblePreference.Short,
			InstructionEncodingPreference.Long => AssemblePreference.Long,
			InstructionEncodingPreference.Far => AssemblePreference.Far,
			_ => throw new ArgumentOutOfRangeException(nameof(preference), preference,
				"The encoding preference must be None, Short, Long or Far.")
		};
	}

	private static Attempt<T> Fail<T>(CheatEngineFailure failure)
	{
		return new Attempt<T>(false, default!, failure);
	}

	private static Attempt<T> Succeed<T>(T value)
	{
		return new Attempt<T>(true, value, default);
	}

	/// <summary>
	///     Admits the activation, observes cancellation, then runs one dispatched callback: Lua admission, one profile
	///     observation, the address check and <paramref name="work" />.
	/// </summary>
	private bool TryRun<T>(string operation, Address address, Func<InstructionProfileObservation, Attempt<T>> work,
		out T result, out CheatEngineFailure failure, CancellationToken cancellationToken)
	{
		result = default!;
		_lifetime.ThrowIfInactive(operation);
		if (cancellationToken.IsCancellationRequested)
		{
			failure = CancellationMapping.BeforeNativeCall(operation);
			return false;
		}

		Attempt<T> attempt = default;
		if (!SdkBoundary.TryInvoke(_dispatcher, operation,
				() => attempt = RunOnMainThread(operation, address, work), CheatEngineHostEffect.Unknown, _lifetime,
				out failure, cancellationToken))
		{
			return false;
		}

		if (!attempt.Succeeded)
		{
			failure = attempt.Failure;
			return false;
		}

		result = attempt.Value;
		failure = default;
		return true;
	}

	private Attempt<T> RunOnMainThread<T>(string operation, Address address,
		Func<InstructionProfileObservation, Attempt<T>> work)
	{
		Attempt<T> attempt = default;
		if (!_port.TryRunAdmitted(operation, () =>
			{
				InstructionOperationStatus status = _port.ObserveProfile(out InstructionProfileObservation profile);
				if (InstructionMapping.ToFailure(operation, status, InstructionCallPhase.ProfileObservation) is { } refused)
				{
					attempt = Fail<T>(refused);
				}
				else
				{
					attempt = profile.Accepts(address)
						? work(profile)
						: Fail<T>(InstructionMapping.AddressOutsideProfile(operation));
				}
			}, out CheatEngineFailure admissionFailure))
		{
			return Fail<T>(admissionFailure);
		}

		return attempt;
	}

	private Attempt<ImmutableArray<byte>> AssembleOnMainThread(InstructionProfileObservation profile,
		string instruction, Address address, AssemblePreference preference, bool skipRangeCheck)
	{
		int limit = _limits.MaximumReadBytes;
		byte[] buffer = new byte[Math.Min(InitialAssemblyCapacity, limit)];
		InstructionCallPhase phase = InstructionCallPhase.Operation;
		InstructionOperationStatus status = _port.Assemble(profile, instruction, address, preference, skipRangeCheck,
			buffer, out int written, out int requiredLength);
		if (status == InstructionOperationStatus.DestinationTooSmall && requiredLength > buffer.Length)
		{
			if (requiredLength > limit)
			{
				return Fail<ImmutableArray<byte>>(
					InstructionMapping.ResultExceedsLimit(AssembleOperation, requiredLength, limit));
			}

			// The one retry, with the exact length CheatEngine.SDK reported and the same profile.
			buffer = new byte[requiredLength];
			phase = InstructionCallPhase.AfterEarlierCall;
			status = _port.Assemble(profile, instruction, address, preference, skipRangeCheck, buffer, out written,
				out _);
		}

		if (InstructionMapping.ToFailure(AssembleOperation, status, phase) is { } failure)
		{
			return Fail<ImmutableArray<byte>>(failure);
		}

		if (written == 0)
		{
			// CheatEngine.SDK reports an empty byte table as a success; one instruction is never zero bytes long.
			return Fail<ImmutableArray<byte>>(InstructionMapping.InvalidResult(AssembleOperation,
				"CheatEngine.SDK reported an empty assembled instruction; nothing was copied."));
		}

		if (written < 0 || written > buffer.Length)
		{
			return Fail<ImmutableArray<byte>>(InstructionMapping.InvalidResult(AssembleOperation,
				"CheatEngine.SDK reported an assembled length outside the Client buffer; nothing was copied."));
		}

		return Succeed(written == buffer.Length
			? ImmutableCollectionsMarshal.AsImmutableArray(buffer)
			: ImmutableArray.Create(buffer, 0, written));
	}

	private Attempt<AssemblyInstructionSnapshot> DisassembleOnMainThread(InstructionProfileObservation profile,
		Address address)
	{
		InstructionOperationStatus status = _port.GetLength(profile, address, out int length);
		if (InstructionMapping.ToFailure(DisassembleOperation, status, InstructionCallPhase.Operation) is { } failure)
		{
			return Fail<AssemblyInstructionSnapshot>(failure);
		}

		if (length <= 0)
		{
			return Fail<AssemblyInstructionSnapshot>(InstructionMapping.InvalidResult(DisassembleOperation,
				"CheatEngine.SDK reported an instruction length that is not positive."));
		}

		if (length > _limits.MaximumReadBytes)
		{
			return Fail<AssemblyInstructionSnapshot>(
				InstructionMapping.ResultExceedsLimit(DisassembleOperation, length, _limits.MaximumReadBytes));
		}

		// The byte read and the disassembly follow the length query, which already called Cheat Engine: a step refused
		// before its own call is Completed, never NotStarted.
		byte[] bytes = new byte[length];
		if (!_port.TryReadBytes(address, bytes, out int written, out MemoryAccessFailure readFailure))
		{
			// The snapshot is all or nothing: a confirmed prefix is only named in the message.
			int confirmed = written > 0 && written < length ? written : 0;
			return Fail<AssemblyInstructionSnapshot>(InstructionMapping.AfterEarlierCall(
				MemoryAccessFailureMapping.ToByteReadFailure(DisassembleOperation, readFailure, confirmed, length)));
		}

		status = _port.Disassemble(profile, address, _limits.MaximumStringBytes,
			out InstructionDisassembly disassembly);
		CheatEngineFailure? refused =
			InstructionMapping.ToFailure(DisassembleOperation, status, InstructionCallPhase.AfterEarlierCall);
		if (refused is { } disassemblyFailure)
		{
			return Fail<AssemblyInstructionSnapshot>(disassemblyFailure);
		}

		if (disassembly.AddressText is null || string.IsNullOrWhiteSpace(disassembly.Opcode) ||
			disassembly.Extra is null)
		{
			return Fail<AssemblyInstructionSnapshot>(InstructionMapping.InvalidResult(DisassembleOperation,
				"Cheat Engine's disassembler returned no instruction text."));
		}

		return Succeed(new AssemblyInstructionSnapshot(address, length, disassembly.AddressText, disassembly.Opcode,
			disassembly.Extra, bytes));
	}

	private Attempt<int> GetLengthOnMainThread(InstructionProfileObservation profile, Address address)
	{
		InstructionOperationStatus status = _port.GetLength(profile, address, out int length);
		CheatEngineFailure? failure =
			InstructionMapping.ToFailure(GetInstructionLengthOperation, status, InstructionCallPhase.Operation);
		if (failure is { } refused)
		{
			return Fail<int>(refused);
		}

		return length > 0
			? Succeed(length)
			: Fail<int>(InstructionMapping.InvalidResult(GetInstructionLengthOperation,
				"CheatEngine.SDK reported an instruction length that is not positive."));
	}

	private Attempt<Address> GetPreviousOnMainThread(InstructionProfileObservation profile, Address address)
	{
		InstructionOperationStatus status = _port.GetPrevious(profile, address, out Address previous);
		if (status == InstructionOperationStatus.AddressExceedsProfileWidth ||
			(status == InstructionOperationStatus.Success && !profile.Accepts(previous)))
		{
			// The input was accepted before the call, so the width refusal is Cheat Engine's estimate.
			return Fail<Address>(InstructionMapping.ReturnedAddressOutsideProfile(GetPreviousInstructionAddressOperation));
		}

		CheatEngineFailure? failure =
			InstructionMapping.ToFailure(GetPreviousInstructionAddressOperation, status, InstructionCallPhase.Operation);
		return failure is { } refused ? Fail<Address>(refused) : Succeed(previous);
	}

	/// <summary>The result of one dispatched call, or the failure that replaced it.</summary>
	private readonly record struct Attempt<T>(bool Succeeded, T Value, CheatEngineFailure Failure);
}
