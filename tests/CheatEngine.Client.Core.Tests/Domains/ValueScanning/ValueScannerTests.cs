using CheatEngine.Client.Core.Dispatching;
using CheatEngine.Client.Core.Domains;
using CheatEngine.Client.Core.Domains.ValueScanning;
using CheatEngine.Client.Core.Infrastructure;
using CheatEngine.Client.Core.Tests.TestSupport;
using CheatEngine.Client.Results;
using CheatEngine.Client.Scanning;
using CheatEngine.SDK.Engine.Enums;
using CheatEngine.SDK.Engine.Scanning.Values;
using CheatEngine.SDK.Engine.Targets;
using CheatEngine.SDK.Engine.Values;

namespace CheatEngine.Client.Core.Tests.Domains.ValueScanning;

/// <summary>
///     The value-scan battery of the audit (chapter 13) against a scripted SDK session: zero and many results, invalid
///     results, a malformed count, a creation failure after the first object, Cheat Engine closing during the wait,
///     cancellation before and after the start, a changed target, an error completion, next scans, a stale owner after
///     an external reset, a release while busy, a release that cannot reach Cheat Engine, and a process selected in Cheat
///     Engine's own window that keeps the sessions created for it.
/// </summary>
public sealed class ValueScannerTests : IDisposable
{
	private readonly ControlledCoreLifetimeContext _context = new();
	private readonly SdkMainThreadDispatcher _dispatcher;
	private readonly CoreLifetime _lifetime;
	private readonly FakeValueScanPort _port = new();
	private readonly ProcessClient _processes;
	private readonly ValueScanner _scanner;
	private readonly FakeSelectedTarget _target = new();

	public ValueScannerTests()
	{
		_lifetime = new CoreLifetime(_context);
		_dispatcher = new SdkMainThreadDispatcher(_lifetime, new InlineMainThreadInvoker());
		_processes = FakeSelectedTarget.CreateProcessClient(_dispatcher, _target);
		_scanner = new ValueScanner(_dispatcher, _processes, _port);
	}

	private FakeValueScanSessionHandle Handle => _port.Session;

	private static CancellationToken Token => TestContext.Current.CancellationToken;

	public void Dispose()
	{
		_context.Dispose();
	}

	[Fact]
	[Trait("Qualification", "Q25")]
	public void ACreatedSessionIsRegisteredAndReleasedBeforeTheActivationEnds()
	{
		IValueScanSession session = CreateSession();

		Assert.Equal(ValueScanSessionState.Created, session.State);
		Assert.Equal(ValueScanInvalidationKind.None, session.Invalidation);
		Assert.False(session.IsReleased);
		Assert.Null(session.LastReleaseOutcome);
		Assert.Equal(0, Handle.Destroys);

		_context.Stop();
		using (_lifetime.EnterCleanupScope())
		{
			_lifetime.DrainOwnedResourcesForDisable();
		}

		Assert.Equal(1, Handle.Destroys);
		Assert.True(session.IsReleased);
		Assert.Equal(ValueScanSessionState.Closed, session.State);
		Assert.Equal(new LeaseReleaseOutcome(LeaseReleaseKind.Released, CheatEngineHostEffect.Completed),
			session.LastReleaseOutcome);
	}

	[Theory]
	[Trait("Qualification", "Q25")]
	[InlineData(MemoryScanCreationStatus.TargetIdentityUnavailable, CheatEngineFailureKind.TargetIdentityUnavailable,
		CheatEngineHostEffect.NotStarted)]
	[InlineData(MemoryScanCreationStatus.GlobalUnavailable, CheatEngineFailureKind.CapabilityUnavailable,
		CheatEngineHostEffect.NotApplied)]
	[InlineData(MemoryScanCreationStatus.LuaFailure, CheatEngineFailureKind.LuaError, CheatEngineHostEffect.Unknown)]
	[InlineData(MemoryScanCreationStatus.NoScannerResult, CheatEngineFailureKind.OperationRejected,
		CheatEngineHostEffect.NotApplied)]
	[InlineData(MemoryScanCreationStatus.NoFoundListResult, CheatEngineFailureKind.OperationRejected,
		CheatEngineHostEffect.NotApplied)]
	[InlineData(MemoryScanCreationStatus.InvalidFoundListResult, CheatEngineFailureKind.InvalidHostResult,
		CheatEngineHostEffect.NotApplied)]
	[InlineData(MemoryScanCreationStatus.AliasedFoundList, CheatEngineFailureKind.InvalidHostResult,
		CheatEngineHostEffect.NotApplied)]
	[InlineData(MemoryScanCreationStatus.RollbackUnconfirmed, CheatEngineFailureKind.IndeterminateHostResult,
		CheatEngineHostEffect.CleanupUnconfirmed)]
	public void ARefusedCreationPublishesNoSessionAndKeepsTheSecondObjectRollbackFact(
		MemoryScanCreationStatus status, CheatEngineFailureKind kind, CheatEngineHostEffect hostEffect)
	{
		_port.Status = status;

		bool created = _scanner.TryCreateSession(out IValueScanSession? session, out CheatEngineFailure failure, Token);

		Assert.False(created);
		Assert.Null(session);
		Assert.Equal(kind, failure.Kind);
		Assert.Equal(hostEffect, failure.HostEffect);
		Assert.Equal(ValueScanner.CreateOperation, failure.Operation);
		Assert.Equal(1, _port.Creations);
	}

	[Theory]
	[InlineData(TargetReleaseStatus.Released, CheatEngineHostEffect.Unknown)]
	[InlineData(TargetReleaseStatus.UnconfirmedAfterInvocation, CheatEngineHostEffect.CleanupUnconfirmed)]
	public void ASessionNextToAFailedCreationIsReleasedAndAnIncompleteReleaseLeavesTheCleanupUnconfirmed(
		TargetReleaseStatus release, CheatEngineHostEffect hostEffect)
	{
		_port.Status = MemoryScanCreationStatus.LuaFailure;
		_port.PublishesSessionOnFailure = true;
		Handle.OwnerReleases = (release, release);

		bool created = _scanner.TryCreateSession(out IValueScanSession? session, out CheatEngineFailure failure, Token);

		Assert.False(created);
		Assert.Null(session);
		Assert.Equal(CheatEngineFailureKind.LuaError, failure.Kind);
		Assert.Equal(hostEffect, failure.HostEffect);
		Assert.Equal(1, Handle.Destroys);
	}

	[Fact]
	public void CancellationBeforeCreationReachesNoFactory()
	{
		bool created = _scanner.TryCreateSession(out IValueScanSession? session, out CheatEngineFailure failure,
			new CancellationToken(true));

		Assert.False(created);
		Assert.Null(session);
		Assert.Equal(CheatEngineFailureKind.Cancelled, failure.Kind);
		Assert.Equal(CheatEngineHostEffect.NotStarted, failure.HostEffect);
		Assert.Equal(0, _port.Creations);
		Assert.Throws<CheatEngineOperationCanceledException>(() =>
			_scanner.CreateSession(new CancellationToken(true)));
	}

	[Fact]
	public void CancellationObservedAfterCreationReleasesTheNewSessionAndPublishesNothing()
	{
		using CancellationTokenSource cancellation = new();
		_port.DuringCreate = cancellation.Cancel;

		bool created = _scanner.TryCreateSession(out IValueScanSession? session, out CheatEngineFailure failure,
			cancellation.Token);

		Assert.False(created);
		Assert.Null(session);
		Assert.Equal(CheatEngineFailureKind.Cancelled, failure.Kind);
		Assert.Equal(CheatEngineHostEffect.Completed, failure.HostEffect);
		Assert.Equal(1, Handle.Destroys);
	}

	[Fact]
	public void ACreationFaultIsTranslatedAndCheatEngineClosingDuringItExpiresTheActivation()
	{
		InvalidOperationException detached = new("The Cheat Engine plugin is not enabled.");
		_port.Fault = detached;

		bool created = _scanner.TryCreateSession(out _, out CheatEngineFailure failure, Token);
		_port.DuringCreate = () => _context.IsCurrent = false;
		CheatEngineActivationExpiredException expired = Assert.Throws<CheatEngineActivationExpiredException>(() =>
			_scanner.TryCreateSession(out _, out _, Token));

		Assert.False(created);
		Assert.Equal(CheatEngineFailureKind.OperationRejected, failure.Kind);
		Assert.Equal(CheatEngineHostEffect.Unknown, failure.HostEffect);
		Assert.Same(detached, failure.Exception);
		Assert.Equal(CheatEngineFailureKind.ActivationExpired, expired.Failure.Kind);
	}

	[Fact]
	[Trait("Qualification", "Q25")]
	public void AScanWithoutResultsReadsAsAnEmptyPage()
	{
		IValueScanSession session = CreateSession();

		session.FirstScan(ValueScanFirstRequest.Exact(ValueScanValue.FromInt32(100)), Token);
		ValueScanPage page = session.Read(new ValueScanReadRequest(0, 10), Token);

		Assert.Equal(ValueScanSessionState.ResultsReady, session.State);
		Assert.Equal(0UL, session.GetResultCount(Token));
		Assert.Equal(0UL, page.ResultCount);
		Assert.True(page.Matches.IsEmpty);
		Assert.False(page.HasMore);
		Assert.Equal(["StartFirstScan", "Wait", "CopyPage", "ResultCount"], Handle.Calls);
	}

	[Fact]
	[Trait("Qualification", "Q25")]
	public void ManyResultsAreReadInPagesBoundedByTheClientLimit()
	{
		for (int index = 0; index < 3000; index++)
		{
			Handle.Results.Add(new MemoryScanResult(new Address(0x10000 + ((ulong) index * 4)), "100"));
		}

		IValueScanSession session = CreateSession();
		session.FirstScan(ValueScanFirstRequest.Exact(ValueScanValue.FromInt32(100)), Token);

		ValueScanPage first = session.Read(new ValueScanReadRequest(0, 5000), Token);
		ValueScanPage last = session.Read(new ValueScanReadRequest(2990, 100), Token);

		Assert.Equal(3000UL, session.GetResultCount(Token));
		Assert.Equal(ScanResourceLimits.MaximumValueScanPage, first.Matches.Length);
		Assert.Equal(3000UL, first.ResultCount);
		Assert.True(first.HasMore);
		Assert.Equal(first.Matches.Length, first.NextStartIndex);
		Assert.Equal(new Address(0x10000), first.Matches[0].Address);
		Assert.Equal("100", first.Matches[0].ValueText);
		Assert.Equal(10, last.Matches.Length);
		Assert.Equal(2990, last.StartIndex);
		Assert.False(last.HasMore);
	}

	[Fact]
	[Trait("Qualification", "Q25")]
	public void AnInvalidResultOrAPageBeyondTheResultsPublishesNoPage()
	{
		Handle.Results.Add(new MemoryScanResult(new Address(0x1000), "1"));
		IValueScanSession session = CreateSession();
		session.FirstScan(ValueScanFirstRequest.Exact(ValueScanValue.FromInt32(1)), Token);

		bool beyond = session.TryRead(new ValueScanReadRequest(5, 1), out ValueScanPage beyondPage,
			out CheatEngineFailure beyondFailure, Token);
		Handle.CopyStatus = MemoryScanMaterializationStatus.InvalidResult;
		bool invalid = session.TryRead(new ValueScanReadRequest(0, 1), out ValueScanPage invalidPage,
			out CheatEngineFailure invalidFailure, Token);

		Assert.False(beyond);
		Assert.Equal(default, beyondPage);
		Assert.Equal(CheatEngineFailureKind.OperationRejected, beyondFailure.Kind);
		Assert.Equal(CheatEngineHostEffect.Completed, beyondFailure.HostEffect);
		Assert.False(invalid);
		Assert.Equal(default, invalidPage);
		Assert.Equal(CheatEngineFailureKind.InvalidHostResult, invalidFailure.Kind);
		Assert.Equal(ValueScanSession.ReadOperation, invalidFailure.Operation);
	}

	[Fact]
	[Trait("Qualification", "Q25")]
	public void AMalformedCountIsAnInvalidHostResult()
	{
		MemoryScanException malformed = ScanFaults.Scan(MemoryScanFailureKind.UnexpectedResult);
		IValueScanSession session = CreateSession();
		session.FirstScan(ValueScanFirstRequest.Exact(ValueScanValue.FromInt32(1)), Token);
		Handle.CountFault = malformed;

		bool counted = session.TryGetResultCount(out ulong count, out CheatEngineFailure failure, Token);

		Assert.False(counted);
		Assert.Equal(0UL, count);
		Assert.Equal(CheatEngineFailureKind.InvalidHostResult, failure.Kind);
		Assert.Equal(CheatEngineHostEffect.Completed, failure.HostEffect);
		Assert.Same(malformed, failure.Exception);
		Assert.Throws<CheatEngineOperationException>(() => session.GetResultCount(Token));
	}

	[Fact]
	[Trait("Qualification", "Q25")]
	public void CheatEngineClosingDuringTheWaitExpiresTheActivation()
	{
		IValueScanSession session = CreateSession();
		Handle.WaitFault = new InvalidOperationException("The Cheat Engine plugin is not enabled.");
		Handle.DuringWait = () => _context.IsCurrent = false;

		CheatEngineActivationExpiredException expired = Assert.Throws<CheatEngineActivationExpiredException>(() =>
			session.TryFirstScan(ValueScanFirstRequest.Exact(ValueScanValue.FromInt32(1)), out _, Token));

		Assert.Equal(CheatEngineFailureKind.ActivationExpired, expired.Failure.Kind);
		Assert.Equal(ValueScanSessionState.Invalidated, session.State);
	}

	[Fact]
	public void CancellationBeforeTheStartReachesNoScanAndLeavesTheSessionCreated()
	{
		IValueScanSession session = CreateSession();
		using CancellationTokenSource cancellation = new();
		Handle.BeforeStartCheck = cancellation.Cancel;

		bool precancelled = session.TryFirstScan(ValueScanFirstRequest.Exact(ValueScanValue.FromInt32(1)),
			out CheatEngineFailure precancelledFailure, new CancellationToken(true));
		bool raced = session.TryFirstScan(ValueScanFirstRequest.Exact(ValueScanValue.FromInt32(1)),
			out CheatEngineFailure racedFailure, cancellation.Token);

		Assert.False(precancelled);
		Assert.Equal(CheatEngineFailureKind.Cancelled, precancelledFailure.Kind);
		Assert.Equal(CheatEngineHostEffect.NotStarted, precancelledFailure.HostEffect);
		Assert.False(raced);
		Assert.Equal(CheatEngineFailureKind.Cancelled, racedFailure.Kind);
		Assert.Equal(CheatEngineHostEffect.NotStarted, racedFailure.HostEffect);
		Assert.Equal(["StartFirstScan"], Handle.Calls);
		Assert.Equal(ValueScanSessionState.Created, session.State);
	}

	[Fact]
	public void CancellationAfterTheStartLeavesTheScanRunningUntilTheRelease()
	{
		IValueScanSession session = CreateSession();
		using CancellationTokenSource cancellation = new();
		Handle.DuringStart = cancellation.Cancel;

		bool scanned = session.TryFirstScan(ValueScanFirstRequest.Exact(ValueScanValue.FromInt32(1)),
			out CheatEngineFailure failure, cancellation.Token);
		bool reset = session.TryReset(out CheatEngineFailure resetFailure, Token);
		LeaseReleaseOutcome released = session.Release();

		Assert.False(scanned);
		Assert.Equal(CheatEngineFailureKind.Cancelled, failure.Kind);
		Assert.Equal(CheatEngineHostEffect.Started, failure.HostEffect);
		Assert.False(reset);
		Assert.Equal(CheatEngineFailureKind.InvalidState, resetFailure.Kind);
		Assert.Equal(CheatEngineHostEffect.NotStarted, resetFailure.HostEffect);
		// The release asked Cheat Engine to stop the scan that may still run, and Cheat Engine confirmed the stop.
		Assert.Equal(new LeaseReleaseOutcome(LeaseReleaseKind.Released, CheatEngineHostEffect.Completed), released);
		Assert.Equal(1, Handle.StopRequests);
		Assert.Equal(1, Handle.Destroys);
	}

	[Theory]
	[InlineData(MemoryScanTerminationStatus.WaitTimedOut)]
	[InlineData(MemoryScanTerminationStatus.TerminateFailed)]
	[InlineData(MemoryScanTerminationStatus.WaitFailed)]
	public void AStopOfARunningScanThatIsNotConfirmedLeavesTheReleaseUnconfirmed(MemoryScanTerminationStatus stop)
	{
		IValueScanSession session = CreateSession();
		using CancellationTokenSource cancellation = new();
		Handle.DuringStart = cancellation.Cancel;
		Handle.StopStatus = stop;
		_ = session.TryFirstScan(ValueScanFirstRequest.Exact(ValueScanValue.FromInt32(1)), out _, cancellation.Token);

		LeaseReleaseOutcome released = session.Release();

		// Both objects were destroyed, but a scan thread may still run: never reported as a confirmed release.
		Assert.Equal(new LeaseReleaseOutcome(LeaseReleaseKind.CleanupUnconfirmed, CheatEngineHostEffect.Started),
			released);
		Assert.True(released.RequiresManualRecovery);
		Assert.True(session.IsReleased);
		Assert.Equal(1, Handle.StopRequests);
	}

	[Fact]
	public void ACompletedScanNeedsNoStopWhenItIsReleased()
	{
		IValueScanSession session = CreateSession();
		session.FirstScan(ValueScanFirstRequest.Exact(ValueScanValue.FromInt32(1)), Token);

		LeaseReleaseOutcome released = session.Release();

		Assert.Equal(new LeaseReleaseOutcome(LeaseReleaseKind.Released, CheatEngineHostEffect.Completed), released);
		Assert.Equal(0, Handle.StopRequests);
	}

	[Fact]
	[Trait("Qualification", "Q25")]
	public async Task AReleaseFromAWorkerThreadRunsOnCheatEngineMainThreadAsync()
	{
		using DedicatedThreadInvoker mainThread = new();
		SdkMainThreadDispatcher dispatcher = new(_lifetime, mainThread);
		FakeValueScanPort port = new();
		IValueScanSession session = new ValueScanner(dispatcher, FakeSelectedTarget.CreateProcessClient(dispatcher),
			port).CreateSession(Token);
		int? releaseThread = null;
		port.Session.OnRelease = () => releaseThread = Environment.CurrentManagedThreadId;

		LeaseReleaseOutcome released = await Task.Run(session.Release, Token);

		Assert.Equal(new LeaseReleaseOutcome(LeaseReleaseKind.Released, CheatEngineHostEffect.Completed), released);
		Assert.Equal(mainThread.ThreadId, releaseThread);
		Assert.Equal(1, port.Session.Destroys);
		Assert.True(session.IsReleased);
	}

	[Fact]
	public void CancellationDuringTheWaitCompletesTheScanAndPublishesNothing()
	{
		IValueScanSession session = CreateSession();
		using CancellationTokenSource cancellation = new();
		Handle.DuringWait = cancellation.Cancel;

		bool scanned = session.TryFirstScan(ValueScanFirstRequest.Exact(ValueScanValue.FromInt32(1)),
			out CheatEngineFailure failure, cancellation.Token);

		Assert.False(scanned);
		Assert.Equal(CheatEngineFailureKind.Cancelled, failure.Kind);
		Assert.Equal(CheatEngineHostEffect.Completed, failure.HostEffect);
		Assert.Equal(ValueScanSessionState.ResultsReady, session.State);
		Assert.Throws<CheatEngineOperationCanceledException>(() =>
			session.NextScan(ValueScanNextRequest.Changed(), new CancellationToken(true)));
	}

	[Fact]
	[Trait("Qualification", "Q26")]
	public void ATargetChangeRefusesTheScanAndReleasesTheSessionWithoutRetargeting()
	{
		IValueScanSession session = CreateSession();
		Handle.ContextFault = ScanFaults.Scan(MemoryScanFailureKind.TargetIdentityMismatch);
		Handle.OwnerReleases = (TargetReleaseStatus.RefusedTargetChanged, TargetReleaseStatus.RefusedTargetChanged);

		bool scanned = session.TryFirstScan(ValueScanFirstRequest.Exact(ValueScanValue.FromInt32(1)),
			out CheatEngineFailure failure, Token);
		ValueScanInvalidationKind invalidation = session.Invalidation;
		_ = _lifetime.TargetSelection.Advance("Processes.Attach");
		int callsAfterRelease = Handle.Calls.Count;
		bool after = session.TryReset(out CheatEngineFailure afterFailure, Token);

		Assert.False(scanned);
		Assert.Equal(CheatEngineFailureKind.TargetChanged, failure.Kind);
		Assert.Equal(CheatEngineHostEffect.NotStarted, failure.HostEffect);
		Assert.Equal(ValueScanInvalidationKind.TargetChanged, invalidation);
		Assert.True(session.IsReleased);
		Assert.Equal(ValueScanSessionState.Closed, session.State);
		Assert.Equal(LeaseReleaseKind.RefusedTargetChanged, session.LastReleaseOutcome?.Kind);
		Assert.True(session.LastReleaseOutcome?.RequiresManualRecovery);
		Assert.False(after);
		Assert.Equal(CheatEngineFailureKind.TargetChanged, afterFailure.Kind);
		Assert.Equal(CheatEngineHostEffect.NotStarted, afterFailure.HostEffect);
		Assert.Equal(callsAfterRelease, Handle.Calls.Count);
	}

	[Fact]
	[Trait("Qualification", "Q25")]
	public void AnErrorCompletionCarriesCheatEngineTextAndAResetRecoversTheSession()
	{
		MemoryScanException luaError = ScanFaults.Scan(MemoryScanFailureKind.LuaError);
		IValueScanSession session = CreateSession();
		Handle.WaitFault = luaError;
		Handle.HostErrorText = "Invalid value";
		Handle.HostErrorTextTruncated = true;

		bool scanned = session.TryFirstScan(ValueScanFirstRequest.Exact(ValueScanValue.FromInt32(1)),
			out CheatEngineFailure failure, Token);
		ValueScanSessionState failedState = session.State;
		ValueScanInvalidationKind invalidation = session.Invalidation;
		Handle.WaitFault = null;
		session.Reset(Token);
		ValueScanSessionState resetState = session.State;
		session.FirstScan(ValueScanFirstRequest.Exact(ValueScanValue.FromInt32(1)), Token);

		Assert.False(scanned);
		Assert.Equal(CheatEngineFailureKind.LuaError, failure.Kind);
		Assert.Equal(CheatEngineHostEffect.Started, failure.HostEffect);
		Assert.Same(luaError, failure.Exception);
		Assert.EndsWith(" Cheat Engine reported: Invalid value (truncated)", failure.Message, StringComparison.Ordinal);
		Assert.Equal(ValueScanSessionState.Invalidated, failedState);
		Assert.Equal(ValueScanInvalidationKind.HostCallFailed, invalidation);
		Assert.Equal(ValueScanSessionState.Created, resetState);
		Assert.Equal(ValueScanSessionState.ResultsReady, session.State);
		Assert.Equal(ValueScanInvalidationKind.None, session.Invalidation);
	}

	[Fact]
	[Trait("Qualification", "Q25")]
	public void NextScansCompareTheTypeOfTheFirstScan()
	{
		IValueScanSession session = CreateSession();
		session.FirstScan(ValueScanFirstRequest.UnknownInitialValue(ValueScanValueType.Integer32), Token);

		session.NextScan(ValueScanNextRequest.Exact(ValueScanValue.FromInt32(95)), Token);
		NextScanRequest exact = Handle.LastNextScan.GetValueOrDefault();
		bool mismatched = session.TryNextScan(ValueScanNextRequest.Exact(ValueScanValue.FromInt64(95)),
			out CheatEngineFailure mismatch, Token);
		int startsAfterMismatch = Handle.Calls.Count(static call => call == "StartNextScan");
		session.NextScan(ValueScanNextRequest.Decreased(), Token);

		Assert.Equal(ScanOption.UnknownValue, Handle.LastFirstScan.GetValueOrDefault().ScanOption);
		Assert.Equal(VariableType.Dword, Handle.LastFirstScan.GetValueOrDefault().VariableType);
		Assert.Equal(ScanOption.ExactValue, exact.ScanOption);
		Assert.Equal("95", exact.Input1);
		Assert.False(mismatched);
		Assert.Equal(CheatEngineFailureKind.OperationRejected, mismatch.Kind);
		Assert.Equal(CheatEngineHostEffect.NotStarted, mismatch.HostEffect);
		Assert.Equal(1, startsAfterMismatch);
		Assert.Equal(ScanOption.DecreasedValue, Handle.LastNextScan.GetValueOrDefault().ScanOption);
		Assert.Equal(ValueScanSessionState.ResultsReady, session.State);
	}

	[Fact]
	public void ANextScanBeforeAFirstScanIsRefusedByTheSessionState()
	{
		IValueScanSession session = CreateSession();

		bool scanned = session.TryNextScan(ValueScanNextRequest.Changed(), out CheatEngineFailure failure, Token);

		Assert.False(scanned);
		Assert.Equal(CheatEngineFailureKind.InvalidState, failure.Kind);
		Assert.Equal(CheatEngineHostEffect.NotStarted, failure.HostEffect);
		Assert.Equal(ValueScanSessionState.Created, session.State);
	}

	[Fact]
	[Trait("Qualification", "Q26")]
	public void AStaleOwnerAfterAnExternalResetIsRefusedAndItsReleaseIsReportedAtDeactivation()
	{
		IValueScanSession session = CreateSession();
		session.FirstScan(ValueScanFirstRequest.Exact(ValueScanValue.FromInt32(1)), Token);
		Handle.ContextFault = ScanFaults.Scan(MemoryScanFailureKind.RuntimeInvalidated);
		// CheatEngine.SDK consumes the owners of a session from an earlier runtime without any Cheat Engine call.
		Handle.OwnerReleases = (TargetReleaseStatus.NotInvoked, TargetReleaseStatus.NotInvoked);

		bool counted = session.TryGetResultCount(out _, out CheatEngineFailure countFailure, Token);
		bool scanned = session.TryNextScan(ValueScanNextRequest.Changed(), out CheatEngineFailure scanFailure, Token);
		LeaseReleaseOutcome released = session.Release();
		bool afterRelease = session.TryGetResultCount(out _, out CheatEngineFailure afterFailure, Token);

		Assert.False(counted);
		Assert.Equal(CheatEngineFailureKind.RuntimeChanged, countFailure.Kind);
		Assert.Equal(CheatEngineHostEffect.NotStarted, countFailure.HostEffect);
		Assert.Equal(ValueScanInvalidationKind.RuntimeChanged, session.Invalidation);
		// The invalidated session refuses a next scan by its state, before any Cheat Engine call.
		Assert.False(scanned);
		Assert.Equal(CheatEngineFailureKind.InvalidState, scanFailure.Kind);
		Assert.Equal(CheatEngineHostEffect.NotStarted, scanFailure.HostEffect);
		// NotInvoked stays retryable: the lease stays registered and the deactivation report carries it.
		Assert.Equal(LeaseReleaseKind.CleanupUnavailable, released.Kind);
		Assert.False(session.IsReleased);
		Assert.Equal(ValueScanSessionState.Closed, session.State);
		Assert.False(afterRelease);
		Assert.Equal(CheatEngineFailureKind.InvalidState, afterFailure.Kind);
	}

	[Fact]
	[Trait("Qualification", "Q25")]
	public void AReleaseRequestedDuringTheWaitRunsOnceAfterItAndTheSessionReportsItsFinalOutcome()
	{
		IValueScanSession session = CreateSession();
		LeaseReleaseOutcome? duringWait = null;
		Handle.DuringWait = () => duringWait = session.Release();

		bool scanned = session.TryFirstScan(ValueScanFirstRequest.Exact(ValueScanValue.FromInt32(1)),
			out CheatEngineFailure failure, Token);
		bool releasedAfterScan = session.IsReleased;
		LeaseReleaseOutcome final = session.Release();

		Assert.Equal(LeaseReleaseKind.Unknown, duringWait?.Kind);
		Assert.True(duringWait?.IsRetryable);
		Assert.False(scanned);
		Assert.Equal(CheatEngineFailureKind.InvalidState, failure.Kind);
		Assert.Equal(CheatEngineHostEffect.Started, failure.HostEffect);
		Assert.IsType<ObjectDisposedException>(failure.Exception);
		Assert.False(releasedAfterScan);
		Assert.Equal(new LeaseReleaseOutcome(LeaseReleaseKind.Released, CheatEngineHostEffect.Completed), final);
		Assert.True(session.IsReleased);
		Assert.Equal(1, Handle.Destroys);
	}

	[Fact]
	[Trait("Qualification", "Q25")]
	public void ACallFromWorkCheatEngineRunsDuringTheWaitIsRefusedWithoutACheatEngineCall()
	{
		IValueScanSession session = CreateSession();
		CheatEngineFailure reentrant = default;
		bool reentrantSucceeded = true;
		Handle.DuringWait = () =>
			reentrantSucceeded = session.TryGetResultCount(out _, out reentrant, Token);

		session.FirstScan(ValueScanFirstRequest.Exact(ValueScanValue.FromInt32(1)), Token);

		Assert.False(reentrantSucceeded);
		Assert.Equal(CheatEngineFailureKind.InvalidState, reentrant.Kind);
		Assert.Equal(CheatEngineHostEffect.NotStarted, reentrant.HostEffect);
		Assert.Equal(ValueScanSessionState.ResultsReady, session.State);
	}

	[Fact]
	[Trait("Qualification", "Q43")]
	public void AReleaseThatCannotReachCheatEngineKeepsTheLeaseForTheDeactivationReport()
	{
		IValueScanSession session = CreateSession();
		Handle.OwnerReleases = (TargetReleaseStatus.NotInvoked, TargetReleaseStatus.NotInvoked);

		_context.Stop();
		LeaseReleaseOutcome refused = session.Release();
		int destroysWhileStopping = Handle.Destroys;
		AggregateException? report = null;
		using (_lifetime.EnterCleanupScope())
		{
			try
			{
				_lifetime.DrainOwnedResourcesForDisable();
			}
			catch (CheatEngineOperationException single)
			{
				report = new AggregateException(single);
			}
			catch (AggregateException several)
			{
				report = several;
			}
		}

		Assert.Equal(new LeaseReleaseOutcome(LeaseReleaseKind.CleanupUnavailable, CheatEngineHostEffect.NotStarted),
			refused);
		Assert.Equal(0, destroysWhileStopping);
		Assert.Equal(1, Handle.Destroys);
		Assert.NotNull(report);
		CheatEngineOperationException reported = Assert.IsType<CheatEngineOperationException>(
			Assert.Single(report.InnerExceptions));
		Assert.Equal(ValueScanSession.ReleaseOperation, reported.Failure.Operation);
		Assert.Equal(CheatEngineFailureKind.CapabilityUnavailable, reported.Failure.Kind);
	}

	[Fact]
	public void DisposeNeverThrowsWhenTheReleaseFaults()
	{
		IValueScanSession session = CreateSession();
		Handle.ReleaseFault = new InvalidOperationException("destroy raised");

		session.Dispose();

		Assert.True(session.IsReleased);
		Assert.Equal(new LeaseReleaseOutcome(LeaseReleaseKind.CleanupUnconfirmed, CheatEngineHostEffect.Unknown),
			session.LastReleaseOutcome);
	}

	[Fact]
	public void AReleasedSessionRefusesEveryOperationWithoutACheatEngineCall()
	{
		IValueScanSession session = CreateSession();
		session.Dispose();
		int calls = Handle.Calls.Count;

		bool scanned = session.TryFirstScan(ValueScanFirstRequest.Exact(ValueScanValue.FromInt32(1)),
			out CheatEngineFailure failure, Token);

		Assert.False(scanned);
		Assert.Equal(CheatEngineFailureKind.InvalidState, failure.Kind);
		Assert.Equal(CheatEngineHostEffect.NotStarted, failure.HostEffect);
		Assert.Equal(calls, Handle.Calls.Count);
		Assert.Equal(session.LastReleaseOutcome, session.Release());
		Assert.Throws<CheatEngineInvalidStateException>(() => session.Reset(Token));
	}

	[Fact]
	public void InvalidRequestsAreRefusedBeforeDispatch()
	{
		IValueScanSession session = CreateSession();

		bool first = session.TryFirstScan(default, out CheatEngineFailure firstFailure, Token);
		bool wide = session.TryRead(new ValueScanReadRequest((long) int.MaxValue + 1, 1), out _,
			out CheatEngineFailure wideFailure, Token);
		bool empty = session.TryRead(default, out _, out CheatEngineFailure emptyFailure, Token);

		Assert.False(first);
		Assert.Equal(CheatEngineFailureKind.OperationRejected, firstFailure.Kind);
		Assert.Equal(CheatEngineHostEffect.NotStarted, firstFailure.HostEffect);
		Assert.False(wide);
		Assert.Equal(CheatEngineFailureKind.ResultLimitExceeded, wideFailure.Kind);
		Assert.Equal(CheatEngineHostEffect.NotStarted, wideFailure.HostEffect);
		Assert.False(empty);
		Assert.Equal(CheatEngineFailureKind.OperationRejected, emptyFailure.Kind);
		Assert.Empty(Handle.Calls);
		Assert.Throws<CheatEngineOperationException>(() => session.FirstScan(default, Token));
	}

	[Fact]
	public void AScanFaultBeforeCheatEngineIsCalledCarriesNoHostText()
	{
		IValueScanSession session = CreateSession();
		session.FirstScan(ValueScanFirstRequest.Exact(ValueScanValue.FromInt32(1)), Token);
		Handle.HostErrorText = "stale text";

		bool scanned = session.TryFirstScan(ValueScanFirstRequest.Exact(ValueScanValue.FromInt32(1)),
			out CheatEngineFailure failure, Token);

		Assert.False(scanned);
		Assert.Equal(CheatEngineFailureKind.InvalidState, failure.Kind);
		Assert.Equal(CheatEngineHostEffect.NotStarted, failure.HostEffect);
		Assert.DoesNotContain("stale text", failure.Message, StringComparison.Ordinal);
	}

	[Fact]
	[Trait("Qualification", "Q26")]
	public void ASessionForAProcessSelectedInCheatEngineStaysWithThatProcess()
	{
		long firstEpoch = _processes.GetCurrent(Token).SelectionEpoch;
		FakeValueScanSessionHandle first = Handle;
		first.OwnerReleases = (TargetReleaseStatus.RefusedTargetChanged, TargetReleaseStatus.RefusedTargetChanged);
		IValueScanSession forFirst = CreateSession();
		// Cheat Engine's own window selects another process: no Client call observes it.
		_target.Select(FakeSelectedTarget.OtherProcessIncarnation);
		FakeValueScanSessionHandle second = new()
		{
			TargetIncarnation = FakeSelectedTarget.OtherProcessIncarnation
		};
		_port.Session = second;

		IValueScanSession forSecond = CreateSession();
		int secondDestroysAfterCreation = second.Destroys;
		long secondEpoch = _processes.GetCurrent(Token).SelectionEpoch;
		forSecond.FirstScan(ValueScanFirstRequest.Exact(ValueScanValue.FromInt32(1)), Token);

		// The first session's process is no longer selected: it was released when the second session was bound.
		Assert.True(forFirst.IsReleased);
		Assert.Equal(LeaseReleaseKind.RefusedTargetChanged, forFirst.LastReleaseOutcome?.Kind);
		Assert.Equal(firstEpoch, forFirst.SelectionEpoch);
		// The second session belongs to the selection the next observation finds, which releases nothing.
		Assert.True(secondEpoch > firstEpoch);
		Assert.Equal(secondEpoch, forSecond.SelectionEpoch);
		Assert.Equal(0, secondDestroysAfterCreation);
		Assert.Equal(0, second.Destroys);
		Assert.False(forSecond.IsReleased);
		Assert.Equal(ValueScanSessionState.ResultsReady, forSecond.State);
	}

	[Fact]
	public void ASessionInTheObservedProcessKeepsTheObservedSelectionEpoch()
	{
		long observedEpoch = _processes.GetCurrent(Token).SelectionEpoch;

		IValueScanSession session = CreateSession();
		long laterEpoch = _processes.GetCurrent(Token).SelectionEpoch;

		Assert.Equal(observedEpoch, session.SelectionEpoch);
		Assert.Equal(observedEpoch, laterEpoch);
		Assert.False(session.IsReleased);
		Assert.Equal(0, Handle.Destroys);
	}

	[Fact]
	[Trait("Qualification", "Q43")]
	public void ASessionDuringTheDeactivationCleanupIsRefusedBeforeCheatEngineCreatesIt()
	{
		_context.Stop();
		using (_lifetime.EnterCleanupScope())
		{
			Assert.Throws<CheatEngineInvalidStateException>(() => _scanner.TryCreateSession(out _, out _, Token));
		}

		Assert.Equal(0, _port.Creations);
	}

	[Fact]
	[Trait("Qualification", "Q43")]
	public void AnActivationStoppingDuringTheCreationReleasesTheSessionAndThrows()
	{
		_port.DuringCreate = _context.Stop;

		CheatEngineInvalidStateException stopping = Assert.Throws<CheatEngineInvalidStateException>(() =>
			_scanner.TryCreateSession(out _, out _, Token));

		Assert.Contains("the new scan session, which was released at once", stopping.Message, StringComparison.Ordinal);
		Assert.Equal(ValueScanner.CreateOperation, stopping.Failure.Operation);
		Assert.Equal(1, Handle.Destroys);
	}

	private IValueScanSession CreateSession()
	{
		Assert.True(_scanner.TryCreateSession(out IValueScanSession? session, out CheatEngineFailure failure, Token),
			failure.ToString());
		return session;
	}
}
