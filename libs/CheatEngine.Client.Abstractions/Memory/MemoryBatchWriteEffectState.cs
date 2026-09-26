namespace CheatEngine.Client.Memory;

/// <summary>Describes the observable target-memory effect of a sequential primitive batch write.</summary>
public enum MemoryBatchWriteEffectState
{
	/// <summary>
	///     The Client could not establish whether the target observed any write. It is also the value of an unassigned
	///     state, so an uninitialized outcome never reads as an established effect.
	/// </summary>
	Unknown = 0,

	/// <summary>No write is known to have reached the target.</summary>
	NotStarted = 1,

	/// <summary>A strict prefix completed before a later write failed.</summary>
	Partial = 2,

	/// <summary>Every requested write completed.</summary>
	Completed = 3
}
