using System.Diagnostics;

using CheatEngine.Client.Core.Infrastructure;
using CheatEngine.Client.Results;

namespace CheatEngine.Client.Core.Domains.Events;

/// <summary>Creates the uniform capability-gated result used by high-level domains awaiting their live-host gate.</summary>
internal static class UnavailableCapabilityFailure
{
	internal static CheatEngineFailure Create(string capabilityName, string operation,
		CancellationToken cancellationToken)
	{
		return Create(null, capabilityName, operation, cancellationToken);
	}

	internal static CheatEngineFailure Create(CoreLifetime? lifetime, string capabilityName, string operation,
		CancellationToken cancellationToken)
	{
		ArgumentException.ThrowIfNullOrWhiteSpace(capabilityName);
		ArgumentException.ThrowIfNullOrWhiteSpace(operation);
		lifetime?.ThrowIfInactive(operation);

		// The lifetime is checked first so an expired activation is never reported as cancelled or unavailable. Neither
		// result starts Cheat Engine work.
		return cancellationToken.IsCancellationRequested
			? CoreFailureFactory.Cancelled(operation)
			: new CheatEngineFailure(CheatEngineFailureKind.CapabilityUnavailable, operation,
				$"{capabilityName} is unavailable because this Client package currently composes an unavailable adapter. " +
				"Promotion also requires its ownership, thread-affinity, cleanup, disable, re-enable, and target-change " +
				"behavior to pass the required Cheat Engine 7.7 x64 live gate.", null, CheatEngineHostEffect.NotStarted);
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
