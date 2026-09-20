using System.Diagnostics;

using CheatEngine.Client.Results;
using CheatEngine.Client.Scanning;

namespace CheatEngine.Client.Core.Domains;

/// <summary>
///     Keeps the public capability honest until the owned MemScan/FoundList lifecycle passes the required live CE 7.7
///     gate.
/// </summary>
internal sealed class UnavailableValueScanner : IValueScanner
{
	// This is a deliberate product gate, not a transient host-capability probe.  SDK 1.0.0 exposes the state machine
	// only through MemoryScanSession.Adopt(Owned<MemScan>, Owned<FoundList>), while Owned<T> has an internal
	// constructor and the Lua-global generator cannot marshal CEObject results.  Bypassing that with reflection or a
	// hand-rolled destroy owner would make an unverified CE ownership assumption part of the Client contract.
	private const string OwnershipGateMessage =
		"Value scans are disabled: CheatEngine.SDK 1.0.0 has no public ownership factory for createMemScan or " +
		"createFoundList, and its generated Lua globals cannot return CEObject handles. Enablement requires the " +
		"Cheat Engine 7.7 ownership and reactivation live gate.";

	public bool TryCreateSession(out IValueScanSession? session, out CheatEngineFailure failure,
		CancellationToken cancellationToken = default)
	{
		session = null;
		failure = cancellationToken.IsCancellationRequested
			? new CheatEngineFailure(CheatEngineFailureKind.Cancelled, "Scans.CreateSession",
				"The operation was cancelled before Cheat Engine work began.")
			: new CheatEngineFailure(CheatEngineFailureKind.CapabilityUnavailable, "Scans.CreateSession",
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
