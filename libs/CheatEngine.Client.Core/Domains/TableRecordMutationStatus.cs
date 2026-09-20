namespace CheatEngine.Client.Core.Domains;

/// <summary>Outcome of one protected address-list record mutation.</summary>
internal enum TableRecordMutationStatus
{
	Success,
	RecordNotFound,
	ParentNotFound,
	InvalidRelationship,
	HostRejected
}
