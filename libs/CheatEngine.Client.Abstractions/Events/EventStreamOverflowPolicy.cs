namespace CheatEngine.Client.Events;

/// <summary>Specifies the bounded-admission policy for a copied Client event stream.</summary>
public enum EventStreamOverflowPolicy
{
	/// <summary>Evicts the oldest buffered observation before admitting the newest observation.</summary>
	DropOldest,

	/// <summary>Rejects the newly raised observation while retaining the buffered observations.</summary>
	DropNewest,

	/// <summary>
	///     Terminates observation delivery when its bounded buffer cannot admit an observation. This does not select a
	///     native callback disposition.
	/// </summary>
	FailSubscription
}
