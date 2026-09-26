using System.Diagnostics.CodeAnalysis;

using CheatEngine.Client.Core.Domains;
using CheatEngine.Client.Core.Infrastructure;
using CheatEngine.Client.Dispatching;
using CheatEngine.Client.Results;
using CheatEngine.Client.Tables;
using CheatEngine.SDK.Engine.AddressList;
using CheatEngine.SDK.Engine.Enums;

namespace CheatEngine.Client.Core.Tests.Domains;

/// <summary>
///     The Tables client over a fake mutation port: every outcome CheatEngine.SDK's <c>AddressListMutations</c> reports,
///     and every outcome of the Client's own create and select steps, reaches the public failure contract through
///     <see cref="TableMapping" />.
/// </summary>
public sealed class TableClientMutationTests
{
	public static TheoryData<MemoryRecordMutationProblem, CheatEngineFailureKind> ParentRefusals => new()
	{
		{ MemoryRecordMutationProblem.CycleDetected, CheatEngineFailureKind.OperationRejected },
		{ MemoryRecordMutationProblem.SelfParent, CheatEngineFailureKind.OperationRejected },
		{ MemoryRecordMutationProblem.TraversalLimitReached, CheatEngineFailureKind.ResultLimitExceeded },
		{ MemoryRecordMutationProblem.ParentNotFound, CheatEngineFailureKind.NotFound },
		{ MemoryRecordMutationProblem.RecordNotFound, CheatEngineFailureKind.NotFound },
		{ MemoryRecordMutationProblem.TableLoadInProgress, CheatEngineFailureKind.InvalidState },
		{ MemoryRecordMutationProblem.RuntimeIdentityChanged, CheatEngineFailureKind.RuntimeChanged },
		{ MemoryRecordMutationProblem.GlobalUnavailable, CheatEngineFailureKind.CapabilityUnavailable },
		{ MemoryRecordMutationProblem.LuaFailure, CheatEngineFailureKind.LuaError }
	};

	[Fact]
	public void TryDeleteDispatchesTheRequestedRecordAndReturnsSuccess()
	{
		FakeRecordMutationPort mutations = new();
		TableClient client = CreateClient(mutations);

		bool succeeded =
			client.TryDelete(new MemoryRecordId(41), out CheatEngineFailure failure,
				TestContext.Current.CancellationToken);

		Assert.True(succeeded);
		Assert.Equal(new MemoryRecordId(41), mutations.LastDeletedId);
		Assert.Equal(default, failure);
	}

	[Fact]
	[Trait("Qualification", "Q34")]
	public void TryDeleteClassifiesAnAbsentRecord()
	{
		TableClient client = CreateClient(new FakeRecordMutationPort
		{
			DeleteOutcome = TableRecordMutationOutcome.NotAttempted(MemoryRecordMutationProblem.RecordNotFound)
		});

		bool succeeded =
			client.TryDelete(new MemoryRecordId(41), out CheatEngineFailure failure,
				TestContext.Current.CancellationToken);

		Assert.False(succeeded);
		Assert.Equal(CheatEngineFailureKind.NotFound, failure.Kind);
		Assert.Equal(CheatEngineHostEffect.NotStarted, failure.HostEffect);
		Assert.Equal("Tables.Delete", failure.Operation);
		Assert.Equal(TableMapping.RecordNotFoundMessage, failure.Message);
	}

	[Fact]
	[Trait("Qualification", "Q34")]
	public void DeleteThrowsAStartedLuaFailureThatIsNeverRetried()
	{
		// AddressListMutations.Delete reports a destroy that raised after it started as Indeterminate: part of it may
		// persist, and the Client never retries it.
		FakeRecordMutationPort mutations = new()
		{
			DeleteOutcome = new TableRecordMutationOutcome(MemoryRecordMutationEffect.Indeterminate,
				MemoryRecordMutationProblem.LuaFailure)
		};
		TableClient client = CreateClient(mutations);

		CheatEngineOperationException exception = Assert.Throws<CheatEngineOperationException>(() =>
			client.Delete(new MemoryRecordId(41), TestContext.Current.CancellationToken));

		Assert.Equal(CheatEngineFailureKind.LuaError, exception.Failure.Kind);
		Assert.Equal(CheatEngineHostEffect.Started, exception.Failure.HostEffect);
		Assert.Equal("Tables.Delete", exception.Failure.Operation);
		Assert.Contains("effect is unknown", exception.Failure.Message, StringComparison.Ordinal);
		Assert.Equal(1, mutations.DeleteCallCount);
	}

	[Fact]
	public void TrySetParentPassesBothIdsAndReturnsTheCopiedPostMutationSnapshot()
	{
		MemoryRecordSnapshot expected = Snapshot(41, "Ammo");
		FakeRecordMutationPort mutations = new()
		{
			SetParentRecord = expected
		};
		TableClient client = CreateClient(mutations);

		bool succeeded = client.TrySetParent(new MemoryRecordId(41), new MemoryRecordId(12),
			out MemoryRecordSnapshot actual,
			out CheatEngineFailure failure, TestContext.Current.CancellationToken);

		Assert.True(succeeded);
		Assert.Equal(new MemoryRecordId(41), mutations.LastChildId);
		Assert.Equal(new MemoryRecordId(12), mutations.LastParentId);
		Assert.Equal(expected, actual);
		Assert.Equal(default, failure);
	}

	[Fact]
	public void TrySetParentWithNullParentRequestsTheAddressListRoot()
	{
		FakeRecordMutationPort mutations = new()
		{
			SetParentRecord = Snapshot(41, "Ammo")
		};
		TableClient client = CreateClient(mutations);

		bool succeeded = client.TrySetParent(new MemoryRecordId(41), null, out _, out CheatEngineFailure failure,
			TestContext.Current.CancellationToken);

		Assert.True(succeeded);
		Assert.Null(mutations.LastParentId);
		Assert.Equal(default, failure);
	}

	[Fact]
	public void TrySetParentRejectsARecordAsItsOwnParentBeforeDispatching()
	{
		FakeRecordMutationPort mutations = new();
		TableClient client = CreateClient(mutations);

		bool succeeded = client.TrySetParent(new MemoryRecordId(41), new MemoryRecordId(41),
			out MemoryRecordSnapshot record,
			out CheatEngineFailure failure, TestContext.Current.CancellationToken);

		Assert.False(succeeded);
		Assert.Equal(default, record);
		Assert.Equal(CheatEngineFailureKind.OperationRejected, failure.Kind);
		Assert.Equal(CheatEngineHostEffect.NotStarted, failure.HostEffect);
		Assert.Equal("Tables.SetParent", failure.Operation);
		Assert.Equal("A memory record cannot be its own parent.", failure.Message);
		Assert.Equal(0, mutations.SetParentCallCount);
	}

	[Theory]
	[Trait("Qualification", "Q34")]
	[MemberData(nameof(ParentRefusals))]
	public void TrySetParentReportsEveryRefusalOfCheatEngineSdkAsNotStarted(MemoryRecordMutationProblem problem,
		CheatEngineFailureKind expected)
	{
		FakeRecordMutationPort mutations = new()
		{
			SetParentOutcome = TableRecordMutationOutcome.NotAttempted(problem),
			SetParentRecord = Snapshot(41, "Ammo")
		};
		TableClient client = CreateClient(mutations);

		bool succeeded = client.TrySetParent(new MemoryRecordId(41), new MemoryRecordId(12),
			out MemoryRecordSnapshot record, out CheatEngineFailure failure, TestContext.Current.CancellationToken);

		Assert.False(succeeded);
		Assert.Equal(default, record);
		Assert.Equal(expected, failure.Kind);
		Assert.Equal(CheatEngineHostEffect.NotStarted, failure.HostEffect);
		Assert.Equal("Tables.SetParent", failure.Operation);
		Assert.Equal(1, mutations.SetParentCallCount);
	}

	[Fact]
	public void TrySetParentKeepsACompletedMoveApartFromAFailedCopyOfTheRecord()
	{
		// CheatEngine.SDK asks callers never to merge a failed post-command read with the command result.
		FakeRecordMutationPort mutations = new()
		{
			SetParentOutcome = TableRecordMutationOutcome.CompletedWithoutSnapshot
		};
		TableClient client = CreateClient(mutations);

		bool succeeded = client.TrySetParent(new MemoryRecordId(41), new MemoryRecordId(12),
			out MemoryRecordSnapshot record, out CheatEngineFailure failure, TestContext.Current.CancellationToken);

		Assert.False(succeeded);
		Assert.Equal(default, record);
		Assert.Equal(CheatEngineFailureKind.InvalidHostResult, failure.Kind);
		Assert.Equal(CheatEngineHostEffect.Completed, failure.HostEffect);
		Assert.Equal("Cheat Engine completed the change, but the memory record could not be copied afterwards.",
			failure.Message);
	}

	[Fact]
	[Trait("Qualification", "Q34")]
	public void SetParentThrowsAClassifiedMissingParentFailure()
	{
		TableClient client = CreateClient(new FakeRecordMutationPort
		{
			SetParentOutcome = TableRecordMutationOutcome.NotAttempted(MemoryRecordMutationProblem.ParentNotFound)
		});

		CheatEngineOperationException exception = Assert.Throws<CheatEngineOperationException>(() =>
			client.SetParent(new MemoryRecordId(41), new MemoryRecordId(12), TestContext.Current.CancellationToken));

		Assert.Equal(CheatEngineFailureKind.NotFound, exception.Failure.Kind);
		Assert.Equal("Tables.SetParent", exception.Failure.Operation);
		Assert.Contains("parent", exception.Failure.Message, StringComparison.OrdinalIgnoreCase);
	}

	[Fact]
	[Trait("Qualification", "Q34")]
	public void AMutationRefusedDuringATableLoadThrowsTheLifecycleException()
	{
		// InvalidState maps to CheatEngineInvalidStateException on the throwing form.
		TableClient client = CreateClient(new FakeRecordMutationPort
		{
			DeleteOutcome = TableRecordMutationOutcome.NotAttempted(MemoryRecordMutationProblem.TableLoadInProgress)
		});

		CheatEngineInvalidStateException exception = Assert.Throws<CheatEngineInvalidStateException>(() =>
			client.Delete(new MemoryRecordId(41), TestContext.Current.CancellationToken));

		Assert.Equal(CheatEngineFailureKind.InvalidState, exception.Failure.Kind);
		Assert.Equal(CheatEngineHostEffect.NotStarted, exception.Failure.HostEffect);
	}

	[Fact]
	[Trait("Qualification", "Q43")]
	public void FailedCreateWithUnconfirmedRollbackReportsCleanupUnconfirmed()
	{
		InvalidOperationException rollbackFault = new("delete faulted");
		FakeRecordMutationPort mutations = new()
		{
			Creation = new TableRecordCreation(
				TableRecordMutationOutcome.NotAttempted(MemoryRecordMutationProblem.ParentNotFound),
				TableRecordRollback.Unconfirmed, RollbackFault: rollbackFault)
		};
		TableClient client = CreateClient(mutations);

		bool succeeded = client.TryCreate(Definition(), out MemoryRecordSnapshot record,
			out CheatEngineFailure failure, TestContext.Current.CancellationToken);

		Assert.False(succeeded);
		Assert.Equal(default, record);
		Assert.Equal(CheatEngineFailureKind.NotFound, failure.Kind);
		Assert.Equal("Tables.Create", failure.Operation);
		Assert.Equal(CheatEngineHostEffect.CleanupUnconfirmed, failure.HostEffect);
		Assert.Contains("may remain in the Address List", failure.Message, StringComparison.Ordinal);
		Assert.Same(rollbackFault, failure.Exception);
		Assert.Equal(1, mutations.CreateCallCount);
	}

	[Fact]
	public void FailedCreateWithConfirmedRollbackReportsACompletedHostEffect()
	{
		TableClient client = CreateClient(new FakeRecordMutationPort
		{
			Creation = new TableRecordCreation(TableRecordMutationOutcome.InvalidResultAfterInvocation,
				TableRecordRollback.Confirmed)
		});

		bool succeeded = client.TryCreate(Definition(), out _, out CheatEngineFailure failure,
			TestContext.Current.CancellationToken);

		Assert.False(succeeded);
		Assert.Equal(CheatEngineFailureKind.InvalidHostResult, failure.Kind);
		Assert.Equal(CheatEngineHostEffect.Completed, failure.HostEffect);
		Assert.Equal(TableMapping.InvalidContractMessage, failure.Message);
	}

	[Fact]
	public void FailedCreateKeepsBothTheCreationAndTheRollbackFaults()
	{
		InvalidOperationException creationFault = new("initialization faulted");
		InvalidOperationException rollbackFault = new("delete faulted");
		TableClient client = CreateClient(new FakeRecordMutationPort
		{
			Creation = new TableRecordCreation(TableRecordMutationOutcome.InvalidResultAfterInvocation,
				TableRecordRollback.Unconfirmed, creationFault, rollbackFault)
		});

		bool succeeded = client.TryCreate(Definition(), out _, out CheatEngineFailure failure,
			TestContext.Current.CancellationToken);

		Assert.False(succeeded);
		Assert.Equal(CheatEngineFailureKind.OperationRejected, failure.Kind);
		Assert.Equal(CheatEngineHostEffect.CleanupUnconfirmed, failure.HostEffect);
		AggregateException aggregate = Assert.IsType<AggregateException>(failure.Exception);
		Assert.Collection(
			aggregate.InnerExceptions,
			first => Assert.Same(creationFault, first),
			second => Assert.Same(rollbackFault, second));
	}

	[Fact]
	public void SuccessfulCreateReturnsTheCopiedSnapshot()
	{
		MemoryRecordSnapshot expected = Snapshot(51, "Health");
		TableClient client = CreateClient(new FakeRecordMutationPort
		{
			CreatedRecord = expected
		});

		bool succeeded = client.TryCreate(Definition(), out MemoryRecordSnapshot record,
			out CheatEngineFailure failure, TestContext.Current.CancellationToken);

		Assert.True(succeeded);
		Assert.Equal(default, failure);
		Assert.Equal(expected, record);
	}

	[Theory]
	[Trait("Qualification", "Q35")]
	[InlineData(true, "left the memory record inactive")]
	[InlineData(false, "left the memory record active")]
	public void TrySetActiveReportsARefusalByTheHostWithThePostChangeSnapshot(bool requested, string expectedMessage)
	{
		FakeRecordMutationPort mutations = new()
		{
			Activation = new TableActivationObservation(MemoryRecordActivationOutcomeKind.RefusedByHost,
				MemoryRecordMutationProblem.None, Snapshot(41, "Health", !requested))
		};
		TableClient client = CreateClient(mutations);

		bool succeeded = client.TrySetActive(new MemoryRecordId(41), requested, out MemoryRecordSnapshot record,
			out CheatEngineFailure failure, TestContext.Current.CancellationToken);

		Assert.False(succeeded);
		Assert.Equal(CheatEngineFailureKind.OperationRejected, failure.Kind);
		Assert.Equal(CheatEngineHostEffect.Started, failure.HostEffect);
		Assert.Equal("Tables.SetActive", failure.Operation);
		Assert.Contains(expectedMessage, failure.Message, StringComparison.Ordinal);
		Assert.Contains("partial script effects may persist", failure.Message, StringComparison.Ordinal);
		Assert.Equal(new MemoryRecordId(41), record.Id);
		Assert.Equal(!requested, record.State.IsActive);
		Assert.Equal([requested], mutations.RequestedStates);
	}

	[Theory]
	[Trait("Qualification", "Q35")]
	[InlineData(MemoryRecordActivationOutcomeKind.Applied)]
	[InlineData(MemoryRecordActivationOutcomeKind.Unchanged)]
	public void TrySetActiveSucceedsWithThePostChangeSnapshot(MemoryRecordActivationOutcomeKind kind)
	{
		FakeRecordMutationPort mutations = new()
		{
			Activation = new TableActivationObservation(kind, MemoryRecordMutationProblem.None,
				Snapshot(41, "Health", true))
		};
		TableClient client = CreateClient(mutations);

		bool succeeded = client.TrySetActive(new MemoryRecordId(41), true, out MemoryRecordSnapshot record,
			out CheatEngineFailure failure, TestContext.Current.CancellationToken);

		Assert.True(succeeded);
		Assert.Equal(default, failure);
		Assert.True(record.State.IsActive);
		Assert.Equal(1, mutations.SetActiveCallCount);
	}

	[Theory]
	[InlineData(MemoryRecordActivationOutcomeKind.Applied, CheatEngineHostEffect.Completed)]
	[InlineData(MemoryRecordActivationOutcomeKind.Unchanged, CheatEngineHostEffect.NotStarted)]
	[InlineData(MemoryRecordActivationOutcomeKind.Pending, CheatEngineHostEffect.Started)]
	public void TrySetActiveKeepsASuccessfulCommandApartFromAFailedCopyOfTheRecord(
		MemoryRecordActivationOutcomeKind kind, CheatEngineHostEffect expectedEffect)
	{
		TableClient client = CreateClient(new FakeRecordMutationPort
		{
			Activation = TableActivationObservation.Of(kind)
		});

		bool succeeded = client.TrySetActive(new MemoryRecordId(41), true, out MemoryRecordSnapshot record,
			out CheatEngineFailure failure, TestContext.Current.CancellationToken);

		Assert.False(succeeded);
		Assert.Equal(default, record);
		Assert.Equal(CheatEngineFailureKind.InvalidHostResult, failure.Kind);
		Assert.Equal(expectedEffect, failure.HostEffect);
	}

	[Fact]
	[Trait("Qualification", "Q35")]
	public void TrySetActiveSucceedsForAnAsynchronousRecordStillProcessingWithItsSnapshotSayingSo()
	{
		// Pending: the setter ran and the asynchronous activation is still processing; the snapshot copied after the
		// command reports it, and a later snapshot observes the final state.
		MemoryRecordSnapshot processing = new(new MemoryRecordId(41), 0,
			new MemoryRecordContentSnapshot("Script", string.Empty, string.Empty, VariableType.Dword, "[ENABLE]"),
			new MemoryRecordStateSnapshot(null, isActive: false, isAsync: true, isAsyncProcessing: true));
		TableClient client = CreateClient(new FakeRecordMutationPort
		{
			Activation = new TableActivationObservation(MemoryRecordActivationOutcomeKind.Pending,
				MemoryRecordMutationProblem.None, processing)
		});

		bool succeeded = client.TrySetActive(new MemoryRecordId(41), true, out MemoryRecordSnapshot record,
			out CheatEngineFailure failure, TestContext.Current.CancellationToken);

		Assert.True(succeeded);
		Assert.Equal(default, failure);
		Assert.Equal(processing, record);
		Assert.True(record.State.IsAsyncProcessing);
	}

	[Fact]
	public void TrySetActiveReportsAStartedIndeterminateActivationWithoutASnapshot()
	{
		// A14-42: the setter ran, but CheatEngine.SDK could not establish the record's state after it.
		TableClient client = CreateClient(new FakeRecordMutationPort
		{
			Activation = new TableActivationObservation(MemoryRecordActivationOutcomeKind.Indeterminate,
				MemoryRecordMutationProblem.LuaFailure, Snapshot(41, "Health"))
		});

		bool succeeded = client.TrySetActive(new MemoryRecordId(41), true, out MemoryRecordSnapshot record,
			out CheatEngineFailure failure, TestContext.Current.CancellationToken);

		Assert.False(succeeded);
		Assert.Equal(CheatEngineFailureKind.IndeterminateHostResult, failure.Kind);
		Assert.Equal(CheatEngineHostEffect.Started, failure.HostEffect);
		Assert.Equal(default, record);
	}

	[Theory]
	[Trait("Qualification", "Q34")]
	[InlineData(MemoryRecordMutationProblem.RecordNotFound, CheatEngineFailureKind.NotFound)]
	[InlineData(MemoryRecordMutationProblem.AddressListUnavailable, CheatEngineFailureKind.CapabilityUnavailable)]
	[InlineData(MemoryRecordMutationProblem.TableLoadInProgress, CheatEngineFailureKind.InvalidState)]
	[InlineData(MemoryRecordMutationProblem.RuntimeIdentityChanged, CheatEngineFailureKind.RuntimeChanged)]
	[InlineData(MemoryRecordMutationProblem.InvalidResult, CheatEngineFailureKind.InvalidHostResult)]
	public void TrySetActiveReportsAnActivationThatWasNotAttemptedAsNotStarted(MemoryRecordMutationProblem problem,
		CheatEngineFailureKind expected)
	{
		FakeRecordMutationPort mutations = new()
		{
			Activation = TableActivationObservation.Of(MemoryRecordActivationOutcomeKind.NotAttempted, problem)
		};
		TableClient client = CreateClient(mutations);

		bool succeeded = client.TrySetActive(new MemoryRecordId(41), true, out MemoryRecordSnapshot record,
			out CheatEngineFailure failure, TestContext.Current.CancellationToken);

		Assert.False(succeeded);
		Assert.Equal(default, record);
		Assert.Equal(expected, failure.Kind);
		Assert.Equal(CheatEngineHostEffect.NotStarted, failure.HostEffect);
		Assert.Equal(1, mutations.SetActiveCallCount);
	}

	[Fact]
	public void TrySetActiveFailsClosedOnAnUnrecognizedActivationOutcome()
	{
		TableClient client = CreateClient(new FakeRecordMutationPort
		{
			Activation = new TableActivationObservation(MemoryRecordActivationOutcomeKind.Unknown,
				MemoryRecordMutationProblem.None, Snapshot(41, "Health"))
		});

		bool succeeded = client.TrySetActive(new MemoryRecordId(41), true, out MemoryRecordSnapshot record,
			out CheatEngineFailure failure, TestContext.Current.CancellationToken);

		Assert.False(succeeded);
		Assert.Equal(default, record);
		Assert.Equal(CheatEngineFailureKind.IndeterminateHostResult, failure.Kind);
		Assert.Equal(CheatEngineHostEffect.Unknown, failure.HostEffect);
	}

	[Fact]
	[Trait("Qualification", "Q34")]
	public void DeletingTheSameRecordTwiceReportsNotFoundTheSecondTime()
	{
		// A14-38: the second delete finds no record, so the record is never destroyed twice.
		FakeRecordMutationPort mutations = new()
		{
			TrackDeletedRecords = true
		};
		TableClient client = CreateClient(mutations);

		bool first = client.TryDelete(new MemoryRecordId(41), out CheatEngineFailure firstFailure,
			TestContext.Current.CancellationToken);
		bool second = client.TryDelete(new MemoryRecordId(41), out CheatEngineFailure secondFailure,
			TestContext.Current.CancellationToken);

		Assert.True(first);
		Assert.Equal(default, firstFailure);
		Assert.False(second);
		Assert.Equal(CheatEngineFailureKind.NotFound, secondFailure.Kind);
		Assert.Equal(1, mutations.DestroyCount);
	}

	[Fact]
	public void SelectIsDispatchedAsAHostVisibleMutation()
	{
		// A14-08: changing Cheat Engine's GUI selection is a host-visible mutation, never a read of a cache.
		FakeRecordMutationPort mutations = new()
		{
			SelectRecord = Snapshot(41, "Ammo")
		};
		TableClient client = CreateClient(mutations);

		bool succeeded = client.TrySelectRecord(new MemoryRecordId(41), out MemoryRecordSnapshot record,
			out CheatEngineFailure failure, TestContext.Current.CancellationToken);

		Assert.True(succeeded);
		Assert.Equal(default, failure);
		Assert.Equal(new MemoryRecordId(41), record.Id);
		Assert.Equal([new MemoryRecordId(41)], mutations.SelectedIds);
	}

	[Fact]
	public void MutationsReportCapabilityUnavailableWhenTheAddressListIsUnavailable()
	{
		// ADR-08: an unavailable Address List is the same capability condition for every mutation as for the lookups,
		// never an unexpected host result; no record was reached.
		TableRecordMutationOutcome unavailable =
			TableRecordMutationOutcome.NotAttempted(MemoryRecordMutationProblem.AddressListUnavailable);
		FakeRecordMutationPort mutations = new()
		{
			SelectOutcome = unavailable,
			DeleteOutcome = unavailable,
			SetParentOutcome = unavailable,
			Creation = new TableRecordCreation(unavailable, TableRecordRollback.NotRequired),
			Activation = TableActivationObservation.Of(MemoryRecordActivationOutcomeKind.NotAttempted,
				MemoryRecordMutationProblem.AddressListUnavailable)
		};
		TableClient client = CreateClient(mutations);
		CancellationToken token = TestContext.Current.CancellationToken;

		bool selected = client.TrySelectRecord(new MemoryRecordId(41), out _, out CheatEngineFailure selectFailure,
			token);
		bool deleted = client.TryDelete(new MemoryRecordId(41), out CheatEngineFailure deleteFailure, token);
		bool reparented = client.TrySetParent(new MemoryRecordId(41), new MemoryRecordId(7), out _,
			out CheatEngineFailure parentFailure, token);
		bool created = client.TryCreate(Definition(), out _, out CheatEngineFailure createFailure, token);
		bool activated = client.TrySetActive(new MemoryRecordId(41), true, out _, out CheatEngineFailure activeFailure,
			token);

		Assert.False(selected || deleted || reparented || created || activated);
		foreach (CheatEngineFailure failure in (CheatEngineFailure[])
				 [selectFailure, deleteFailure, parentFailure, createFailure, activeFailure])
		{
			Assert.Equal(CheatEngineFailureKind.CapabilityUnavailable, failure.Kind);
			Assert.Equal(CheatEngineHostEffect.NotStarted, failure.HostEffect);
			Assert.Equal(TableMapping.AddressListUnavailableMessage, failure.Message);
		}
	}

	private static MemoryRecordDefinition Definition()
	{
		return new MemoryRecordDefinition("Health", "game.exe+24", "100", VariableType.Dword);
	}

	private static TableClient CreateClient(FakeRecordMutationPort mutations)
	{
		return new TableClient(new InlineDispatcher(), CoreClientPolicy.SafeDefaults, mutations);
	}

	private static MemoryRecordSnapshot Snapshot(int id, string description, bool isActive = false)
	{
		return new MemoryRecordSnapshot(
			new MemoryRecordId(id),
			0,
			new MemoryRecordContentSnapshot(description, "game.exe+24", "50", VariableType.Dword),
			new MemoryRecordStateSnapshot(null, isActive));
	}

	/// <summary>
	///     A scripted mutation port: each member returns its configured outcome in CheatEngine.SDK's mutation vocabulary
	///     and records how it was called.
	/// </summary>
	private sealed class FakeRecordMutationPort : ITableRecordMutationPort
	{
		internal TableRecordMutationOutcome DeleteOutcome
		{
			get;
			init;
		} = TableRecordMutationOutcome.Succeeded;

		internal TableRecordMutationOutcome SetParentOutcome
		{
			get;
			init;
		} = TableRecordMutationOutcome.Succeeded;

		internal MemoryRecordSnapshot SetParentRecord
		{
			get;
			init;
		}

		internal TableRecordCreation Creation
		{
			get;
			init;
		} = TableRecordCreation.Created;

		internal MemoryRecordSnapshot CreatedRecord
		{
			get;
			init;
		}

		internal TableActivationObservation Activation
		{
			get;
			init;
		} = new(MemoryRecordActivationOutcomeKind.Applied, MemoryRecordMutationProblem.None, Snapshot(41, "Health"));

		internal MemoryRecordSnapshot SelectRecord
		{
			get;
			init;
		}

		internal TableRecordMutationOutcome SelectOutcome
		{
			get;
			init;
		} = TableRecordMutationOutcome.Succeeded;

		internal bool TrackDeletedRecords
		{
			get;
			init;
		}

		internal int CreateCallCount
		{
			get;
			private set;
		}

		internal int DeleteCallCount
		{
			get;
			private set;
		}

		internal MemoryRecordId LastDeletedId
		{
			get;
			private set;
		}

		internal int DestroyCount
		{
			get;
			private set;
		}

		internal MemoryRecordId LastChildId
		{
			get;
			private set;
		}

		internal MemoryRecordId? LastParentId
		{
			get;
			private set;
		}

		internal int SetParentCallCount
		{
			get;
			private set;
		}

		internal int SetActiveCallCount => RequestedStates.Count;

		internal List<bool> RequestedStates
		{
			get;
		} = [];

		internal List<MemoryRecordId> SelectedIds
		{
			get;
		} = [];

		private HashSet<MemoryRecordId> DeletedIds
		{
			get;
		} = [];

		public TableRecordCreation TryCreate(MemoryRecordDefinition definition, out MemoryRecordSnapshot record)
		{
			CreateCallCount++;
			record = Creation.Outcome.IsSuccess ? CreatedRecord : default;
			return Creation;
		}

		public TableRecordMutationOutcome TryDelete(MemoryRecordId id)
		{
			DeleteCallCount++;
			LastDeletedId = id;
			if (!TrackDeletedRecords)
			{
				return DeleteOutcome;
			}

			if (!DeletedIds.Add(id))
			{
				return TableRecordMutationOutcome.NotAttempted(MemoryRecordMutationProblem.RecordNotFound);
			}

			DestroyCount++;
			return TableRecordMutationOutcome.Succeeded;
		}

		public TableActivationObservation TrySetActive(MemoryRecordId id, bool requested)
		{
			RequestedStates.Add(requested);
			return Activation;
		}

		public TableRecordMutationOutcome TrySelect(MemoryRecordId id, out MemoryRecordSnapshot record)
		{
			SelectedIds.Add(id);
			record = SelectOutcome.IsSuccess ? SelectRecord : default;
			return SelectOutcome;
		}

		public TableRecordMutationOutcome TrySetParent(MemoryRecordId childId, MemoryRecordId? parentId,
			out MemoryRecordSnapshot record)
		{
			SetParentCallCount++;
			LastChildId = childId;
			LastParentId = parentId;
			record = SetParentRecord;
			return SetParentOutcome;
		}
	}

	private sealed class InlineDispatcher : ICheatEngineDispatcher
	{
		public bool IsMainThread => true;

		public bool TryInvoke(Action callback, out CheatEngineFailure failure,
			CancellationToken cancellationToken = default)
		{
			ArgumentNullException.ThrowIfNull(callback);
			if (cancellationToken.IsCancellationRequested)
			{
				failure = new CheatEngineFailure(CheatEngineFailureKind.Cancelled, "Test.Dispatcher", "Cancelled.");
				return false;
			}

			callback();
			failure = default;
			return true;
		}

		public bool TryInvoke<T>(Func<T> callback, [MaybeNullWhen(false)] out T result,
			out CheatEngineFailure failure, CancellationToken cancellationToken = default)
		{
			ArgumentNullException.ThrowIfNull(callback);
			if (cancellationToken.IsCancellationRequested)
			{
				result = default;
				failure = new CheatEngineFailure(CheatEngineFailureKind.Cancelled, "Test.Dispatcher", "Cancelled.");
				return false;
			}

			result = callback();
			failure = default;
			return true;
		}

		public void Invoke(Action callback, CancellationToken cancellationToken = default)
		{
			if (!TryInvoke(callback, out CheatEngineFailure failure, cancellationToken))
			{
				failure.Throw(cancellationToken);
			}
		}

		public T Invoke<T>(Func<T> callback, CancellationToken cancellationToken = default)
		{
			if (TryInvoke(callback, out T? result, out CheatEngineFailure failure, cancellationToken))
			{
				return result;
			}

			failure.Throw(cancellationToken);
			return default!;
		}
	}
}
