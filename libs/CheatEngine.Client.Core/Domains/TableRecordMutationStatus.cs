namespace CheatEngine.Client.Core.Domains;

/// <summary>Outcome of one protected address-list record mutation.</summary>
internal enum TableRecordMutationStatus
{
	Success,
	RecordNotFound,
	ParentNotFound,
	InvalidRelationship,
	HostRejected,

	/// <summary>Cheat Engine's Address List is unavailable, so no record was reached (a capability condition, ADR-08).</summary>
	AddressListUnavailable
}
