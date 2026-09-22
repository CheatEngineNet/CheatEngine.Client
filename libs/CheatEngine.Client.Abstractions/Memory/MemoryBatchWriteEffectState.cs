namespace CheatEngine.Client.Memory;

/// <summary>Describes the observable target-memory effect of a sequential primitive batch write.</summary>
public enum MemoryBatchWriteEffectState
{
	/// <summary>No write is known to have reached the target.</summary>
	NotStarted = 0,

	/// <summary>A strict prefix completed before a later write failed.</summary>
	Partial = 1,

	/// <summary>Every requested write completed.</summary>
	Complete = 2,

	/// <summary>The dispatcher could not establish whether the target observed any write.</summary>
	Unknown = 3
}
