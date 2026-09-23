using CheatEngine.Client.Tables;
using CheatEngine.SDK.Engine.AddressList;

namespace CheatEngine.Client.Core.Domains;

/// <summary>
///     Binds the memory-record identifiers that one Client activation handed out to the table load they were observed in
///     (audit ch.14, A14-01, A14-05, A14-29, Q34).
/// </summary>
/// <remarks>
///     <para>
///         Every copied record snapshot marks its identifier as current. A trusted table load that reached Cheat Engine
///         (merge or replace, whatever its result: Cheat Engine may already have cleared the table) advances the
///         generation and turns every current identifier stale, because Cheat Engine does not promise that an identifier
///         survives a load. A stale identifier becomes current again when a later snapshot observes it.
///     </para>
///     <para>
///         Ordering: <see cref="Advance" /> runs inside the dispatched load callback, and every dispatched lookup or
///         mutation reads <see cref="Generation" /> on Cheat Engine's main thread when it copies its snapshot. The main
///         thread runs dispatched callbacks one at a time, so the generation a snapshot carries is the table load it was
///         copied from, whatever the order in which the calling workers resume afterwards. <see cref="Observe(MemoryRecordSnapshot, long)" />
///         therefore judges each snapshot by the generation it was copied in: a snapshot copied before a later load
///         hands out stale identifiers, never current ones.
///     </para>
///     <para>
///         An identifier this activation never handed out is not judged (unknown provenance). A reload by the user, a
///         script or another plugin is not observed, and a successful delete does not make an identifier stale (a second
///         delete reports not found).
///     </para>
/// </remarks>
internal sealed class TableRecordGeneration
{
	private readonly HashSet<MemoryRecordId> _current = [];
	private readonly Lock _gate = new();
	private readonly HashSet<MemoryRecordId> _stale = [];
	private long _generation;

	/// <summary>Gets the number of trusted table loads of this activation that reached Cheat Engine.</summary>
	/// <remarks>Read it on Cheat Engine's main thread, inside the dispatched callback that copies a snapshot.</remarks>
	internal long Generation => Volatile.Read(ref _generation);

	/// <summary>Records the identifier of a record copied in <paramref name="observedGeneration" />.</summary>
	internal void Observe(MemoryRecordSnapshot record, long observedGeneration)
	{
		lock (_gate)
		{
			ObserveCore(record.Id, observedGeneration);
		}
	}

	/// <summary>Records every identifier of a table snapshot copied in <paramref name="observedGeneration" />.</summary>
	internal void Observe(AddressTableSnapshot table, long observedGeneration)
	{
		if (table.Records.IsDefault)
		{
			return;
		}

		lock (_gate)
		{
			foreach (MemoryRecordSnapshot record in table.Records)
			{
				ObserveCore(record.Id, observedGeneration);
			}
		}
	}

	/// <summary>Records every identifier of a hierarchy copied in <paramref name="observedGeneration" />.</summary>
	internal void Observe(MemoryRecordHierarchySnapshot hierarchy, long observedGeneration)
	{
		lock (_gate)
		{
			ObserveHierarchy(hierarchy, observedGeneration);
		}
	}

	/// <summary>Gets whether the identifier was handed out before the last trusted load and not observed since.</summary>
	internal bool IsStale(MemoryRecordId id)
	{
		lock (_gate)
		{
			return _stale.Contains(id);
		}
	}

	/// <summary>
	///     Advances the generation after a trusted load reached Cheat Engine; returns the new generation. Call it inside the
	///     dispatched load callback, so no snapshot copied after the load can carry the previous generation.
	/// </summary>
	internal long Advance()
	{
		lock (_gate)
		{
			_stale.UnionWith(_current);
			_current.Clear();
			return Interlocked.Increment(ref _generation);
		}
	}

	private void ObserveHierarchy(MemoryRecordHierarchySnapshot hierarchy, long observedGeneration)
	{
		ObserveCore(hierarchy.Record.Id, observedGeneration);
		foreach (MemoryRecordHierarchySnapshot child in hierarchy.Children)
		{
			ObserveHierarchy(child, observedGeneration);
		}
	}

	private void ObserveCore(MemoryRecordId id, long observedGeneration)
	{
		if (observedGeneration == _generation)
		{
			_current.Add(id);
			_stale.Remove(id);
			return;
		}

		// The snapshot was copied before a trusted load that has since advanced the generation: it hands out an
		// identifier of an earlier table state. It stays refused unless a snapshot of the current load observed it.
		if (!_current.Contains(id))
		{
			_stale.Add(id);
		}
	}
}
