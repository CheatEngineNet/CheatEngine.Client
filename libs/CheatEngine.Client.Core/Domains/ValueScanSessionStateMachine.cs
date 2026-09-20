using CheatEngine.Client.Scanning;

namespace CheatEngine.Client.Core.Domains;

/// <summary>
///     Keeps the client-visible value-scan lifecycle conservative and independent of SDK object handles.
/// </summary>
/// <remarks>
///     This state machine is deliberately usable without a live Cheat Engine host. The eventual CE-backed session must
///     call
///     <see cref="BeginFirstScan" /> or <see cref="BeginNextScan" /> immediately before work begins, then either
///     <see cref="CompleteScan" /> after wait-and-initialize succeeds or <see cref="Invalidate" /> when a protected Lua
///     call
///     leaves the safe continuation state unknown.
/// </remarks>
internal sealed class ValueScanSessionStateMachine
{
	public ValueScanSessionState State
	{
		get;
		private set;
	} = ValueScanSessionState.Created;

	internal void BeginFirstScan()
	{
		Require(ValueScanSessionState.Created, "a first scan");
		State = ValueScanSessionState.Scanning;
	}

	internal void BeginNextScan()
	{
		Require(ValueScanSessionState.ResultsReady, "a subsequent scan");
		State = ValueScanSessionState.Scanning;
	}

	internal void CompleteScan()
	{
		Require(ValueScanSessionState.Scanning, "scan completion");
		State = ValueScanSessionState.ResultsReady;
	}

	internal void Invalidate()
	{
		if (State != ValueScanSessionState.Disposed)
		{
			State = ValueScanSessionState.Invalidated;
		}
	}

	internal void Reset()
	{
		if (State == ValueScanSessionState.Created)
		{
			return;
		}

		if (State is ValueScanSessionState.Scanning or ValueScanSessionState.Disposed)
		{
			ThrowInvalidTransition("a reset");
		}

		State = ValueScanSessionState.Created;
	}

	internal void MarkDisposed()
	{
		State = ValueScanSessionState.Disposed;
	}

	private void Require(ValueScanSessionState expected, string operation)
	{
		if (State == expected)
		{
			return;
		}

		ThrowInvalidTransition(operation);
	}

	private void ThrowInvalidTransition(string operation)
	{
		throw new InvalidOperationException(
			$"The value-scan session is {State} and cannot begin {operation}.");
	}
}
