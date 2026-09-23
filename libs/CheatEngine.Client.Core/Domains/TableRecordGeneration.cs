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
	internal long Generation => Volatile.Read(ref _generation);

	/// <summary>Records the identifier of a copied record as observed in the current table load.</summary>
	internal void Observe(MemoryRecordSnapshot record)
	{
		lock (_gate)
		{
			ObserveCore(record.Id);
		}
	}

	/// <summary>Records every identifier of a copied table snapshot.</summary>
	internal void Observe(AddressTableSnapshot table)
	{
		if (table.Records.IsDefault)
		{
			return;
		}

		lock (_gate)
		{
			foreach (MemoryRecordSnapshot record in table.Records)
			{
				ObserveCore(record.Id);
			}
		}
	}

	/// <summary>Records every identifier of a copied hierarchy.</summary>
	internal void Observe(MemoryRecordHierarchySnapshot hierarchy)
	{
		lock (_gate)
		{
			ObserveHierarchy(hierarchy);
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

	/// <summary>Advances the generation after a trusted load reached Cheat Engine; returns the new generation.</summary>
	internal long Advance()
	{
		lock (_gate)
		{
			_stale.UnionWith(_current);
			_current.Clear();
			return Interlocked.Increment(ref _generation);
		}
	}

	private void ObserveHierarchy(MemoryRecordHierarchySnapshot hierarchy)
	{
		ObserveCore(hierarchy.Record.Id);
		foreach (MemoryRecordHierarchySnapshot child in hierarchy.Children)
		{
			ObserveHierarchy(child);
		}
	}

	private void ObserveCore(MemoryRecordId id)
	{
		_current.Add(id);
		_stale.Remove(id);
	}
}
