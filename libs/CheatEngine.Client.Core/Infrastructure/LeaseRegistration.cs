using CheatEngine.Client.Results;

namespace CheatEngine.Client.Core.Infrastructure;

/// <summary>Reports a new lease that could not be registered once the resource it would own was released at once.</summary>
/// <remarks>
///     A domain checks that the activation admits new work before Cheat Engine creates a resource, so a registration
///     refused afterwards means that the activation stopped or ended during the call, or that the target selection moved
///     on. Either way the resource is released in the same main-thread callback and no lease is published; what the
///     release left is in the message, so that an allocation that may remain can be recovered by other means.
/// </remarks>
internal static class LeaseRegistration
{
	/// <summary>Creates the failure of a refused registration, or throws the activation's lifecycle exception.</summary>
	/// <param name="lifetime">The activation that refused the registration.</param>
	/// <param name="operation">The public Client operation name.</param>
	/// <param name="registration">The exception of the refused registration.</param>
	/// <param name="released">The outcome of the release made because no lease was published.</param>
	/// <param name="resource">What was released, for the message, for example the address and size of an allocation.</param>
	/// <returns>
	///     While the activation still admits work, a <see cref="CheatEngineFailureKind.TargetChanged" /> failure (the
	///     target selection moved on): <see cref="CheatEngineHostEffect.Completed" /> when the release was confirmed,
	///     otherwise <see cref="CheatEngineHostEffect.CleanupUnconfirmed" />.
	/// </returns>
	/// <exception cref="CheatEngineActivationExpiredException">The activation ended; the message says what remains.</exception>
	/// <exception cref="CheatEngineClientLifecycleException">The activation is stopping; the message says what remains.</exception>
	internal static CheatEngineFailure Refused(CoreLifetime lifetime, string operation, Exception registration,
		LeaseReleaseOutcome released, string resource)
	{
		ArgumentNullException.ThrowIfNull(lifetime);
		string message = released.IsComplete
			? $"No lease could be registered for {resource}, which was released at once."
			: $"No lease could be registered for {resource}, and releasing it ended with {released.Kind}: it may " +
			  "remain in Cheat Engine or in the target.";
		if (!lifetime.IsActivationCurrent)
		{
			throw new CheatEngineActivationExpiredException(operation, message, registration);
		}

		if (!lifetime.IsCurrent)
		{
			throw new CheatEngineClientLifecycleException(operation, message, registration);
		}

		return new CheatEngineFailure(CheatEngineFailureKind.TargetChanged, operation,
			message + " The target selection changed before the lease was registered.", registration,
			released.IsComplete ? CheatEngineHostEffect.Completed : CheatEngineHostEffect.CleanupUnconfirmed);
	}
}
