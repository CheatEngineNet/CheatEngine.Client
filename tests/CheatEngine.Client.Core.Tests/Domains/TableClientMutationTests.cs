using System.Diagnostics.CodeAnalysis;

using CheatEngine.Client.Core.Domains;
using CheatEngine.Client.Core.Infrastructure;
using CheatEngine.Client.Dispatching;
using CheatEngine.Client.Results;
using CheatEngine.Client.Tables;
using CheatEngine.SDK.Engine.AddressList;
using CheatEngine.SDK.Engine.Enums;

namespace CheatEngine.Client.Core.Tests.Domains;

public sealed class TableClientMutationTests
{
	[Fact]
	public void TryDeleteDispatchesTheRequestedRecordAndReturnsSuccess()
	{
		FakeRecordMutationPort mutations = new()
		{
			DeleteStatus = TableRecordMutationStatus.Success
		};
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
			DeleteStatus = TableRecordMutationStatus.RecordNotFound
		});

		bool succeeded =
			client.TryDelete(new MemoryRecordId(41), out CheatEngineFailure failure,
				TestContext.Current.CancellationToken);

		Assert.False(succeeded);
		Assert.Equal(CheatEngineFailureKind.NotFound, failure.Kind);
		Assert.Equal("Tables.Delete", failure.Operation);
	}

	[Fact]
	[Trait("Qualification", "Q34")]
	public void DeleteThrowsTheClassifiedHostFailure()
	{
		TableClient client = CreateClient(new FakeRecordMutationPort
		{
			DeleteStatus = TableRecordMutationStatus.HostRejected
		});

		CheatEngineOperationException exception = Assert.Throws<CheatEngineOperationException>(() =>
			client.Delete(new MemoryRecordId(41), TestContext.Current.CancellationToken));

		Assert.Equal(CheatEngineFailureKind.InvalidHostResult, exception.Failure.Kind);
		Assert.Equal("Tables.Delete", exception.Failure.Operation);
	}

	[Fact]
	public void TrySetParentPassesBothIdsAndReturnsTheCopiedPostMutationSnapshot()
	{
		MemoryRecordSnapshot expected = Snapshot(41, "Ammo");
		FakeRecordMutationPort mutations = new()
		{
			SetParentStatus = TableRecordMutationStatus.Success,
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
			SetParentStatus = TableRecordMutationStatus.Success,
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
		Assert.Equal("Tables.SetParent", failure.Operation);
		Assert.Equal(
			"The requested parent relationship is invalid: it is self-referential, cyclic, or exceeds the " +
			"supported hierarchy depth.",
			failure.Message);
		Assert.Equal(0, mutations.SetParentCallCount);
	}

	[Fact]
	[Trait("Qualification", "Q34")]
	public void TrySetParentMapsAHostRejectedMutationToTheExactHostFailure()
	{
		FakeRecordMutationPort mutations = new()
		{
			SetParentStatus = TableRecordMutationStatus.HostRejected
		};
		TableClient client = CreateClient(mutations);

		bool succeeded = client.TrySetParent(new MemoryRecordId(41), new MemoryRecordId(12),
			out MemoryRecordSnapshot record, out CheatEngineFailure failure, TestContext.Current.CancellationToken);

		Assert.False(succeeded);
		Assert.Equal(default, record);
		Assert.Equal(new MemoryRecordId(41), mutations.LastChildId);
		Assert.Equal(new MemoryRecordId(12), mutations.LastParentId);
		Assert.Equal(CheatEngineFailureKind.InvalidHostResult, failure.Kind);
		Assert.Equal("Tables.SetParent", failure.Operation);
		Assert.Equal("Cheat Engine did not return the expected Address List contract.", failure.Message);
	}

	[Fact]
	[Trait("Qualification", "Q34")]
	public void SetParentThrowsAClassifiedMissingParentFailure()
	{
		TableClient client = CreateClient(new FakeRecordMutationPort
		{
			SetParentStatus = TableRecordMutationStatus.ParentNotFound
		});

		CheatEngineOperationException exception = Assert.Throws<CheatEngineOperationException>(() =>
			client.SetParent(new MemoryRecordId(41), new MemoryRecordId(12), TestContext.Current.CancellationToken));

		Assert.Equal(CheatEngineFailureKind.NotFound, exception.Failure.Kind);
		Assert.Equal("Tables.SetParent", exception.Failure.Operation);
		Assert.Contains("parent", exception.Failure.Message, StringComparison.OrdinalIgnoreCase);
	}

	[Fact]
	public void ParentRelationshipGuardPermitsARootTerminatedCandidateChain()
	{
		TableRecordMutationStatus status = TableParentRelationshipGuard.Validate(new MemoryRecordId(41),
			new MemoryRecordId(12), 4,
			static id => id == new MemoryRecordId(12) ? ParentChainStep.Root : ParentChainStep.HostRejected);

		Assert.Equal(TableRecordMutationStatus.Success, status);
	}

	[Fact]
	public void ParentRelationshipGuardPropagatesARejectedHostRead()
	{
		TableRecordMutationStatus status = TableParentRelationshipGuard.Validate(new MemoryRecordId(41),
			new MemoryRecordId(12), 4, static _ => ParentChainStep.HostRejected);

		Assert.Equal(TableRecordMutationStatus.HostRejected, status);
	}

	[Fact]
	[Trait("Qualification", "Q34")]
	public void ParentRelationshipGuardRejectsAnIndirectCycleBackToTheChild()
	{
		Dictionary<MemoryRecordId, ParentChainStep> links = new()
		{
			[new MemoryRecordId(12)] = ParentChainStep.Parent(new MemoryRecordId(24)),
			[new MemoryRecordId(24)] = ParentChainStep.Parent(new MemoryRecordId(41))
		};

		TableRecordMutationStatus status = TableParentRelationshipGuard.Validate(new MemoryRecordId(41),
			new MemoryRecordId(12), 4,
			id => links[id]);

		Assert.Equal(TableRecordMutationStatus.InvalidRelationship, status);
	}

	[Fact]
	[Trait("Qualification", "Q34")]
	public void ParentRelationshipGuardRejectsAnExistingParentLoop()
	{
		Dictionary<MemoryRecordId, ParentChainStep> links = new()
		{
			[new MemoryRecordId(12)] = ParentChainStep.Parent(new MemoryRecordId(24)),
			[new MemoryRecordId(24)] = ParentChainStep.Parent(new MemoryRecordId(12))
		};

		TableRecordMutationStatus status = TableParentRelationshipGuard.Validate(new MemoryRecordId(41),
			new MemoryRecordId(12), 4,
			id => links[id]);

		Assert.Equal(TableRecordMutationStatus.InvalidRelationship, status);
	}

	[Fact]
	[Trait("Qualification", "Q34")]
	public void ParentRelationshipGuardRejectsAChainThatExceedsItsBound()
	{
		TableRecordMutationStatus status = TableParentRelationshipGuard.Validate(new MemoryRecordId(41),
			new MemoryRecordId(12), 2,
			static id => id switch
			{
				{ Value: 12 } => ParentChainStep.Parent(new MemoryRecordId(24)),
				{ Value: 24 } => ParentChainStep.Parent(new MemoryRecordId(36)),
				_ => ParentChainStep.Root
			});

		Assert.Equal(TableRecordMutationStatus.InvalidRelationship, status);
	}

	[Fact]
	[Trait("Qualification", "Q43")]
	public void FailedCreateWithUnconfirmedRollbackReportsCleanupUnconfirmed()
	{
		InvalidOperationException rollbackFault = new("destroy faulted");
		FakeRecordMutationPort mutations = new()
		{
			Creation = new TableRecordCreation(TableRecordMutationStatus.ParentNotFound,
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
			Creation = new TableRecordCreation(TableRecordMutationStatus.HostRejected, TableRecordRollback.Confirmed)
		});

		bool succeeded = client.TryCreate(Definition(), out _, out CheatEngineFailure failure,
			TestContext.Current.CancellationToken);

		Assert.False(succeeded);
		Assert.Equal(CheatEngineFailureKind.InvalidHostResult, failure.Kind);
		Assert.Equal(CheatEngineHostEffect.Completed, failure.HostEffect);
	}

	[Fact]
	public void FailedCreateKeepsBothTheCreationAndTheRollbackFaults()
	{
		InvalidOperationException creationFault = new("initialization faulted");
		InvalidOperationException rollbackFault = new("destroy faulted");
		TableClient client = CreateClient(new FakeRecordMutationPort
		{
			Creation = new TableRecordCreation(TableRecordMutationStatus.HostRejected, TableRecordRollback.Unconfirmed,
				creationFault, rollbackFault)
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

	[Fact]
	[Trait("Qualification", "Q35")]
	public void TrySetActiveReportsRefusedWhenCheatEngineLeavesTheRecordInactive()
	{
		FakeRecordActivation activation = new(active: false)
		{
			RefuseChange = true
		};
		TableClient client = CreateClient(new FakeRecordMutationPort { ActivationRecord = activation });

		bool succeeded = client.TrySetActive(new MemoryRecordId(41), true, out MemoryRecordSnapshot record,
			out CheatEngineFailure failure, TestContext.Current.CancellationToken);

		Assert.False(succeeded);
		Assert.Equal(CheatEngineFailureKind.OperationRejected, failure.Kind);
		Assert.Equal(CheatEngineHostEffect.Started, failure.HostEffect);
		Assert.Equal("Tables.SetActive", failure.Operation);
		Assert.Contains("left the memory record inactive", failure.Message, StringComparison.Ordinal);
		Assert.Contains("partial script effects may persist", failure.Message, StringComparison.Ordinal);
		Assert.Equal(new MemoryRecordId(41), record.Id);
		Assert.False(record.IsActive);
		Assert.Equal(1, activation.WriteCount);
	}

	[Fact]
	[Trait("Qualification", "Q35")]
	public void TrySetActiveReportsRefusedWhenCheatEngineLeavesTheRecordActive()
	{
		FakeRecordActivation activation = new(active: true)
		{
			RefuseChange = true
		};
		TableClient client = CreateClient(new FakeRecordMutationPort { ActivationRecord = activation });

		bool succeeded = client.TrySetActive(new MemoryRecordId(41), false, out MemoryRecordSnapshot record,
			out CheatEngineFailure failure, TestContext.Current.CancellationToken);

		Assert.False(succeeded);
		Assert.Equal(CheatEngineFailureKind.OperationRejected, failure.Kind);
		Assert.Contains("left the memory record active", failure.Message, StringComparison.Ordinal);
		Assert.True(record.IsActive);
		Assert.Equal(1, activation.WriteCount);
	}

	[Fact]
	[Trait("Qualification", "Q35")]
	public void TrySetActiveInTheRequestedStateDoesNotCallTheSetter()
	{
		FakeRecordActivation activation = new(active: true);
		TableClient client = CreateClient(new FakeRecordMutationPort { ActivationRecord = activation });

		bool succeeded = client.TrySetActive(new MemoryRecordId(41), true, out MemoryRecordSnapshot record,
			out CheatEngineFailure failure, TestContext.Current.CancellationToken);

		Assert.True(succeeded);
		Assert.Equal(default, failure);
		Assert.True(record.IsActive);
		Assert.Equal(0, activation.WriteCount);
	}

	[Fact]
	[Trait("Qualification", "Q35")]
	public void TrySetActiveAppliesTheRequestedStateAndReturnsThePostChangeSnapshot()
	{
		FakeRecordActivation activation = new(active: false);
		TableClient client = CreateClient(new FakeRecordMutationPort { ActivationRecord = activation });

		bool succeeded = client.TrySetActive(new MemoryRecordId(41), true, out MemoryRecordSnapshot record,
			out CheatEngineFailure failure, TestContext.Current.CancellationToken);

		Assert.True(succeeded);
		Assert.Equal(default, failure);
		Assert.True(record.IsActive);
		Assert.Equal(1, activation.WriteCount);
	}

	[Fact]
	[Trait("Qualification", "Q35")]
	public void TrySetActiveReportsPendingForAnAsynchronousRecordStillProcessing()
	{
		FakeRecordActivation activation = new(active: false)
		{
			ProcessingAfterWrite = true,
			RefuseChange = true
		};
		TableClient client = CreateClient(new FakeRecordMutationPort { ActivationRecord = activation });

		bool succeeded = client.TrySetActive(new MemoryRecordId(41), true, out MemoryRecordSnapshot record,
			out CheatEngineFailure failure, TestContext.Current.CancellationToken);

		Assert.False(succeeded);
		Assert.Equal(CheatEngineFailureKind.IndeterminateHostResult, failure.Kind);
		Assert.Equal(CheatEngineHostEffect.Started, failure.HostEffect);
		Assert.Contains("asynchronously", failure.Message, StringComparison.Ordinal);
		Assert.Equal(new MemoryRecordId(41), record.Id);
	}

	[Fact]
	public void TrySetActiveReportsAnUnknownEffectWhenThePostChangeReadFails()
	{
		// A14-42: the native effect may have happened, but its result could not be copied.
		FakeRecordActivation activation = new(active: false)
		{
			FailReadAfterWrite = true
		};
		TableClient client = CreateClient(new FakeRecordMutationPort { ActivationRecord = activation });

		bool succeeded = client.TrySetActive(new MemoryRecordId(41), true, out MemoryRecordSnapshot record,
			out CheatEngineFailure failure, TestContext.Current.CancellationToken);

		Assert.False(succeeded);
		Assert.Equal(CheatEngineFailureKind.InvalidHostResult, failure.Kind);
		Assert.Equal(CheatEngineHostEffect.Unknown, failure.HostEffect);
		Assert.Equal(default, record);
		Assert.Equal(1, activation.WriteCount);
	}

	[Fact]
	public void TrySetActiveNeverRetriesAfterARefusal()
	{
		// An OnActivationFailure handler that asks for a retry already loops inside Cheat Engine's single setter call.
		FakeRecordActivation activation = new(active: false)
		{
			RefuseChange = true
		};
		TableClient client = CreateClient(new FakeRecordMutationPort { ActivationRecord = activation });

		Assert.False(client.TrySetActive(new MemoryRecordId(41), true, out _, out _,
			TestContext.Current.CancellationToken));

		Assert.Equal(1, activation.WriteCount);
		Assert.Equal(2, activation.ActiveReadCount);
		Assert.Equal(1, activation.ProcessingReadCount);
	}

	[Fact]
	[Trait("Qualification", "Q34")]
	public void TrySetActiveReportsNotFoundForAnUnknownRecordWithoutTouchingTheHost()
	{
		FakeRecordMutationPort mutations = new()
		{
			Activation = TableActivationObservation.Of(TableActivationStatus.RecordNotFound)
		};
		TableClient client = CreateClient(mutations);

		bool succeeded = client.TrySetActive(new MemoryRecordId(41), true, out MemoryRecordSnapshot record,
			out CheatEngineFailure failure, TestContext.Current.CancellationToken);

		Assert.False(succeeded);
		Assert.Equal(default, record);
		Assert.Equal(CheatEngineFailureKind.NotFound, failure.Kind);
		Assert.Equal(CheatEngineHostEffect.NotStarted, failure.HostEffect);
		Assert.Equal(1, mutations.SetActiveCallCount);
	}

	[Fact]
	[Trait("Qualification", "Q34")]
	public void DeletingTheSameRecordTwiceReportsNotFoundTheSecondTime()
	{
		// A14-38: the second delete finds no record, so destroy is never reached twice.
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

		bool succeeded = client.TrySelect(new MemoryRecordId(41), out MemoryRecordSnapshot record,
			out CheatEngineFailure failure, TestContext.Current.CancellationToken);

		Assert.True(succeeded);
		Assert.Equal(default, failure);
		Assert.Equal(new MemoryRecordId(41), record.Id);
		Assert.Equal([new MemoryRecordId(41)], mutations.SelectedIds);
	}

	[Fact]
	public void MutationsReportCapabilityUnavailableWhenTheAddressListIsUnavailable()
	{
		// ADR-08: an unavailable Address List is the same capability condition for every mutation as for TrySetActive
		// and the lookups, never an unexpected host result; no record was reached.
		FakeRecordMutationPort mutations = new()
		{
			SelectStatus = TableRecordMutationStatus.AddressListUnavailable,
			DeleteStatus = TableRecordMutationStatus.AddressListUnavailable,
			SetParentStatus = TableRecordMutationStatus.AddressListUnavailable,
			Creation = new TableRecordCreation(TableRecordMutationStatus.AddressListUnavailable,
				TableRecordRollback.NotRequired)
		};
		TableClient client = CreateClient(mutations);
		CancellationToken token = TestContext.Current.CancellationToken;

		bool selected = client.TrySelect(new MemoryRecordId(41), out _, out CheatEngineFailure selectFailure, token);
		bool deleted = client.TryDelete(new MemoryRecordId(41), out CheatEngineFailure deleteFailure, token);
		bool reparented = client.TrySetParent(new MemoryRecordId(41), new MemoryRecordId(7), out _,
			out CheatEngineFailure parentFailure, token);
		bool created = client.TryCreate(Definition(), out _, out CheatEngineFailure createFailure, token);

		Assert.False(selected || deleted || reparented || created);
		foreach (CheatEngineFailure failure in (CheatEngineFailure[]) [selectFailure, deleteFailure, parentFailure,
					 createFailure])
		{
			Assert.Equal(CheatEngineFailureKind.CapabilityUnavailable, failure.Kind);
			Assert.Equal(CheatEngineHostEffect.NotStarted, failure.HostEffect);
			Assert.Equal("Cheat Engine's Address List capability is unavailable.", failure.Message);
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

	private static MemoryRecordSnapshot Snapshot(int id, string description)
	{
		return new MemoryRecordSnapshot(
			new MemoryRecordId(id),
			0,
			new MemoryRecordContentSnapshot(description, "game.exe+24", "50", VariableType.Dword),
			new MemoryRecordStateSnapshot(null));
	}

	private sealed class FakeRecordMutationPort : ITableRecordMutationPort
	{
		internal TableRecordMutationStatus DeleteStatus
		{
			get;
			init;
		} = TableRecordMutationStatus.Success;

		internal TableRecordMutationStatus SetParentStatus
		{
			get;
			init;
		} = TableRecordMutationStatus.Success;

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

		internal int CreateCallCount
		{
			get;
			private set;
		}

		internal MemoryRecordId LastDeletedId
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

		internal TableActivationObservation? Activation
		{
			get;
			init;
		}

		internal FakeRecordActivation? ActivationRecord
		{
			get;
			init;
		}

		internal int SetActiveCallCount
		{
			get;
			private set;
		}

		internal bool TrackDeletedRecords
		{
			get;
			init;
		}

		internal int DestroyCount
		{
			get;
			private set;
		}

		internal MemoryRecordSnapshot SelectRecord
		{
			get;
			init;
		}

		internal TableRecordMutationStatus SelectStatus
		{
			get;
			init;
		} = TableRecordMutationStatus.Success;

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
			record = Creation.Status == TableRecordMutationStatus.Success ? CreatedRecord : default;
			return Creation;
		}

		public TableRecordMutationStatus TryDelete(MemoryRecordId id)
		{
			LastDeletedId = id;
			if (!TrackDeletedRecords)
			{
				return DeleteStatus;
			}

			if (!DeletedIds.Add(id))
			{
				return TableRecordMutationStatus.RecordNotFound;
			}

			DestroyCount++;
			return TableRecordMutationStatus.Success;
		}

		public TableActivationObservation TrySetActive(MemoryRecordId id, bool requested)
		{
			SetActiveCallCount++;
			return ActivationRecord is { } record
				? TableRecordActivation.Apply(record, requested)
				: Activation ?? TableActivationObservation.Of(TableActivationStatus.Applied);
		}

		public TableRecordMutationStatus TrySelect(MemoryRecordId id, out MemoryRecordSnapshot record)
		{
			SelectedIds.Add(id);
			record = SelectStatus == TableRecordMutationStatus.Success ? SelectRecord : default;
			return SelectStatus;
		}

		public TableRecordMutationStatus TrySetParent(MemoryRecordId childId, MemoryRecordId? parentId,
			out MemoryRecordSnapshot record)
		{
			SetParentCallCount++;
			LastChildId = childId;
			LastParentId = parentId;
			record = SetParentRecord;
			return SetParentStatus;
		}
	}

	/// <summary>A record whose activation state, asynchronous processing and failures are scripted.</summary>
	private sealed class FakeRecordActivation(bool active) : IRecordActivationAccess
	{
		private bool _active = active;
		private bool _written;

		internal bool RefuseChange
		{
			get;
			init;
		}

		internal bool ProcessingAfterWrite
		{
			get;
			init;
		}

		internal bool FailReadAfterWrite
		{
			get;
			init;
		}

		internal int WriteCount
		{
			get;
			private set;
		}

		internal int ActiveReadCount
		{
			get;
			private set;
		}

		internal int ProcessingReadCount
		{
			get;
			private set;
		}

		public bool TryReadActive(out bool value)
		{
			ActiveReadCount++;
			value = _active;
			return !(_written && FailReadAfterWrite);
		}

		public bool TryWriteActive(bool value)
		{
			WriteCount++;
			_written = true;
			if (!RefuseChange)
			{
				_active = value;
			}

			return true;
		}

		public bool TryReadAsyncProcessing(out bool processing)
		{
			ProcessingReadCount++;
			processing = _written && ProcessingAfterWrite;
			return true;
		}

		public bool TrySnapshot(out MemoryRecordSnapshot snapshot)
		{
			snapshot = new MemoryRecordSnapshot(
				new MemoryRecordId(41),
				0,
				new MemoryRecordContentSnapshot("Health", "game.exe+24", "100", VariableType.Dword),
				new MemoryRecordStateSnapshot(null, _active, 0));
			return true;
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
				failure.Throw();
			}
		}

		public T Invoke<T>(Func<T> callback, CancellationToken cancellationToken = default)
		{
			if (TryInvoke(callback, out T? result, out CheatEngineFailure failure, cancellationToken))
			{
				return result;
			}

			failure.Throw();
			return default!;
		}
	}
}
