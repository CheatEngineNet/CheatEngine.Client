using CheatEngine.Client.Core.Domains;
using CheatEngine.Client.Core.Infrastructure;
using CheatEngine.Client.Core.Qualification;
using CheatEngine.Client.Runtime;
using CheatEngine.SDK.Engine.Runtime;

namespace CheatEngine.Client.Core.Tests.Qualification;

/// <summary>
///     The qualification gate of a capability is derived from the embedded host evidence only: Satisfied for the exact
///     tuple of a recorded run in which every required scenario passed without a waiver, and Unknown, naming the first
///     condition that does not hold, for anything else, including evidence that names another tuple than this build's.
/// </summary>
public sealed class HostQualificationGateTests
{
	private const string NoEvidence = "no evidence";
	private const string RunId = "20260930T101530Z-a1b2";
	private const string SdkIdentity = "2.0.0+325c47b573f8bd39a247f1d0101f110fa36c1696";

	private static readonly HostQualificationContext Exact = new(true, SdkIdentity, CheatEngineVersion.Ce77010621,
		PointerSize.Bit64, CheatEngineOperatingSystem.Windows, TargetBackend.LocalProcess, CheatEngineArchitecture.X64,
		"1.0.0");

	private static ClientCapabilityDescriptor TypedMemory =>
		ClientCapabilityCatalog.Entries.Single(static entry => entry.Id == ClientCapabilityId.TypedMemory);

	[Fact]
	public void TheEmbeddedEvidenceIsEmptyUntilARunIsRecorded()
	{
		Assert.Null(HostQualificationEvidence.Recorded);
	}

	[Fact]
	public void OnlyTheExactTupleOfARecordedRunIsSatisfied()
	{
		ClientCapabilityEvidenceGate gate = HostQualificationGate.Evaluate(TypedMemory, Evidence(), Exact, NoEvidence);

		Assert.Equal(ClientCapabilityEvidenceState.Satisfied, gate.State);
		Assert.Contains(RunId, gate.Reason, StringComparison.Ordinal);
		Assert.Contains(SdkIdentity, gate.Reason, StringComparison.Ordinal);
		Assert.Contains(ConsumedSdkIdentity.SupportedHostProfileId, gate.Reason, StringComparison.Ordinal);
	}

	[Fact]
	public void WithoutEvidenceTheGateIsUnknownWithTheGivenReason()
	{
		ClientCapabilityEvidenceGate gate = HostQualificationGate.Evaluate(TypedMemory, null, Exact, NoEvidence);

		Assert.Equal((ClientCapabilityEvidenceState.Unknown, NoEvidence), (gate.State, gate.Reason));
	}

	[Fact]
	public void UnsafeLuaExecutionIsNeverQualified()
	{
		ClientCapabilityDescriptor unsafeLua =
			ClientCapabilityCatalog.Entries.Single(static entry => entry.Id == ClientCapabilityId.UnsafeLuaExecution);

		ClientCapabilityEvidenceGate gate = HostQualificationGate.Evaluate(unsafeLua, Evidence(), Exact, NoEvidence);

		Assert.Equal(ClientCapabilityEvidenceState.Unknown, gate.State);
		Assert.Contains("never host-qualified", gate.Reason, StringComparison.Ordinal);
	}

	[Theory]
	[InlineData("identity", "(" + SdkIdentity + ") is not exactly the CheatEngine.SDK package")]
	[InlineData("no-loaded-identity", "not the loaded CheatEngine.SDK.Engine (no informational version)")]
	[InlineData("version", "is not Cheat Engine 7.7.0.10621 64-bit on Windows")]
	[InlineData("unknown-version", "is not Cheat Engine 7.7.0.10621 64-bit on Windows")]
	[InlineData("bitness", "(observed 7.7.0.10621, an unknown width, Windows)")]
	[InlineData("32-bit", "(observed 7.7.0.10621, 32-bit, Windows)")]
	[InlineData("operating-system", "is not Cheat Engine 7.7.0.10621 64-bit on Windows")]
	[InlineData("backend", "reached through FileAsProcess")]
	[InlineData("client-version", "is not the Client 1.0.0")]
	[InlineData("no-client-version", "is not the Client 1.0.0")]
	[InlineData("architecture", "on a X86 target")]
	public void EveryOtherHostOrTargetIsUnknownWithItsReason(string mismatch, string reason)
	{
		HostQualificationContext context = mismatch switch
		{
			"identity" => Exact with
			{
				ExactReviewedIdentity = false
			},
			"no-loaded-identity" => Exact with
			{
				SdkInformationalVersion = null
			},
			"version" => Exact with
			{
				CheatEngineVersion = new CheatEngineVersion(7, 6, 0, 0)
			},
			"unknown-version" => Exact with
			{
				CheatEngineVersion = null
			},
			"bitness" => Exact with
			{
				CheatEngineBitness = PointerSize.Unknown
			},
			"32-bit" => Exact with
			{
				CheatEngineBitness = PointerSize.Bit32
			},
			"operating-system" => Exact with
			{
				OperatingSystem = CheatEngineOperatingSystem.Linux
			},
			"backend" => Exact with
			{
				Backend = TargetBackend.FileAsProcess
			},
			"client-version" => Exact with
			{
				ClientVersion = "1.0.1"
			},
			"no-client-version" => Exact with
			{
				ClientVersion = null
			},
			"architecture" => Exact with
			{
				TargetArchitecture = CheatEngineArchitecture.X86
			},
			_ => throw new ArgumentOutOfRangeException(nameof(mismatch), mismatch, null)
		};

		ClientCapabilityEvidenceGate gate = HostQualificationGate.Evaluate(TypedMemory, Evidence(), context, NoEvidence);

		Assert.Equal(ClientCapabilityEvidenceState.Unknown, gate.State);
		Assert.Contains(reason, gate.Reason, StringComparison.Ordinal);
	}

	[Theory]
	[InlineData("sdk", "used CheatEngine.SDK 2.0.1+325c47b573f8bd39a247f1d0101f110fa36c1696, not the loaded " +
					   "CheatEngine.SDK.Engine (" + SdkIdentity + ")")]
	[InlineData("sdk-commit", "used CheatEngine.SDK 2.0.0+0000000000000000000000000000000000000000")]
	[InlineData("cheat-engine", "recorded Cheat Engine 7.6.0.0; a qualification covers Cheat Engine 7.7.0.10621 only")]
	[InlineData("host-profile", "recorded the host profile ce-7.7.0.10621-x86-managed-hostfxr; this Client supports " +
								"ce-7.7.0.10621-x64-managed-hostfxr only")]
	public void EvidenceOfAnotherTupleIsUnknownWithItsReason(string mismatch, string reason)
	{
		HostQualificationRecord evidence = mismatch switch
		{
			"sdk" => Evidence() with
			{
				SdkInformationalVersion = "2.0.1+325c47b573f8bd39a247f1d0101f110fa36c1696"
			},
			"sdk-commit" => Evidence() with
			{
				SdkInformationalVersion = "2.0.0+0000000000000000000000000000000000000000"
			},
			"cheat-engine" => Evidence() with
			{
				CheatEngineVersion = new CheatEngineVersion(7, 6, 0, 0)
			},
			"host-profile" => Evidence() with
			{
				HostProfile = "ce-7.7.0.10621-x86-managed-hostfxr"
			},
			_ => throw new ArgumentOutOfRangeException(nameof(mismatch), mismatch, null)
		};

		ClientCapabilityEvidenceGate gate = HostQualificationGate.Evaluate(TypedMemory, evidence, Exact, NoEvidence);

		Assert.Equal(ClientCapabilityEvidenceState.Unknown, gate.State);
		Assert.Contains(reason, gate.Reason, StringComparison.Ordinal);
	}

	[Fact]
	public void ACapabilityTheRunDidNotRecordIsUnknown()
	{
		ClientCapabilityDescriptor tables = ClientCapabilityCatalog.Entries.Single(static entry => entry.Id == ClientCapabilityId.Tables);

		ClientCapabilityEvidenceGate gate = HostQualificationGate.Evaluate(tables, Evidence(), Exact, NoEvidence);

		Assert.Equal(ClientCapabilityEvidenceState.Unknown, gate.State);
		Assert.Contains($"recorded no qualification of {ClientCapabilityId.Tables.Value}", gate.Reason, StringComparison.Ordinal);
	}

	[Fact]
	public void AMissingOrWaivedScenarioNeverQualifies()
	{
		ClientCapabilityEvidenceGate missing = HostQualificationGate.Evaluate(TypedMemory, Evidence(passed: ["Q20", "Q21"]),
			Exact, NoEvidence);
		ClientCapabilityEvidenceGate waived = HostQualificationGate.Evaluate(TypedMemory,
			Evidence(passed: ["Q20", "Q21", "Q33"], waived: ["Q33"]), Exact, NoEvidence);

		Assert.Equal(ClientCapabilityEvidenceState.Unknown, missing.State);
		Assert.Contains("Scenario Q33 of Client.TypedMemory did not pass", missing.Reason, StringComparison.Ordinal);
		Assert.Equal(ClientCapabilityEvidenceState.Unknown, waived.State);
		Assert.Contains("is waived", waived.Reason, StringComparison.Ordinal);
	}

	[Theory]
	[InlineData("1.0.0+0123456789abcdef", "1.0.0")]
	[InlineData("1.0.0-rc.1", "1.0.0-rc.1")]
	[InlineData("", null)]
	[InlineData(null, null)]
	public void TheClientVersionDropsItsBuildMetadata(string? informational, string? expected)
	{
		Assert.Equal(expected, HostQualificationGate.WithoutMetadata(informational));
	}

	private static HostQualificationRecord Evidence(string[]? passed = null, string[]? waived = null)
	{
		return new HostQualificationRecord(RunId, "1.0.0", SdkIdentity, ConsumedSdkIdentity.SupportedHostProfileId,
			CheatEngineVersion.Ce77010621,
		[
			new HostQualifiedCapability(ClientCapabilityId.TypedMemory, [.. passed ?? ["Q20", "Q21", "Q33"]], [.. waived ?? []],
				[CheatEngineArchitecture.X64])
		]);
	}
}
