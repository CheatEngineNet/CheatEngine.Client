using System.Diagnostics;

using CheatEngine.Client.Core.Infrastructure;
using CheatEngine.Client.Results;
using CheatEngine.Client.Runtime;

namespace CheatEngine.Client.Core.Domains.Events;

/// <summary>Creates the uniform capability-gated result used by high-level domains awaiting their live-host gate.</summary>
internal static class UnavailableCapabilityFailure
{
	/// <summary>Creates the refusal of a contract-only capability and reports it to the activation diagnostics.</summary>
	/// <param name="lifetime">The owning activation, when the caller has one.</param>
	/// <param name="capability">The Client capability whose implementation gate is missing.</param>
	/// <param name="capabilityName">The human-readable capability name used in the failure message.</param>
	/// <param name="operation">The public Client operation name.</param>
	/// <param name="cancellationToken">Observed before the capability gate.</param>
	internal static CheatEngineFailure Create(CoreLifetime? lifetime, ClientCapabilityId capability,
		string capabilityName, string operation, CancellationToken cancellationToken)
	{
		ArgumentException.ThrowIfNullOrWhiteSpace(capabilityName);
		ArgumentException.ThrowIfNullOrWhiteSpace(operation);
		lifetime?.ThrowIfInactive(operation);

		// The lifetime is checked first so an expired activation is never reported as cancelled or unavailable. Neither
		// result starts Cheat Engine work.
		if (cancellationToken.IsCancellationRequested)
		{
			return CoreFailureFactory.Cancelled(operation);
		}

		lifetime?.Diagnostics.CapabilityRefused(capability.Value, operation,
			ClientCapabilityEvidenceReasonCode.Implementation, ClientCapabilityEvidenceState.Missing);
		return new CheatEngineFailure(CheatEngineFailureKind.CapabilityUnavailable, operation,
			$"{capabilityName} is unavailable because this Client package currently composes an unavailable adapter. " +
			"Promotion also requires its ownership, thread-affinity, cleanup, disable, re-enable, and target-change " +
			"behavior to pass the required Cheat Engine 7.7 x64 live gate.", null, CheatEngineHostEffect.NotStarted);
	}

	internal static T Throw<T>(CheatEngineFailure failure, CancellationToken cancellationToken)
	{
		failure.Throw(cancellationToken);
		throw new UnreachableException();
	}
}
