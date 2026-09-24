using CheatEngine.Client.Core.Domains;
using CheatEngine.Client.Core.Tests.TestSupport;
using CheatEngine.Client.Results;
using CheatEngine.SDK.Engine.AddressList;
using CheatEngine.SDK.Lua.Calls;

namespace CheatEngine.Client.Core.Tests.Domains;

/// <summary>
///     Every outcome of CheatEngine.SDK 2.0.0's Address List mutations and table files has a deliberate Client
///     counterpart, and a value the SDK could add later fails closed (Q48).
/// </summary>
public sealed class TableMappingTests
{
	private static readonly Dictionary<MemoryRecordMutationProblem, CheatEngineFailureKind> ProblemKinds = new()
	{
		[MemoryRecordMutationProblem.Uninitialized] = CheatEngineFailureKind.IndeterminateHostResult,
		[MemoryRecordMutationProblem.None] = CheatEngineFailureKind.IndeterminateHostResult,
		[MemoryRecordMutationProblem.AddressListUnavailable] = CheatEngineFailureKind.CapabilityUnavailable,
		[MemoryRecordMutationProblem.RecordNotFound] = CheatEngineFailureKind.NotFound,
		[MemoryRecordMutationProblem.ParentNotFound] = CheatEngineFailureKind.NotFound,
		[MemoryRecordMutationProblem.SelfParent] = CheatEngineFailureKind.OperationRejected,
		[MemoryRecordMutationProblem.CycleDetected] = CheatEngineFailureKind.OperationRejected,
		[MemoryRecordMutationProblem.TraversalLimitReached] = CheatEngineFailureKind.ResultLimitExceeded,
		[MemoryRecordMutationProblem.GlobalUnavailable] = CheatEngineFailureKind.CapabilityUnavailable,
		[MemoryRecordMutationProblem.LuaFailure] = CheatEngineFailureKind.LuaError,
		[MemoryRecordMutationProblem.InvalidResult] = CheatEngineFailureKind.InvalidHostResult,
		[MemoryRecordMutationProblem.TableLoadInProgress] = CheatEngineFailureKind.InvalidState,
		[MemoryRecordMutationProblem.RuntimeIdentityChanged] = CheatEngineFailureKind.RuntimeChanged
	};

	private static readonly Dictionary<MemoryRecordMutationEffect, CheatEngineHostEffect> EffectKinds = new()
	{
		[MemoryRecordMutationEffect.NotAttempted] = CheatEngineHostEffect.NotStarted,
		[MemoryRecordMutationEffect.Completed] = CheatEngineHostEffect.Completed,
		[MemoryRecordMutationEffect.Indeterminate] = CheatEngineHostEffect.Started
	};

	private static readonly
		Dictionary<MemoryRecordActivationOutcomeKind, (CheatEngineFailureKind Kind, CheatEngineHostEffect Effect)?>
		ActivationResults = new()
		{
			[MemoryRecordActivationOutcomeKind.Unknown] =
				(CheatEngineFailureKind.IndeterminateHostResult, CheatEngineHostEffect.Unknown),
			[MemoryRecordActivationOutcomeKind.Applied] = null,
			[MemoryRecordActivationOutcomeKind.Unchanged] = null,
			[MemoryRecordActivationOutcomeKind.RefusedByHost] =
				(CheatEngineFailureKind.OperationRejected, CheatEngineHostEffect.Started),
			[MemoryRecordActivationOutcomeKind.Pending] = null,
			[MemoryRecordActivationOutcomeKind.Indeterminate] =
				(CheatEngineFailureKind.IndeterminateHostResult, CheatEngineHostEffect.Started),
			// Refused before the setter: the problem decides the kind (here RecordNotFound).
			[MemoryRecordActivationOutcomeKind.NotAttempted] =
				(CheatEngineFailureKind.NotFound, CheatEngineHostEffect.NotStarted)
		};

	private static readonly
		Dictionary<LuaOperationStatusKind, (CheatEngineFailureKind Kind, CheatEngineHostEffect Effect)?>
		TableFileResults = new()
		{
			[LuaOperationStatusKind.Unknown] =
				(CheatEngineFailureKind.IndeterminateHostResult, CheatEngineHostEffect.Unknown),
			[LuaOperationStatusKind.Success] = null,
			[LuaOperationStatusKind.GlobalUnavailable] =
				(CheatEngineFailureKind.CapabilityUnavailable, CheatEngineHostEffect.NotStarted),
			[LuaOperationStatusKind.LuaFailure] = (CheatEngineFailureKind.LuaError, CheatEngineHostEffect.Started),
			[LuaOperationStatusKind.NilResult] = (CheatEngineFailureKind.InvalidHostResult, CheatEngineHostEffect.Started),
			[LuaOperationStatusKind.InvalidResult] =
				(CheatEngineFailureKind.InvalidHostResult, CheatEngineHostEffect.Started),
			[LuaOperationStatusKind.StackUnavailable] = (CheatEngineFailureKind.LuaError, CheatEngineHostEffect.NotStarted),
			[LuaOperationStatusKind.MissingResult] =
				(CheatEngineFailureKind.InvalidHostResult, CheatEngineHostEffect.Started),
			[LuaOperationStatusKind.ResultCapacityExceeded] =
				(CheatEngineFailureKind.InvalidHostResult, CheatEngineHostEffect.Started)
		};

	[Fact]
	[Trait("Qualification", "Q48")]
	public void EveryMutationProblemIsMappedAndAnUnknownProblemFailsClosed()
	{
		MappingTotality.AssertTotal<MemoryRecordMutationProblem>(
			static problem => ProblemKinds.TryGetValue(problem, out CheatEngineFailureKind expected) &&
							  TableMapping.ToFailureKind(problem) == expected,
			static problem => TableMapping.ToFailureKind(problem) == CheatEngineFailureKind.IndeterminateHostResult);
	}

	[Fact]
	[Trait("Qualification", "Q48")]
	public void EveryMutationEffectIsMappedAndAnUnknownEffectFailsClosed()
	{
		MappingTotality.AssertTotal<MemoryRecordMutationEffect>(
			static effect => EffectKinds.TryGetValue(effect, out CheatEngineHostEffect expected) &&
							 TableMapping.ToHostEffect(effect) == expected,
			static effect => TableMapping.ToHostEffect(effect) == CheatEngineHostEffect.Unknown);
	}

	[Fact]
	[Trait("Qualification", "Q48")]
	public void EveryActivationOutcomeIsMappedAndAnUnknownOutcomeFailsClosed()
	{
		MappingTotality.AssertTotal<MemoryRecordActivationOutcomeKind>(
			static kind => ActivationResults.TryGetValue(kind,
								out (CheatEngineFailureKind Kind, CheatEngineHostEffect Effect)? expected) &&
							TableMapping.ToActivationFailure(kind, MemoryRecordMutationProblem.RecordNotFound) ==
							expected,
			static kind => TableMapping.ToActivationFailure(kind, MemoryRecordMutationProblem.None) ==
						   (CheatEngineFailureKind.IndeterminateHostResult, CheatEngineHostEffect.Unknown));
	}

	[Fact]
	[Trait("Qualification", "Q48")]
	public void EveryTableFileStatusIsMappedAndAnUnknownStatusFailsClosed()
	{
		MappingTotality.AssertTotal<LuaOperationStatusKind>(
			static status => TableFileResults.TryGetValue(status,
								  out (CheatEngineFailureKind Kind, CheatEngineHostEffect Effect)? expected) &&
							  TableMapping.ToTableFileFailure(status) == expected,
			static status => TableMapping.ToTableFileFailure(status) ==
							  (CheatEngineFailureKind.IndeterminateHostResult, CheatEngineHostEffect.Unknown));
	}

	[Theory]
	[Trait("Qualification", "Q34")]
	[InlineData(true)]
	[InlineData(false)]
	public void TableFileFailuresNameTheActionAndNeverThePath(bool load)
	{
		foreach (LuaOperationStatusKind status in Enum.GetValues<LuaOperationStatusKind>())
		{
			bool succeeded = TableMapping.TryClassifyTableFile("Tables.LoadTrustedTable", load, status,
				out CheatEngineFailure failure);

			Assert.Equal(status == LuaOperationStatusKind.Success, succeeded);
			if (!succeeded)
			{
				Assert.Equal("Tables.LoadTrustedTable", failure.Operation);
				Assert.DoesNotContain(".ct", failure.Message, StringComparison.OrdinalIgnoreCase);
				Assert.DoesNotContain(load ? "save" : "load", failure.Message, StringComparison.Ordinal);
			}
		}
	}

	[Fact]
	public void AnUnrecognizedMutationProblemNeverReportsAnEstablishedEffect()
	{
		// A NotAttempted outcome without a recognized problem is not a shape CheatEngine.SDK produces.
		CheatEngineFailure uninitialized = TableMapping.MutationFailure("Tables.Delete", default);
		CheatEngineFailure noProblem = TableMapping.MutationFailure("Tables.Delete",
			new TableRecordMutationOutcome(MemoryRecordMutationEffect.Indeterminate, MemoryRecordMutationProblem.None));
		CheatEngineFailure undefined = TableMapping.MutationFailure("Tables.Delete",
			TableRecordMutationOutcome.NotAttempted(MappingTotality.Undefined<MemoryRecordMutationProblem>()));

		Assert.All((CheatEngineFailure[]) [uninitialized, noProblem, undefined], static failure =>
		{
			Assert.Equal(CheatEngineFailureKind.IndeterminateHostResult, failure.Kind);
			Assert.Equal(CheatEngineHostEffect.Unknown, failure.HostEffect);
			Assert.Equal("Tables.Delete", failure.Operation);
		});
	}

	[Fact]
	public void TheDefaultMutationOutcomeIsNeverASuccess()
	{
		Assert.False(default(TableRecordMutationOutcome).IsSuccess);
		Assert.False(TableRecordMutationOutcome.CompletedWithoutSnapshot.IsSuccess);
		Assert.True(TableRecordMutationOutcome.Succeeded.IsSuccess);
	}

	[Theory]
	[InlineData(MemoryRecordActivationOutcomeKind.Applied, true)]
	[InlineData(MemoryRecordActivationOutcomeKind.Unchanged, true)]
	[InlineData(MemoryRecordActivationOutcomeKind.Pending, true)]
	[InlineData(MemoryRecordActivationOutcomeKind.RefusedByHost, true)]
	[InlineData(MemoryRecordActivationOutcomeKind.Indeterminate, false)]
	[InlineData(MemoryRecordActivationOutcomeKind.NotAttempted, false)]
	[InlineData(MemoryRecordActivationOutcomeKind.Unknown, false)]
	public void TheRecordIsCopiedOnlyAfterAnActivationWhoseOutcomeIsKnown(MemoryRecordActivationOutcomeKind kind,
		bool copied)
	{
		Assert.Equal(copied, TableMapping.CopiesRecord(kind));
	}

	[Fact]
	public void MutationMessagesNameTheCategoryAndTheTraversalBound()
	{
		CheatEngineFailure traversal = TableMapping.MutationFailure("Tables.SetParent",
			TableRecordMutationOutcome.NotAttempted(MemoryRecordMutationProblem.TraversalLimitReached));
		CheatEngineFailure started = TableMapping.MutationFailure("Tables.Delete",
			new TableRecordMutationOutcome(MemoryRecordMutationEffect.Indeterminate,
				MemoryRecordMutationProblem.LuaFailure));
		CheatEngineFailure refused = TableMapping.MutationFailure("Tables.Delete",
			TableRecordMutationOutcome.NotAttempted(MemoryRecordMutationProblem.LuaFailure));

		Assert.Contains("4096", traversal.Message, StringComparison.Ordinal);
		Assert.Equal(CheatEngineHostEffect.Started, started.HostEffect);
		Assert.Contains("after the change started", started.Message, StringComparison.Ordinal);
		Assert.Equal(CheatEngineHostEffect.NotStarted, refused.HostEffect);
		Assert.Contains("before the change was attempted", refused.Message, StringComparison.Ordinal);
	}
}
