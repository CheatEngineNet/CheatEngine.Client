namespace CheatEngine.Client.Events;

/// <summary>Specifies the bounded-admission policy for a copied Client event stream.</summary>
public enum EventStreamOverflowPolicy
{
	/// <summary>Evicts the oldest buffered event before admitting the newest event.</summary>
	DropOldest,

	/// <summary>Rejects the newly raised event while retaining the buffered events.</summary>
	DropNewest,

	/// <summary>Terminates the subscription when its bounded buffer cannot admit an event.</summary>
	FailSubscription
}
