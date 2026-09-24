#pragma warning disable CECLIENT5003 // These tests exercise the experimental instruction client.

using System.Collections.Immutable;

using CheatEngine.Client.Assembly;
using CheatEngine.Client.Core.Dispatching;
using CheatEngine.Client.Core.Domains.Assembly;
using CheatEngine.Client.Core.Infrastructure;
using CheatEngine.Client.Core.Tests.TestSupport;
using CheatEngine.Client.Memory;
using CheatEngine.Client.Results;
using CheatEngine.SDK.Engine.Assembly;
using CheatEngine.SDK.Engine.Inspection;
using CheatEngine.SDK.Engine.Memory;
using CheatEngine.SDK.Engine.Runtime;
using CheatEngine.SDK.Engine.Values;

namespace CheatEngine.Client.Core.Tests.Domains;

/// <summary>
///     The experimental instruction client over a fake port (plan L18): one profile observation per call, the bounded
///     assembly buffer and its one retry, the width refusal of a 32-bit profile, the options that reach CheatEngine.SDK,
///     bytes read from target memory, and a target change detected between the profile and the call (Q32).
/// </summary>
public sealed class AssemblyClientTests
{
	private static readonly Address Code = new(0x401000);

	private static readonly Address AboveFourGiB = new(0x1_0000_0000);

	private static readonly InstructionProfileObservation X64 =
		new(default, new TargetProcessId(4242), CheatEngineArchitecture.X64, PointerSize.Bit64);

	private static readonly InstructionProfileObservation X86 =
		new(default, new TargetProcessId(4343), CheatEngineArchitecture.X86, PointerSize.Bit32);

	private readonly CoreLifetime _lifetime = InertCoreLifetime.Create();

	public static TheoryData<InstructionEncodingPreference, AssemblePreference, bool> Preferences => new()
	{
		{ InstructionEncodingPreference.None, AssemblePreference.None, false },
		{ InstructionEncodingPreference.Short, AssemblePreference.Short, true },
		{ InstructionEncodingPreference.Long, AssemblePreference.Long, false },
		{ InstructionEncodingPreference.Far, AssemblePreference.Far, true }
	};

	[Fact]
	[Trait("Qualification", "Q32")]
	public void TheProfileIsObservedOnceAndTheOneRetryUsesTheExactRequiredLength()
	{
		byte[] encoded = [.. Enumerable.Range(1, 20).Select(static value => (byte) value)];
		FakePort port = new()
		{
			AssembleResults =
			{
				new AssembleResult(InstructionOperationStatus.DestinationTooSmall, [], 20),
				new AssembleResult(InstructionOperationStatus.Success, encoded, 20)
			}
		};
		AssemblyClient client = CreateClient(port);

		bool assembled = client.TryAssemble(new AssemblyInstructionRequest(Code, "db 01 02"),
			out ImmutableArray<byte> bytes, out CheatEngineFailure failure, TestContext.Current.CancellationToken);

		Assert.True(assembled, failure.Message);
		Assert.True(failure.IsDefault);
		Assert.Equal(encoded, bytes);
		Assert.Equal(1, port.Admissions);
		Assert.Equal(1, port.ProfileObservations);
		Assert.Equal([AssemblyClient.InitialAssemblyCapacity, 20], port.AssembleCalls.Select(static call => call.Capacity));
		Assert.All(port.AssembleCalls, static call => Assert.Equal(X64, call.Profile));
	}

	[Fact]
	public void AResultThatFitsTheFirstBufferIsCopiedWithoutARetry()
	{
		FakePort port = new()
		{
			AssembleResults = { new AssembleResult(InstructionOperationStatus.Success, [0x90], 1) }
		};

		ImmutableArray<byte> bytes = CreateClient(port).Assemble(new AssemblyInstructionRequest(Code, "nop"),
			TestContext.Current.CancellationToken);

		Assert.Equal([0x90], bytes);
		Assert.Single(port.AssembleCalls);
	}

	[Fact]
	public void ASecondTooSmallDestinationIsAResultLimitWithoutAThirdCall()
	{
		FakePort port = new()
		{
			AssembleResults =
			{
				new AssembleResult(InstructionOperationStatus.DestinationTooSmall, [], 20),
				new AssembleResult(InstructionOperationStatus.DestinationTooSmall, [], 24)
			}
		};

		bool assembled = CreateClient(port).TryAssemble(new AssemblyInstructionRequest(Code, "db 01"),
			out ImmutableArray<byte> bytes, out CheatEngineFailure failure, TestContext.Current.CancellationToken);

		Assert.False(assembled);
		Assert.True(bytes.IsDefault);
		Assert.Equal(CheatEngineFailureKind.ResultLimitExceeded, failure.Kind);
		Assert.Equal(CheatEngineHostEffect.Completed, failure.HostEffect);
		Assert.Equal(2, port.AssembleCalls.Count);
	}

	[Fact]
	public void ARequiredLengthAboveTheLimitIsRefusedWithoutARetry()
	{
		FakePort port = new()
		{
			AssembleResults = { new AssembleResult(InstructionOperationStatus.DestinationTooSmall, [], 64) }
		};
		AssemblyClient client = CreateClient(port, new MemoryResourceLimits { MaximumReadBytes = 32 });

		bool assembled = client.TryAssemble(new AssemblyInstructionRequest(Code, "db 01"), out _,
			out CheatEngineFailure failure, TestContext.Current.CancellationToken);

		Assert.False(assembled);
		Assert.Equal(CheatEngineFailureKind.ResultLimitExceeded, failure.Kind);
		Assert.Equal(CheatEngineHostEffect.Completed, failure.HostEffect);
		Assert.Single(port.AssembleCalls);
	}

	[Fact]
	public void TheFirstBufferNeverExceedsTheConfiguredLimit()
	{
		FakePort port = new()
		{
			AssembleResults = { new AssembleResult(InstructionOperationStatus.Success, [0x90], 1) }
		};
		AssemblyClient client = CreateClient(port, new MemoryResourceLimits { MaximumReadBytes = 4 });

		Assert.True(client.TryAssemble(new AssemblyInstructionRequest(Code, "nop"), out _, out _,
			TestContext.Current.CancellationToken));
		Assert.Equal(4, Assert.Single(port.AssembleCalls).Capacity);
	}

	[Theory]
	[MemberData(nameof(Preferences))]
	[Trait("Qualification", "Q32")]
	public void ThePreferenceTheRangeCheckOptionAndTheOriginReachCheatEngineSdk(
		InstructionEncodingPreference preference, AssemblePreference expected, bool skipRangeCheck)
	{
		FakePort port = new()
		{
			AssembleResults = { new AssembleResult(InstructionOperationStatus.Success, [0xEB, 0x00], 2) }
		};

		Assert.True(CreateClient(port).TryAssemble(
			new AssemblyInstructionRequest(Code, "jmp 00401002", preference, skipRangeCheck), out _, out _,
			TestContext.Current.CancellationToken));

		AssembleCall call = Assert.Single(port.AssembleCalls);
		Assert.Equal(expected, call.Preference);
		Assert.Equal(skipRangeCheck, call.SkipRangeCheck);
		Assert.Equal(Code, call.Address);
		Assert.Equal("jmp 00401002", call.Instruction);
	}

	[Fact]
	[Trait("Qualification", "Q32")]
	public void AnX86ProfileRefusesAnAddressAboveFourGiBBeforeCheatEngineIsCalled()
	{
		FakePort port = new()
		{
			Profile = X86
		};
		AssemblyClient client = CreateClient(port);
		CancellationToken token = TestContext.Current.CancellationToken;

		CheatEngineFailure[] failures =
		[
			Failure(() => (client.TryAssemble(new AssemblyInstructionRequest(AboveFourGiB, "nop"), out _,
				out CheatEngineFailure f, token), f)),
			Failure(() => (client.TryDisassemble(AboveFourGiB, out _, out CheatEngineFailure f, token), f)),
			Failure(() => (client.TryGetInstructionLength(AboveFourGiB, out _, out CheatEngineFailure f, token), f)),
			Failure(() => (client.TryGetPreviousInstruction(AboveFourGiB, out _, out CheatEngineFailure f, token), f))
		];

		Assert.All(failures, static failure =>
		{
			Assert.Equal(CheatEngineFailureKind.OperationRejected, failure.Kind);
			Assert.Equal(CheatEngineHostEffect.NotStarted, failure.HostEffect);
		});
		Assert.Equal(
			[
				AssemblyClient.AssembleOperation, AssemblyClient.DisassembleOperation,
				AssemblyClient.GetInstructionLengthOperation, AssemblyClient.GetPreviousInstructionOperation
			],
			failures.Select(static failure => failure.Operation));
		Assert.Equal(4, port.ProfileObservations);
		Assert.Equal(["Admit", "ObserveProfile", "Admit", "ObserveProfile", "Admit", "ObserveProfile", "Admit", "ObserveProfile"],
			port.Calls);
	}

	[Fact]
	[Trait("Qualification", "Q32")]
	public void AnX86ProfileAcceptsAnAddressWithinFourGiB()
	{
		FakePort port = new()
		{
			Profile = X86,
			Length = (InstructionOperationStatus.Success, 2)
		};

		Assert.Equal(2, CreateClient(port).GetInstructionLength(new Address(uint.MaxValue),
			TestContext.Current.CancellationToken));
	}

	[Fact]
	[Trait("Qualification", "Q32")]
	public void DisassembledBytesAreReadFromTargetMemoryForTheReportedLength()
	{
		FakePort port = new()
		{
			Length = (InstructionOperationStatus.Success, 3),
			Memory = [0x8B, 0x45, 0x08],
			Disassembly = (InstructionOperationStatus.Success,
				new InstructionDisassembly(Code, "00401000", "90 90 90", "mov eax,[ebp+08]", "", 26))
		};

		AssemblyInstructionSnapshot instruction = CreateClient(port).Disassemble(Code,
			TestContext.Current.CancellationToken);

		Assert.Equal(Code, instruction.Address);
		Assert.Equal(3, instruction.Length);
		Assert.Equal(instruction.Length, instruction.Bytes.Length);
		Assert.Equal([0x8B, 0x45, 0x08], instruction.Bytes);
		Assert.Equal("00401000", instruction.AddressText);
		Assert.Equal("mov eax,[ebp+08]", instruction.Opcode);
		Assert.Equal("mov eax,[ebp+08]", instruction.Text);
		Assert.Equal(["Admit", "ObserveProfile", "GetLength", "ReadBytes", "Disassemble"], port.Calls);
		Assert.Equal(MemoryResourceLimits.DefaultMaximumStringBytes, port.DisassemblyBound);
		Assert.Equal(3, port.ReadLength);
	}

	[Theory]
	[Trait("Qualification", "Q32")]
	[InlineData("GetLength")]
	[InlineData("Disassemble")]
	public void ATargetChangeBetweenTheProfileAndTheCallIsDetected(string changedAt)
	{
		FakePort port = new()
		{
			Length = changedAt == "GetLength"
				? (InstructionOperationStatus.TargetChanged, 0)
				: (InstructionOperationStatus.Success, 1),
			Memory = [0x90],
			Disassembly = (InstructionOperationStatus.TargetChanged, default)
		};

		bool disassembled = CreateClient(port).TryDisassemble(Code, out AssemblyInstructionSnapshot instruction,
			out CheatEngineFailure failure, TestContext.Current.CancellationToken);

		Assert.False(disassembled);
		Assert.Equal(default, instruction);
		Assert.Equal(CheatEngineFailureKind.TargetChanged, failure.Kind);
		Assert.Equal(AssemblyClient.DisassembleOperation, failure.Operation);
		Assert.Equal(1, port.ProfileObservations);
		Assert.Equal(changedAt == "Disassemble", port.Calls.Contains("ReadBytes"));
	}

	[Fact]
	[Trait("Qualification", "Q32")]
	public void AnAssemblyOnAChangedTargetPublishesNoBytes()
	{
		FakePort port = new()
		{
			AssembleResults = { new AssembleResult(InstructionOperationStatus.TargetChanged, [], 0) }
		};

		bool assembled = CreateClient(port).TryAssemble(new AssemblyInstructionRequest(Code, "nop"),
			out ImmutableArray<byte> bytes, out CheatEngineFailure failure, TestContext.Current.CancellationToken);

		Assert.False(assembled);
		Assert.True(bytes.IsDefault);
		Assert.Equal(CheatEngineFailureKind.TargetChanged, failure.Kind);
	}

	[Theory]
	[InlineData(InstructionOperationStatus.TargetNotSelected, CheatEngineFailureKind.TargetNotAttached)]
	[InlineData(InstructionOperationStatus.UnsupportedTargetBackend, CheatEngineFailureKind.Unsupported)]
	[InlineData(InstructionOperationStatus.InvalidProfile, CheatEngineFailureKind.InvalidHostResult)]
	[InlineData(InstructionOperationStatus.LuaFailure, CheatEngineFailureKind.LuaError)]
	public void AFailedProfileObservationStopsTheCallBeforeAnyInstructionGlobal(InstructionOperationStatus status,
		CheatEngineFailureKind expected)
	{
		FakePort port = new()
		{
			ProfileStatus = status
		};

		bool measured = CreateClient(port).TryGetInstructionLength(Code, out int length,
			out CheatEngineFailure failure, TestContext.Current.CancellationToken);

		Assert.False(measured);
		Assert.Equal(0, length);
		Assert.Equal(expected, failure.Kind);
		Assert.Equal(CheatEngineHostEffect.NotStarted, failure.HostEffect);
		Assert.Equal(["Admit", "ObserveProfile"], port.Calls);
	}

	[Fact]
	public void ARejectedInstructionAppliedNothing()
	{
		FakePort port = new()
		{
			AssembleResults =
			{
				new AssembleResult(InstructionOperationStatus.InstructionRejected, [], 0),
				new AssembleResult(InstructionOperationStatus.InstructionRejected, [], 0)
			}
		};
		AssemblyClient client = CreateClient(port);

		bool assembled = client.TryAssemble(new AssemblyInstructionRequest(Code, "notAnOpcode"), out _,
			out CheatEngineFailure failure, TestContext.Current.CancellationToken);

		Assert.False(assembled);
		Assert.Equal(CheatEngineFailureKind.OperationRejected, failure.Kind);
		Assert.Equal(CheatEngineHostEffect.NotApplied, failure.HostEffect);
		Assert.Equal(failure,
			Assert.Throws<CheatEngineOperationException>(() =>
				client.Assemble(new AssemblyInstructionRequest(Code, "notAnOpcode"),
					TestContext.Current.CancellationToken)).Failure);
	}

	[Fact]
	public void APartialByteReadPublishesNoInstruction()
	{
		FakePort port = new()
		{
			Length = (InstructionOperationStatus.Success, 4),
			Memory = [0x0F, 0x1F],
			ReadFailure = MemoryAccessFailure.PartialRead
		};

		bool disassembled = CreateClient(port).TryDisassemble(Code, out AssemblyInstructionSnapshot instruction,
			out CheatEngineFailure failure, TestContext.Current.CancellationToken);

		Assert.False(disassembled);
		Assert.Equal(default, instruction);
		Assert.Equal(CheatEngineFailureKind.MemoryReadFailed, failure.Kind);
		Assert.Contains("2 of the 4", failure.Message, StringComparison.Ordinal);
		Assert.DoesNotContain("Disassemble", port.Calls);
	}

	[Theory]
	[InlineData("ReadBytes")]
	[InlineData("Disassemble")]
	public void AStepRefusedAfterTheLengthQueryIsCompletedNotNotStarted(string refusedAt)
	{
		FakePort port = new()
		{
			Length = (InstructionOperationStatus.Success, 1),
			Memory = [0x90],
			ReadFailure = refusedAt == "ReadBytes" ? MemoryAccessFailure.GlobalUnavailable : MemoryAccessFailure.None,
			Disassembly = (InstructionOperationStatus.GlobalUnavailable, default)
		};

		bool disassembled = CreateClient(port).TryDisassemble(Code, out AssemblyInstructionSnapshot instruction,
			out CheatEngineFailure failure, TestContext.Current.CancellationToken);

		Assert.False(disassembled);
		Assert.Equal(default, instruction);
		Assert.Equal(CheatEngineFailureKind.CapabilityUnavailable, failure.Kind);
		Assert.Equal(CheatEngineHostEffect.Completed, failure.HostEffect);
		Assert.Equal("GetLength", port.Calls[2]);
		Assert.Equal(refusedAt, port.Calls[^1]);
	}

	[Fact]
	public void AnUnavailableAssemblerIsNotStartedOnlyBeforeTheFirstCall()
	{
		FakePort first = new()
		{
			AssembleResults = { new AssembleResult(InstructionOperationStatus.GlobalUnavailable, [], 0) }
		};
		FakePort retry = new()
		{
			AssembleResults =
			{
				new AssembleResult(InstructionOperationStatus.DestinationTooSmall, [], 20),
				new AssembleResult(InstructionOperationStatus.GlobalUnavailable, [], 0)
			}
		};
		CancellationToken token = TestContext.Current.CancellationToken;

		Assert.False(CreateClient(first).TryAssemble(new AssemblyInstructionRequest(Code, "nop"), out _,
			out CheatEngineFailure beforeCall, token));
		Assert.False(CreateClient(retry).TryAssemble(new AssemblyInstructionRequest(Code, "db 01"), out _,
			out CheatEngineFailure afterCall, token));

		Assert.Equal((CheatEngineFailureKind.CapabilityUnavailable, CheatEngineHostEffect.NotStarted),
			(beforeCall.Kind, beforeCall.HostEffect));
		Assert.Equal((CheatEngineFailureKind.CapabilityUnavailable, CheatEngineHostEffect.Completed),
			(afterCall.Kind, afterCall.HostEffect));
		Assert.Equal(2, retry.AssembleCalls.Count);
	}

	[Fact]
	public void AnEmptyAssemblyIsAnInvalidHostResult()
	{
		FakePort port = new()
		{
			AssembleResults = { new AssembleResult(InstructionOperationStatus.Success, [], 0) }
		};
		AssemblyClient client = CreateClient(port);

		bool assembled = client.TryAssemble(new AssemblyInstructionRequest(Code, "nop"),
			out ImmutableArray<byte> bytes, out CheatEngineFailure failure, TestContext.Current.CancellationToken);

		Assert.False(assembled);
		Assert.True(bytes.IsDefault);
		Assert.Equal(CheatEngineFailureKind.InvalidHostResult, failure.Kind);
		Assert.Equal(CheatEngineHostEffect.Completed, failure.HostEffect);
		Assert.Contains("empty", failure.Message, StringComparison.Ordinal);
		Assert.Single(port.AssembleCalls);
	}

	[Fact]
	public void AnInstructionLongerThanTheReadLimitIsNotRead()
	{
		FakePort port = new()
		{
			Length = (InstructionOperationStatus.Success, 16)
		};
		AssemblyClient client = CreateClient(port, new MemoryResourceLimits { MaximumReadBytes = 8 });

		bool disassembled = client.TryDisassemble(Code, out _, out CheatEngineFailure failure,
			TestContext.Current.CancellationToken);

		Assert.False(disassembled);
		Assert.Equal(CheatEngineFailureKind.ResultLimitExceeded, failure.Kind);
		Assert.Equal(CheatEngineHostEffect.Completed, failure.HostEffect);
		Assert.DoesNotContain("ReadBytes", port.Calls);
	}

	[Theory]
	[InlineData("", false)]
	[InlineData(" ", false)]
	[InlineData("nop", true)]
	public void ADisassemblyWithoutAnOpcodeIsAnInvalidHostResult(string opcode, bool expected)
	{
		FakePort port = new()
		{
			Length = (InstructionOperationStatus.Success, 1),
			Memory = [0x90],
			Disassembly = (InstructionOperationStatus.Success,
				new InstructionDisassembly(Code, "00401000", "90", opcode, "", 12))
		};

		bool disassembled = CreateClient(port).TryDisassemble(Code, out _, out CheatEngineFailure failure,
			TestContext.Current.CancellationToken);

		Assert.Equal(expected, disassembled);
		Assert.Equal(expected ? default : CheatEngineFailureKind.InvalidHostResult, failure.Kind);
	}

	[Fact]
	public void TheInstructionLengthIsCheatEnginesPositiveLength()
	{
		FakePort measured = new()
		{
			Length = (InstructionOperationStatus.Success, 5)
		};
		FakePort malformed = new()
		{
			Length = (InstructionOperationStatus.Success, 0)
		};

		Assert.Equal(5, CreateClient(measured).GetInstructionLength(Code, TestContext.Current.CancellationToken));
		Assert.False(CreateClient(malformed).TryGetInstructionLength(Code, out int length,
			out CheatEngineFailure failure, TestContext.Current.CancellationToken));
		Assert.Equal(0, length);
		Assert.Equal(CheatEngineFailureKind.InvalidHostResult, failure.Kind);
	}

	[Fact]
	public void ThePreviousInstructionIsCheatEnginesEstimateWithinTheProfileWidth()
	{
		FakePort estimated = new()
		{
			Previous = (InstructionOperationStatus.Success, new Address(0x400FFE))
		};
		FakePort refused = new()
		{
			Previous = (InstructionOperationStatus.AddressExceedsProfileWidth, Address.Zero)
		};
		FakePort tooWide = new()
		{
			Profile = X86,
			Previous = (InstructionOperationStatus.Success, AboveFourGiB)
		};
		CancellationToken token = TestContext.Current.CancellationToken;

		Assert.Equal(new Address(0x400FFE), CreateClient(estimated).GetPreviousInstruction(Code, token));
		foreach (FakePort port in (FakePort[]) [refused, tooWide])
		{
			Assert.False(CreateClient(port).TryGetPreviousInstruction(Code, out Address previous,
				out CheatEngineFailure failure, token));
			Assert.Equal(default, previous);
			Assert.Equal(CheatEngineFailureKind.OperationRejected, failure.Kind);
			Assert.Equal(CheatEngineHostEffect.Completed, failure.HostEffect);
		}
	}

	[Fact]
	public void ARefusedLuaAdmissionIsReportedWithoutAProfileObservation()
	{
		CheatEngineFailure refusal = new(CheatEngineFailureKind.ActivationExpired, AssemblyClient.AssembleOperation,
			"Detached.", null, CheatEngineHostEffect.NotStarted);
		FakePort port = new()
		{
			AdmissionRefusal = refusal
		};

		bool assembled = CreateClient(port).TryAssemble(new AssemblyInstructionRequest(Code, "nop"), out _,
			out CheatEngineFailure failure, TestContext.Current.CancellationToken);

		Assert.False(assembled);
		Assert.Equal(refusal, failure);
		Assert.Equal(["Admit"], port.Calls);
	}

	[Fact]
	public void CancellationIsObservedBeforeDispatchOnly()
	{
		FakePort port = new();
		AssemblyClient client = CreateClient(port);
		CancellationToken cancelled = new(true);

		Assert.False(client.TryDisassemble(Code, out _, out CheatEngineFailure failure, cancelled));
		Assert.Equal(CheatEngineFailureKind.Cancelled, failure.Kind);
		Assert.Equal(CheatEngineHostEffect.NotStarted, failure.HostEffect);
		Assert.Equal(AssemblyClient.DisassembleOperation, failure.Operation);
		CheatEngineOperationCanceledException exception = Assert.Throws<CheatEngineOperationCanceledException>(() =>
			client.GetPreviousInstruction(Code, cancelled));
		Assert.Equal(cancelled, exception.CancellationToken);
		Assert.Empty(port.Calls);
	}

	[Fact]
	public void TheDefaultRequestAndAnEndedActivationAreRejectedBeforeAnyWork()
	{
		FakePort port = new();
		using ControlledCoreLifetimeContext context = new()
		{
			IsCurrent = false
		};
		AssemblyClient ended = new(new SdkMainThreadDispatcher(new CoreLifetime(context), new InlineMainThreadInvoker()),
			new CoreLifetime(context), new MemoryResourceLimits(), port);

		Assert.Throws<ArgumentException>(() =>
			CreateClient(port).TryAssemble(default, out _, out _, TestContext.Current.CancellationToken));
		Assert.Throws<CheatEngineActivationExpiredException>(() =>
			ended.TryGetInstructionLength(Code, out _, out _, TestContext.Current.CancellationToken));
		Assert.Empty(port.Calls);
	}

	[Fact]
	public void AnSdkFaultInsideTheCallNeverCrossesTheTryForm()
	{
		InvalidOperationException fault = new("detached runtime");
		FakePort port = new()
		{
			LengthFault = fault
		};

		bool measured = CreateClient(port).TryGetInstructionLength(Code, out _, out CheatEngineFailure failure,
			TestContext.Current.CancellationToken);

		Assert.False(measured);
		Assert.Same(fault, failure.Exception);
		Assert.Equal(AssemblyClient.GetInstructionLengthOperation, failure.Operation);
		Assert.Equal(CheatEngineHostEffect.Unknown, failure.HostEffect);
	}

	private static CheatEngineFailure Failure(Func<(bool Succeeded, CheatEngineFailure Failure)> attempt)
	{
		(bool succeeded, CheatEngineFailure failure) = attempt();
		Assert.False(succeeded);
		return failure;
	}

	private AssemblyClient CreateClient(FakePort port, MemoryResourceLimits? limits = null)
	{
		return new AssemblyClient(new SdkMainThreadDispatcher(_lifetime, new InlineMainThreadInvoker()), _lifetime,
			limits ?? new MemoryResourceLimits(), port);
	}

	private sealed record AssembleResult(InstructionOperationStatus Status, byte[] Bytes, int RequiredLength);

	private sealed record AssembleCall(
		InstructionProfileObservation Profile,
		string Instruction,
		Address Address,
		AssemblePreference Preference,
		bool SkipRangeCheck,
		int Capacity);

	/// <summary>A port that records every call and answers from its configured results.</summary>
	private sealed class FakePort : IInstructionPort
	{
		internal List<string> Calls
		{
			get;
		} = [];

		internal CheatEngineFailure? AdmissionRefusal
		{
			get;
			init;
		}

		internal InstructionOperationStatus ProfileStatus
		{
			get;
			init;
		} = InstructionOperationStatus.Success;

		internal InstructionProfileObservation Profile
		{
			get;
			init;
		} = X64;

		internal List<AssembleResult> AssembleResults
		{
			get;
		} = [];

		internal List<AssembleCall> AssembleCalls
		{
			get;
		} = [];

		internal (InstructionOperationStatus Status, int Length) Length
		{
			get;
			init;
		} = (InstructionOperationStatus.Success, 1);

		internal Exception? LengthFault
		{
			get;
			init;
		}

		internal (InstructionOperationStatus Status, InstructionDisassembly Disassembly) Disassembly
		{
			get;
			init;
		} = (InstructionOperationStatus.Success, new InstructionDisassembly(Code, "00401000", "90", "nop", "", 12));

		internal (InstructionOperationStatus Status, Address Previous) Previous
		{
			get;
			init;
		}

		internal byte[] Memory
		{
			get;
			init;
		} = [0x90];

		internal MemoryAccessFailure ReadFailure
		{
			get;
			init;
		}

		internal int Admissions => Calls.Count(static call => call == "Admit");

		internal int ProfileObservations => Calls.Count(static call => call == "ObserveProfile");

		internal int DisassemblyBound
		{
			get;
			private set;
		}

		internal int ReadLength
		{
			get;
			private set;
		}

		public bool TryRunAdmitted(string operation, Action work, out CheatEngineFailure admissionFailure)
		{
			Calls.Add("Admit");
			if (AdmissionRefusal is { } refusal)
			{
				admissionFailure = refusal;
				return false;
			}

			admissionFailure = default;
			work();
			return true;
		}

		public InstructionOperationStatus ObserveProfile(out InstructionProfileObservation profile)
		{
			Calls.Add("ObserveProfile");
			profile = ProfileStatus == InstructionOperationStatus.Success ? Profile : default;
			return ProfileStatus;
		}

		public InstructionOperationStatus Assemble(InstructionProfileObservation profile, string instruction,
			Address address, AssemblePreference preference, bool skipRangeCheck, Span<byte> destination,
			out int written, out int requiredLength)
		{
			Calls.Add("Assemble");
			AssembleCalls.Add(new AssembleCall(profile, instruction, address, preference, skipRangeCheck,
				destination.Length));
			AssembleResult result = AssembleResults[AssembleCalls.Count - 1];
			requiredLength = result.RequiredLength;
			if (result.Status != InstructionOperationStatus.Success)
			{
				written = 0;
				return result.Status;
			}

			result.Bytes.CopyTo(destination);
			written = result.Bytes.Length;
			return result.Status;
		}

		public InstructionOperationStatus Disassemble(InstructionProfileObservation profile, Address address,
			int maximumUtf8Bytes, out InstructionDisassembly disassembly)
		{
			Calls.Add("Disassemble");
			DisassemblyBound = maximumUtf8Bytes;
			disassembly = Disassembly.Status == InstructionOperationStatus.Success ? Disassembly.Disassembly : default;
			return Disassembly.Status;
		}

		public InstructionOperationStatus GetLength(InstructionProfileObservation profile, Address address,
			out int length)
		{
			Calls.Add("GetLength");
			if (LengthFault is { } fault)
			{
				throw fault;
			}

			length = Length.Length;
			return Length.Status;
		}

		public InstructionOperationStatus GetPrevious(InstructionProfileObservation profile, Address address,
			out Address previous)
		{
			Calls.Add("GetPrevious");
			previous = Previous.Previous;
			return Previous.Status;
		}

		public bool TryReadBytes(Address address, Span<byte> destination, out int written,
			out MemoryAccessFailure failure)
		{
			Calls.Add("ReadBytes");
			ReadLength = destination.Length;
			int copied = Math.Min(Memory.Length, destination.Length);
			Memory.AsSpan(0, copied).CopyTo(destination);
			written = copied;
			failure = ReadFailure;
			return ReadFailure == MemoryAccessFailure.None && copied == destination.Length;
		}
	}
}
