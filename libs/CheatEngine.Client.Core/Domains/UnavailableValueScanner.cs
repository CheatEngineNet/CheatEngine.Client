using System.Diagnostics;

using CheatEngine.Client.Core.Infrastructure;
using CheatEngine.Client.Results;
using CheatEngine.Client.Runtime;
using CheatEngine.Client.Scanning;

namespace CheatEngine.Client.Core.Domains;

/// <summary>
///     Keeps the public capability honest until the owned MemScan/FoundList lifecycle passes the required live CE 7.7
///     gate.
/// </summary>
internal sealed class UnavailableValueScanner : IValueScanner
{
	// This is a deliberate product gate, not a transient host-capability probe: this build composes no operational
	// value-scan adapter, so no MemScan or FoundList is ever created. An adapter must own both objects through the
	// consumed SDK's owners (never reflection or a hand-rolled destroy owner, which would make an unverified CE
	// ownership assumption part of the Client contract) and pass the live gate before this refusal is lifted.
	private const string OwnershipGateMessage =
		"Value scans are unavailable: this Client build composes no operational value-scan adapter, so it creates no " +
		"MemScan or FoundList. Enablement requires the Cheat Engine 7.7 ownership and reactivation live gate.";

	private readonly CoreLifetime? _lifetime;

	internal UnavailableValueScanner(CoreLifetime? lifetime = null)
	{
		_lifetime = lifetime;
	}

	public bool TryCreateSession(out IValueScanSession? session, out CheatEngineFailure failure,
		CancellationToken cancellationToken = default)
	{
		session = null;
		_lifetime?.ThrowIfInactive("Scans.CreateSession");
		if (cancellationToken.IsCancellationRequested)
		{
			failure = new CheatEngineFailure(CheatEngineFailureKind.Cancelled, "Scans.CreateSession",
				"The operation was cancelled before Cheat Engine work began.");
			return false;
		}

		_lifetime?.Diagnostics.CapabilityRefused(ClientCapabilityId.ValueScanning.Value, "Scans.CreateSession",
			ClientCapabilityEvidenceReasonCode.Implementation, ClientCapabilityEvidenceState.Missing);
		failure = new CheatEngineFailure(CheatEngineFailureKind.CapabilityUnavailable, "Scans.CreateSession",
			OwnershipGateMessage);
		return false;
	}

	public IValueScanSession CreateSession(CancellationToken cancellationToken = default)
	{
		_ = TryCreateSession(out _, out CheatEngineFailure failure, cancellationToken);
		return ThrowFailure<IValueScanSession>(failure);
	}

	private static T ThrowFailure<T>(CheatEngineFailure failure)
	{
		failure.Throw();
		throw new UnreachableException();
	}
}
