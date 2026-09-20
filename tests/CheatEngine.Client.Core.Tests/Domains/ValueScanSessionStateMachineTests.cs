using CheatEngine.Client.Core.Domains;
using CheatEngine.Client.Scanning;

namespace CheatEngine.Client.Core.Tests.Domains;

public sealed class ValueScanSessionStateMachineTests
{
	[Fact]
	public void FirstAndNextScansFollowTheManagedHappyPath()
	{
		ValueScanSessionStateMachine state = new();

		state.BeginFirstScan();
		Assert.Equal(ValueScanSessionState.Scanning, state.State);

		state.CompleteScan();
		Assert.Equal(ValueScanSessionState.ResultsReady, state.State);

		state.BeginNextScan();
		Assert.Equal(ValueScanSessionState.Scanning, state.State);

		state.CompleteScan();
		Assert.Equal(ValueScanSessionState.ResultsReady, state.State);
	}

	[Fact]
	public void FailedStartedOperationInvalidatesUntilReset()
	{
		ValueScanSessionStateMachine state = new();
		state.BeginFirstScan();

		state.Invalidate();

		Assert.Equal(ValueScanSessionState.Invalidated, state.State);
		Assert.Throws<InvalidOperationException>(state.BeginFirstScan);
		Assert.Throws<InvalidOperationException>(state.BeginNextScan);

		state.Reset();
		Assert.Equal(ValueScanSessionState.Created, state.State);
	}

	[Fact]
	public void ResetDoesNotInterruptScanning()
	{
		ValueScanSessionStateMachine state = new();
		state.BeginFirstScan();

		Assert.Throws<InvalidOperationException>(state.Reset);
		Assert.Equal(ValueScanSessionState.Scanning, state.State);
	}

	[Fact]
	public void DisposalIsIdempotentAndTerminal()
	{
		ValueScanSessionStateMachine state = new();

		state.MarkDisposed();
		state.MarkDisposed();

		Assert.Equal(ValueScanSessionState.Disposed, state.State);
		Assert.Throws<InvalidOperationException>(state.Reset);
		Assert.Throws<InvalidOperationException>(state.BeginFirstScan);
	}
}
