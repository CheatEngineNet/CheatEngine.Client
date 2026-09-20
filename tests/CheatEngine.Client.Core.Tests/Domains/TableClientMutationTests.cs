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

		public TableRecordMutationStatus TryDelete(MemoryRecordId id)
		{
			LastDeletedId = id;
			return DeleteStatus;
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
			if (TryInvoke(callback, out T? result, out var failure, cancellationToken))
			{
				return result;
			}

			failure.Throw();
			return default!;
		}
	}
}
