using System.Diagnostics;

using CheatEngine.Client.Results;
using CheatEngine.Client.Core.Infrastructure;

namespace CheatEngine.Client.Core.Domains.Events;

/// <summary>Creates the uniform capability-gated result used by high-level domains awaiting their live-host gate.</summary>
internal static class UnavailableCapabilityFailure
{
	internal static CheatEngineFailure Create(string capabilityName, string operation,
		CancellationToken cancellationToken)
		=> Create(null, capabilityName, operation, cancellationToken);

	internal static CheatEngineFailure Create(CoreLifetime? lifetime, string capabilityName, string operation,
		CancellationToken cancellationToken)
	{
		ArgumentException.ThrowIfNullOrWhiteSpace(capabilityName);
		ArgumentException.ThrowIfNullOrWhiteSpace(operation);
		lifetime?.ThrowIfInactive(operation);

		return cancellationToken.IsCancellationRequested
			? new CheatEngineFailure(CheatEngineFailureKind.Cancelled, operation,
				"The operation was cancelled before Cheat Engine work began.")
			: new CheatEngineFailure(CheatEngineFailureKind.CapabilityUnavailable, operation,
				$"{capabilityName} is unavailable until its ownership, thread-affinity, cleanup, disable, re-enable, " +
				"and target-change behavior pass the required Cheat Engine 7.7 x64 live gate.");
	}

	internal static T Throw<T>(CheatEngineFailure failure)
	{
		failure.Throw();
		throw new UnreachableException();
	}

	internal static void Throw(CheatEngineFailure failure)
	{
		failure.Throw();
	}
}
