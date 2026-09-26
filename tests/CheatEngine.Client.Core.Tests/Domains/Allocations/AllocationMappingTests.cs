using CheatEngine.Client.Allocations;
using CheatEngine.Client.Core.Domains.Allocations;
using CheatEngine.Client.Core.Infrastructure;
using CheatEngine.Client.Core.Tests.TestSupport;
using CheatEngine.Client.Results;
using CheatEngine.SDK.Engine.Allocation;
using CheatEngine.SDK.Engine.Enums;
using CheatEngine.SDK.Engine.Objects;
using CheatEngine.SDK.Engine.Targets;
using CheatEngine.SDK.Engine.Values;

namespace CheatEngine.Client.Core.Tests.Domains.Allocations;

/// <summary>
///     Every allocation outcome of the consumed CheatEngine.SDK maps to its dedicated Client value, and a value the SDK
///     could add fails closed (plan L16, Q48).
/// </summary>
public sealed class AllocationMappingTests
{
	private const string Operation = "Allocations.Contract";

	private static readonly Address Allocated = new(0x2_0000_0000);

	private static Dictionary<TargetMemoryOperationOutcomeKind, CheatEngineFailureKind> ExpectedFailureKinds => new()
	{
		[TargetMemoryOperationOutcomeKind.Unspecified] = CheatEngineFailureKind.IndeterminateHostResult,
		[TargetMemoryOperationOutcomeKind.Succeeded] = CheatEngineFailureKind.IndeterminateHostResult,
		[TargetMemoryOperationOutcomeKind.ExpectedFailure] = CheatEngineFailureKind.OperationRejected,
		[TargetMemoryOperationOutcomeKind.GlobalUnavailable] = CheatEngineFailureKind.CapabilityUnavailable,
		[TargetMemoryOperationOutcomeKind.CapabilityUnavailable] = CheatEngineFailureKind.CapabilityUnavailable,
		[TargetMemoryOperationOutcomeKind.ProtectedLuaFailure] = CheatEngineFailureKind.LuaError,
		[TargetMemoryOperationOutcomeKind.BindingFailure] = CheatEngineFailureKind.BindingError,
		[TargetMemoryOperationOutcomeKind.MarshallingFailure] = CheatEngineFailureKind.InvalidHostResult,
		[TargetMemoryOperationOutcomeKind.TargetIdentityUnavailable] = CheatEngineFailureKind.TargetIdentityUnavailable,
		[TargetMemoryOperationOutcomeKind.TargetIdentityMismatch] = CheatEngineFailureKind.TargetChanged
	};

	private static Dictionary<TargetReleaseStatus, (CheatEngineFailureKind Kind, CheatEngineHostEffect Effect)>
		ExpectedCompensations => new()
		{
			[TargetReleaseStatus.Released] =
				(CheatEngineFailureKind.IndeterminateHostResult, CheatEngineHostEffect.Completed),
			[TargetReleaseStatus.RefusedNoTarget] =
				(CheatEngineFailureKind.TargetNotAttached, CheatEngineHostEffect.CleanupUnconfirmed),
			[TargetReleaseStatus.RefusedIdentityUnavailable] =
				(CheatEngineFailureKind.TargetIdentityUnavailable, CheatEngineHostEffect.CleanupUnconfirmed),
			[TargetReleaseStatus.RefusedTargetChanged] =
				(CheatEngineFailureKind.TargetChanged, CheatEngineHostEffect.CleanupUnconfirmed),
			[TargetReleaseStatus.RefusedProcessReused] =
				(CheatEngineFailureKind.TargetChanged, CheatEngineHostEffect.CleanupUnconfirmed),
			[TargetReleaseStatus.RefusedRuntimeChanged] =
				(CheatEngineFailureKind.RuntimeChanged, CheatEngineHostEffect.CleanupUnconfirmed),
			[TargetReleaseStatus.UnconfirmedAfterInvocation] =
				(CheatEngineFailureKind.IndeterminateHostResult, CheatEngineHostEffect.CleanupUnconfirmed),
			[TargetReleaseStatus.NotInvoked] =
				(CheatEngineFailureKind.IndeterminateHostResult, CheatEngineHostEffect.CleanupUnconfirmed),
			// No release attempted: nothing establishes that the allocation is gone.
			[TargetReleaseStatus.Unspecified] =
				(CheatEngineFailureKind.IndeterminateHostResult, CheatEngineHostEffect.CleanupUnconfirmed)
		};

	[Fact]
	[Trait("Qualification", "Q48")]
	public void EveryAllocationCategoryMapsToItsFailureKind()
	{
		MappingTotality.AssertTotal<TargetMemoryOperationOutcomeKind>(
			static kind => ExpectedFailureKinds.TryGetValue(kind, out CheatEngineFailureKind expected) &&
						   AllocationMapping.ToFailureKind(kind) == expected,
			static kind => AllocationMapping.ToFailureKind(kind) == CheatEngineFailureKind.IndeterminateHostResult);
	}

	[Fact]
	[Trait("Qualification", "Q48")]
	public void EveryCompensationStatusMapsToItsFailureAndOnlyAConfirmedReleaseClaimsNothingRemains()
	{
		MappingTotality.AssertTotal<TargetReleaseStatus>(
			static status => ExpectedCompensations.TryGetValue(status,
								 out (CheatEngineFailureKind Kind, CheatEngineHostEffect Effect) expected) &&
							 Describe(AllocationMapping.FromCompensation(status, Allocated, 16, Operation)) == expected,
			static status => Describe(AllocationMapping.FromCompensation(status, Allocated, 16, Operation)) ==
							 (CheatEngineFailureKind.IndeterminateHostResult, CheatEngineHostEffect.CleanupUnconfirmed));
		Assert.All(Enum.GetValues<TargetReleaseStatus>().Where(static status => status != TargetReleaseStatus.Released),
			static status => Assert.Contains("16 bytes at 0x200000000",
				AllocationMapping.FromCompensation(status, Allocated, 16, Operation).Message, StringComparison.Ordinal));
	}

	[Fact]
	[Trait("Qualification", "Q48")]
	public void EveryProtectionMapsToAnExplicitPageProtection()
	{
		Dictionary<AllocationProtection, MemoryProtection> expected = new()
		{
			[AllocationProtection.ReadWrite] = MemoryProtection.ReadWrite,
			[AllocationProtection.ExecuteReadWrite] = MemoryProtection.ExecuteReadWrite
		};

		MappingTotality.AssertTotal<AllocationProtection>(
			protection => AllocationMapping.TryGetSdkProtection(protection, out MemoryProtection page) &&
						  page == expected[protection],
			static protection => !AllocationMapping.TryGetSdkProtection(protection, out MemoryProtection page) &&
								 page == MemoryProtection.None);
	}

	[Theory]
	[InlineData(EngineEffectState.NotStarted, CheatEngineHostEffect.NotStarted)]
	[InlineData(EngineEffectState.NotApplied, CheatEngineHostEffect.NotApplied)]
	[InlineData(EngineEffectState.Unknown, CheatEngineHostEffect.Unknown)]
	public void AnUnpublishedAllocationTakesItsEffectFromTheSdk(EngineEffectState effect,
		CheatEngineHostEffect hostEffect)
	{
		CheatEngineFailure failure = AllocationMapping.FromUnpublishedAllocation(
			new AllocationAttempt(TargetMemoryOperationOutcomeKind.ExpectedFailure, effect, default, null), 16, Operation);

		Assert.Equal(HostEffectMapping.FromSdk(effect), failure.HostEffect);
		Assert.Equal(hostEffect, failure.HostEffect);
		Assert.Equal(Operation, failure.Operation);
		Assert.Null(failure.Exception);
	}

	[Fact]
	public void AnAppliedAllocationWithoutOwnerOrCompensationMayRemain()
	{
		CheatEngineFailure failure = AllocationMapping.FromUnpublishedAllocation(
			new AllocationAttempt(TargetMemoryOperationOutcomeKind.Succeeded, EngineEffectState.Applied, Allocated,
				null), 16, Operation);

		Assert.Equal(CheatEngineFailureKind.IndeterminateHostResult, failure.Kind);
		Assert.Equal(CheatEngineHostEffect.CleanupUnconfirmed, failure.HostEffect);
		Assert.Contains("16 bytes at 0x200000000", failure.Message, StringComparison.Ordinal);
	}

	[Fact]
	public void AFailureThatStillCarriesAnAddressReportsIt()
	{
		CheatEngineFailure failure = AllocationMapping.FromUnpublishedAllocation(
			new AllocationAttempt(TargetMemoryOperationOutcomeKind.MarshallingFailure, EngineEffectState.Unknown,
				Allocated, null), 16, Operation);

		Assert.Equal(CheatEngineFailureKind.InvalidHostResult, failure.Kind);
		Assert.Equal(CheatEngineHostEffect.Unknown, failure.HostEffect);
		Assert.Contains("16 bytes at 0x200000000", failure.Message, StringComparison.Ordinal);
	}

	[Fact]
	public void TheCompensationTakesPrecedenceOverTheAllocationCategory()
	{
		CheatEngineFailure failure = AllocationMapping.FromUnpublishedAllocation(
			new AllocationAttempt(TargetMemoryOperationOutcomeKind.Succeeded, EngineEffectState.Applied, Allocated,
				TargetReleaseStatus.RefusedTargetChanged), 16, Operation);

		Assert.Equal(CheatEngineFailureKind.TargetChanged, failure.Kind);
		Assert.Equal(CheatEngineHostEffect.CleanupUnconfirmed, failure.HostEffect);
	}

	[Fact]
	public void ACancellationAfterTheAllocationClaimsCompletionOnlyForAConfirmedRelease()
	{
		CheatEngineFailure released = AllocationMapping.CancelledAfterAllocation(
			new LeaseReleaseOutcome(LeaseReleaseKind.Released, CheatEngineHostEffect.Completed), Allocated, 16, Operation);
		CheatEngineFailure unconfirmed = AllocationMapping.CancelledAfterAllocation(
			new LeaseReleaseOutcome(LeaseReleaseKind.CleanupUnconfirmed, CheatEngineHostEffect.Started), Allocated, 16,
			Operation);
		CheatEngineFailure unavailable = AllocationMapping.CancelledAfterAllocation(
			new LeaseReleaseOutcome(LeaseReleaseKind.CleanupUnavailable, CheatEngineHostEffect.NotStarted), Allocated, 16,
			Operation);

		Assert.Equal((CheatEngineFailureKind.Cancelled, CheatEngineHostEffect.Completed), Describe(released));
		Assert.Equal((CheatEngineFailureKind.Cancelled, CheatEngineHostEffect.CleanupUnconfirmed), Describe(unconfirmed));
		Assert.Equal((CheatEngineFailureKind.Cancelled, CheatEngineHostEffect.CleanupUnconfirmed), Describe(unavailable));
		Assert.Contains("16 bytes at 0x200000000", unavailable.Message, StringComparison.Ordinal);
	}

	[Fact]
	public void AValidRequestKeepsItsFacts()
	{
		Address preferred = new(0x1000_0000);

		TargetAllocationRequest request = AllocationMapping.CreateRequest(
			new AllocationRequest(4096, AllocationProtection.ExecuteReadWrite, preferred));

		Assert.Equal(4096, request.Size.Value);
		Assert.Equal(MemoryProtection.ExecuteReadWrite, request.Protection);
		Assert.Equal(preferred, request.PreferredBaseAddress);
	}

	/// <summary>
	///     The default request and a tampered one are programming errors: they throw what the request's constructor
	///     throws for the same value, and no SDK request is built from them.
	/// </summary>
	[Theory]
	[InlineData("Default")]
	[InlineData("NegativeSize")]
	[InlineData("UndefinedProtection")]
	[InlineData("NullPreferredAddress")]
	public void AnInvalidRequestThrowsWhatItsConstructorThrows(string invalid)
	{
		AllocationRequest valid = new(4096);
		AllocationRequest request = invalid switch
		{
			"Default" => default,
			"NegativeSize" => TamperedValues.WithBackingField(valid, nameof(AllocationRequest.Size), -1L),
			"UndefinedProtection" => TamperedValues.WithBackingField(valid, nameof(AllocationRequest.Protection),
				(AllocationProtection) 9),
			"NullPreferredAddress" => TamperedValues.WithBackingField(valid,
				nameof(AllocationRequest.PreferredAddress), (Address?) Address.Zero),
			_ => throw new ArgumentOutOfRangeException(nameof(invalid), invalid, null)
		};

		ArgumentOutOfRangeException thrown =
			Assert.Throws<ArgumentOutOfRangeException>(() => AllocationMapping.CreateRequest(request));

		Assert.Equal("request", thrown.ParamName);
	}

	private static (CheatEngineFailureKind Kind, CheatEngineHostEffect Effect) Describe(CheatEngineFailure failure)
	{
		return (failure.Kind, failure.HostEffect);
	}
}
