using CheatEngine.Client.Core.Dispatching;
using CheatEngine.Client.Core.Domains;
using CheatEngine.Client.Core.Tests.TestSupport;
using CheatEngine.Client.Results;
using CheatEngine.Client.Scanning;
using CheatEngine.SDK.Engine.Inspection;
using CheatEngine.SDK.Engine.Scanning.Aob;
using CheatEngine.SDK.Engine.Scanning.Values;
using CheatEngine.SDK.Engine.Targets;
using CheatEngine.SDK.Engine.Values;

namespace CheatEngine.Client.Core.Tests.Domains;

/// <summary>
///     <see cref="IPatternScanner.ScanDetailed" /> reports, per route, the host's own outcome, why the scan ran on its route,
///     whether the target identity was verified, and the widened metrics (audit F06, F07, API-03).
/// </summary>
public sealed class PatternScannerDetailedOutcomeTests
{
	[Fact]
	[Trait("Qualification", "Q27")]
	public void AnUnscopedScanReportsTheGlobalRouteAndAVerifiedTarget()
	{
		PatternScanner scanner = CreateScanner(new FakeAobScanPort(new RecordingAobMatchList(["400000", "400010"])));

		PatternScanOutcome outcome = scanner.ScanDetailed(Request(null, 5), TestContext.Current.CancellationToken);

		Assert.True(outcome.IsSuccess);
		Assert.Equal(PatternScanHostOutcomeKind.Matches, outcome.HostOutcome);
		Assert.Equal(PatternScanRouteReason.UnscopedRequest, outcome.RouteReason);
		Assert.True(outcome.TargetIdentityVerified);
		PatternScanMetrics metrics = Assert.NotNull(outcome.Metrics);
		Assert.Equal(PatternScanScope.GlobalHostScan, metrics.Scope);
		Assert.Equal(2UL, metrics.HostResultCount);
		Assert.Equal(0UL, metrics.UnreadHostRowCount);
		Assert.True(metrics.InBoundsCountIsExact);
		Assert.Equal(0UL, metrics.BelowStartSkippedCount);
	}

	[Fact]
	[Trait("Qualification", "Q27")]
	public void ANilGlobalResultReportsItsHostOutcomeWithoutMetrics()
	{
		PatternScanner scanner = CreateScanner(new FakeAobScanPort
		{
			Outcome = AobHosts.Outcome(AobScanOutcomeKind.NoResult)
		});

		PatternScanOutcome outcome = scanner.ScanDetailed(Request(null, 1), TestContext.Current.CancellationToken);

		Assert.False(outcome.IsSuccess);
		Assert.Equal(CheatEngineFailureKind.IndeterminateHostResult, outcome.Failure!.Value.Kind);
		Assert.Equal(PatternScanHostOutcomeKind.NoResult, outcome.HostOutcome);
		Assert.Equal(PatternScanRouteReason.UnscopedRequest, outcome.RouteReason);
		Assert.False(outcome.TargetIdentityVerified);
		Assert.Null(outcome.Metrics);
	}

	[Fact]
	public void AGlobalScanOnAnUnqualifiedTargetIsNotVerified()
	{
		RecordingAobMatchList matches = new(["400000"]);
		PatternScanner scanner = CreateScanner(new FakeAobScanPort(matches)
		{
			Outcome = AobHosts.Outcome(AobScanOutcomeKind.Matches, AobHosts.FileAsProcess, AobHosts.FileAsProcess, 1)
		});

		PatternScanOutcome outcome = scanner.ScanDetailed(Request(null, 1), TestContext.Current.CancellationToken);

		Assert.True(outcome.IsSuccess);
		Assert.False(outcome.TargetIdentityVerified);
	}

	[Fact]
	public void AGlobalTargetChangeKeepsTheHostOutcomeAndReportsTheClientVerdict()
	{
		PatternScanner scanner = CreateScanner(new FakeAobScanPort(new RecordingAobMatchList(["400000"]))
		{
			Outcome = AobHosts.Outcome(AobScanOutcomeKind.Matches, AobHosts.Local(), AobHosts.Local(43, 2_000), 1)
		});

		PatternScanOutcome outcome = scanner.ScanDetailed(Request(null, 1), TestContext.Current.CancellationToken);

		Assert.Equal(CheatEngineFailureKind.TargetChanged, outcome.Failure!.Value.Kind);
		Assert.Equal(PatternScanHostOutcomeKind.Matches, outcome.HostOutcome);
		Assert.False(outcome.TargetIdentityVerified);
	}

	[Fact]
	[Trait("Qualification", "Q28")]
	public void ABoundedScanReportsItsRouteItsSkipsAndAVerifiedTarget()
	{
		AobBoundedHostResult bounded = AobHosts.Bounded(AobBoundedScanOutcomeKind.Matches) with
		{
			HostResultCount = 5,
			Written = 2,
			RowsRead = 5,
			BelowStartSkipped = 2,
			AtOrAfterStopSkipped = 1,
			CopyElapsed = TimeSpan.FromMilliseconds(1)
		};
		PatternScanner scanner = CreateScanner(QualifiedPort([0x4010, 0x4020], bounded));

		PatternScanOutcome outcome = scanner.ScanDetailed(Request(new ModuleName("game.exe"), 5),
			TestContext.Current.CancellationToken);

		Assert.True(outcome.IsSuccess);
		Assert.Equal(PatternScanHostOutcomeKind.Matches, outcome.HostOutcome);
		Assert.Equal(PatternScanRouteReason.ScopedRequestOnQualifiedTarget, outcome.RouteReason);
		Assert.True(outcome.TargetIdentityVerified);
		PatternScanMetrics metrics = Assert.NotNull(outcome.Metrics);
		Assert.Equal(PatternScanScope.HostBoundedRange, metrics.Scope);
		Assert.Equal(5UL, metrics.HostResultCount);
		Assert.Equal(5UL, metrics.ExaminedCount);
		Assert.Equal(3UL, metrics.FilteredOutCount);
		Assert.Equal(2UL, metrics.BelowStartSkippedCount);
		Assert.Equal(1UL, metrics.AtOrAfterStopSkippedCount);
		Assert.Equal(0UL, metrics.UnreadHostRowCount);
		Assert.True(metrics.InBoundsCountIsExact);
		Assert.True(metrics.HostScanElapsed > TimeSpan.Zero);
	}

	[Fact]
	[Trait("Qualification", "Q29")]
	public void ATruncatedBoundedScanWithUnreadRowsIsNotAnExactCount()
	{
		PatternScanner scanner = CreateScanner(QualifiedPort([0x4010, 0x4020, 0x4030]));

		PatternScanOutcome outcome = scanner.ScanDetailed(Request(new ModuleName("game.exe"), 1),
			TestContext.Current.CancellationToken);

		Assert.True(outcome.Result!.Value.IsTruncated);
		PatternScanMetrics metrics = Assert.NotNull(outcome.Metrics);
		Assert.Equal(1UL, metrics.UnreadHostRowCount);
		Assert.False(metrics.InBoundsCountIsExact);
	}

	[Fact]
	public void ABoundedFailureReportsItsHostOutcomeAndNoVerifiedTarget()
	{
		PatternScanner scanner = CreateScanner(QualifiedPort([],
			AobHosts.Bounded(AobBoundedScanOutcomeKind.HostReportedError) with
			{
				HostErrorText = "error"
			}));

		PatternScanOutcome outcome = scanner.ScanDetailed(Request(new ModuleName("game.exe"), 1),
			TestContext.Current.CancellationToken);

		Assert.False(outcome.IsSuccess);
		Assert.Equal(PatternScanHostOutcomeKind.HostReportedError, outcome.HostOutcome);
		Assert.Equal(PatternScanRouteReason.ScopedRequestOnQualifiedTarget, outcome.RouteReason);
		Assert.False(outcome.TargetIdentityVerified);
		Assert.False(Assert.NotNull(outcome.Metrics).InBoundsCountIsExact);
	}

	/// <summary>
	///     A fallback never reports a verified target, even after a session-creation failure on a qualified target whose
	///     global scan saw the same incarnation before and after: the reason says the identity was not qualified, and the
	///     flag agrees with it (plan L11, commit 2).
	/// </summary>
	[Theory]
	[Trait("Qualification", "Q28")]
	[InlineData(false)]
	[InlineData(true)]
	public void AFallbackReportsTheGlobalHostOutcomeAndTheUnqualifiedRoute(bool afterSessionFailure)
	{
		AobBoundedHostResult? bounded = afterSessionFailure
			? AobHosts.Bounded(AobBoundedScanOutcomeKind.SessionCreationFailed, false) with
			{
				CreationStatus = MemoryScanCreationStatus.NoScannerResult
			}
			: null;
		FakeAobScanPort port = new(new RecordingAobMatchList(["4010"]))
		{
			Modules = [Module()],
			Selection = afterSessionFailure ? AobHosts.Local() : AobHosts.Remote,
			BoundedResult = bounded
		};
		PatternScanner scanner = CreateScanner(port);

		PatternScanOutcome outcome = scanner.ScanDetailed(Request(new ModuleName("game.exe"), 1),
			TestContext.Current.CancellationToken);

		Assert.True(outcome.IsSuccess, outcome.Failure?.Message);
		Assert.Equal(PatternScanScope.GlobalHostScanWithManagedFilter, Assert.NotNull(outcome.Metrics).Scope);
		Assert.Equal(PatternScanRouteReason.TargetIdentityNotQualified, outcome.RouteReason);
		Assert.Equal(PatternScanHostOutcomeKind.Matches, outcome.HostOutcome);
		Assert.False(outcome.TargetIdentityVerified);
	}

	[Fact]
	public void ARefusalBeforeAnyRouteReportsNoRouteAndNoHostOutcome()
	{
		PatternScanner scanner = CreateScanner(new FakeAobScanPort
		{
			Modules = [Module()]
		});

		PatternScanOutcome outcome = scanner.ScanDetailed(Request(new ModuleName("other.exe"), 1),
			TestContext.Current.CancellationToken);

		Assert.Equal(CheatEngineFailureKind.NotFound, outcome.Failure!.Value.Kind);
		Assert.Equal(PatternScanRouteReason.Unknown, outcome.RouteReason);
		Assert.Equal(PatternScanHostOutcomeKind.Unknown, outcome.HostOutcome);
	}

	[Fact]
	[Trait("Qualification", "Q48")]
	public void EveryGlobalOutcomeKindHasItsHostOutcomeAndAnUnknownKindFailsClosed()
	{
		MappingTotality.AssertTotal<AobScanOutcomeKind>(
			static kind => AobScanMapping.ToHostOutcome(kind).ToString() == kind.ToString(),
			static kind => AobScanMapping.ToHostOutcome(kind) == PatternScanHostOutcomeKind.Unknown);
	}

	[Fact]
	[Trait("Qualification", "Q48")]
	public void EveryBoundedOutcomeKindHasItsHostOutcomeAndAnUnknownKindFailsClosed()
	{
		AobBoundedScanOutcomeKind[] withoutHostOutcome =
		[
			AobBoundedScanOutcomeKind.InvalidBounds, AobBoundedScanOutcomeKind.SessionCreationFailed,
			AobBoundedScanOutcomeKind.WaitTimedOut
		];

		MappingTotality.AssertTotal<AobBoundedScanOutcomeKind>(
			kind => withoutHostOutcome.Contains(kind)
				? AobScanMapping.ToHostOutcome(kind) == PatternScanHostOutcomeKind.Unknown
				: kind == AobBoundedScanOutcomeKind.RuntimeInvalidated
					// The Client names a replaced Lua runtime RuntimeChanged, like the failure kind.
					? AobScanMapping.ToHostOutcome(kind) == PatternScanHostOutcomeKind.RuntimeChanged
					: AobScanMapping.ToHostOutcome(kind).ToString() == kind.ToString(),
			static kind => AobScanMapping.ToHostOutcome(kind) == PatternScanHostOutcomeKind.Unknown);
	}

	private static FakeAobScanPort QualifiedPort(Address[] rows, AobBoundedHostResult? result = null)
	{
		return new FakeAobScanPort(new RecordingAobMatchList(["4010"]))
		{
			Modules = [Module()],
			Selection = AobHosts.Local(),
			BoundedRows = rows,
			BoundedResult = result
		};
	}

	private static ModuleInfo Module()
	{
		return new ModuleInfo("game.exe", new Address(0x4000), new MemorySize(0x100), true, "game.exe");
	}

	private static PatternScanner CreateScanner(FakeAobScanPort port)
	{
		return new PatternScanner(
			new SdkMainThreadDispatcher(InertCoreLifetime.Create(), new InlineMainThreadInvoker()), port);
	}

	private static AobScanRequest Request(ModuleName? module, int maximumResults)
	{
		return new AobScanRequest(new AobPattern("90 90"), maximumResults, module);
	}
}
