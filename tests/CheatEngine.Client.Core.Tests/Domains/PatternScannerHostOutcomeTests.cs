using CheatEngine.Client.Core.Dispatching;
using CheatEngine.Client.Core.Domains;
using CheatEngine.Client.Core.Infrastructure;
using CheatEngine.Client.Core.Tests.TestSupport;
using CheatEngine.Client.Results;
using CheatEngine.Client.Scanning;
using CheatEngine.SDK.Engine.Scanning.Aob;
using CheatEngine.SDK.Engine.Targets;
using CheatEngine.SDK.Lua.Calls;

namespace CheatEngine.Client.Core.Tests.Domains;

/// <summary>
///     The global route classifies every <c>AobScanner.TryScanOutcome</c> outcome and its target context (audit F06,
///     CRIT-03), and releases the result list through the SDK's release outcome (F13).
/// </summary>
public sealed class PatternScannerHostOutcomeTests
{
	private const string ScanOperation = "Patterns.Scan";

	private static readonly Dictionary<AobScanOutcomeKind, (CheatEngineFailureKind Kind, CheatEngineHostEffect Effect)>
		WithoutListTable = new()
		{
			[AobScanOutcomeKind.Unknown] =
				(CheatEngineFailureKind.IndeterminateHostResult, CheatEngineHostEffect.Unknown),
			[AobScanOutcomeKind.Matches] = (CheatEngineFailureKind.InvalidHostResult, CheatEngineHostEffect.Completed),
			[AobScanOutcomeKind.NoMatches] = (CheatEngineFailureKind.InvalidHostResult, CheatEngineHostEffect.Completed),
			[AobScanOutcomeKind.GlobalUnavailable] =
				(CheatEngineFailureKind.CapabilityUnavailable, CheatEngineHostEffect.NotStarted),
			[AobScanOutcomeKind.ProtectedLuaFailure] = (CheatEngineFailureKind.LuaError, CheatEngineHostEffect.Unknown),
			[AobScanOutcomeKind.NoResult] =
				(CheatEngineFailureKind.IndeterminateHostResult, CheatEngineHostEffect.Completed),
			[AobScanOutcomeKind.InvalidResult] =
				(CheatEngineFailureKind.InvalidHostResult, CheatEngineHostEffect.Completed),
			[AobScanOutcomeKind.ResultListCountUnavailable] =
				(CheatEngineFailureKind.InvalidHostResult, CheatEngineHostEffect.Completed)
		};

	public static TheoryData<string> F06Cases =>
	[
		"AbsentGlobal",
		"NilResult",
		"ProtectedError",
		"MalformedResult",
		"EmptyList"
	];

	/// <summary>The five F06 acceptance cases: each SDK outcome keeps its own meaning, and only a list is a success.</summary>
	[Theory]
	[Trait("Qualification", "Q27")]
	[MemberData(nameof(F06Cases))]
	public void EachGlobalHostOutcomeKeepsItsOwnMeaning(string outcome)
	{
		(FakeAobScanPort port, bool expectedSuccess, CheatEngineFailureKind expectedKind,
			CheatEngineHostEffect expectedEffect) = outcome switch
			{
				"AbsentGlobal" => (Port(AobScanOutcomeKind.GlobalUnavailable), false,
					CheatEngineFailureKind.CapabilityUnavailable, CheatEngineHostEffect.NotStarted),
				"NilResult" => (Port(AobScanOutcomeKind.NoResult), false,
					CheatEngineFailureKind.IndeterminateHostResult, CheatEngineHostEffect.Completed),
				"ProtectedError" => (Port(AobScanOutcomeKind.ProtectedLuaFailure), false, CheatEngineFailureKind.LuaError,
					CheatEngineHostEffect.Unknown),
				"MalformedResult" => (Port(AobScanOutcomeKind.InvalidResult), false,
					CheatEngineFailureKind.InvalidHostResult, CheatEngineHostEffect.Completed),
				"EmptyList" => (new FakeAobScanPort(new RecordingAobMatchList([])), true, CheatEngineFailureKind.Unknown,
					CheatEngineHostEffect.Unknown),
				_ => throw new ArgumentOutOfRangeException(nameof(outcome), outcome, null)
			};
		PatternScanner scanner = CreateScanner(port);

		bool succeeded = scanner.TryScan(Request(), out AobScanResult result, out CheatEngineFailure failure,
			TestContext.Current.CancellationToken);

		Assert.Equal(expectedSuccess, succeeded);
		Assert.Equal(1, port.ScanCalls);
		Assert.NotEqual(CheatEngineFailureKind.NotFound, failure.Kind);
		if (expectedSuccess)
		{
			Assert.Equal(default, failure);
			Assert.Empty(result.Matches);
			Assert.False(result.IsTruncated);
			return;
		}

		Assert.Equal(default, result);
		Assert.Equal(expectedKind, failure.Kind);
		Assert.Equal(expectedEffect, failure.HostEffect);
		Assert.Equal(ScanOperation, failure.Operation);
		Assert.Null(failure.Exception);
	}

	[Fact]
	[Trait("Qualification", "Q27")]
	public void ANilResultNamesItsSharedShapeAndTheProtectedErrorNamesItsLuaStatus()
	{
		PatternScanner nil = CreateScanner(Port(AobScanOutcomeKind.NoResult));
		PatternScanner raising = CreateScanner(Port(AobScanOutcomeKind.ProtectedLuaFailure));

		Assert.False(nil.TryScan(Request(), out _, out CheatEngineFailure nilFailure,
			TestContext.Current.CancellationToken));
		Assert.False(raising.TryScan(Request(), out _, out CheatEngineFailure raisingFailure,
			TestContext.Current.CancellationToken));

		Assert.Equal("CE AOBScan returned nil: on CE 7.7 zero matches and host failures share this shape",
			nilFailure.Message);
		Assert.Contains(LuaStatus.RuntimeError.ToString(), raisingFailure.Message, StringComparison.Ordinal);
	}

	/// <summary>A target change during the scan discards the addresses, which may belong to another process.</summary>
	[Theory]
	[Trait("Qualification", "Q27")]
	[Trait("Qualification", "Q30.a")]
	[InlineData("OtherProcess", CheatEngineFailureKind.TargetChanged)]
	[InlineData("ReusedProcessId", CheatEngineFailureKind.TargetChanged)]
	[InlineData("IdentityLost", CheatEngineFailureKind.TargetIdentityUnavailable)]
	[InlineData("IdentityGained", CheatEngineFailureKind.TargetIdentityUnavailable)]
	public void ATargetChangeDuringTheScanDiscardsItsAddresses(string change, CheatEngineFailureKind expectedKind)
	{
		(TargetSelectionFacts before, TargetSelectionFacts after) = change switch
		{
			"OtherProcess" => (AobHosts.Local(), AobHosts.Local(43, 2_000)),
			"ReusedProcessId" => (AobHosts.Local(), AobHosts.Local(42, 9_000)),
			"IdentityLost" => (AobHosts.Local(), AobHosts.Remote),
			"IdentityGained" => (AobHosts.Remote, AobHosts.Local()),
			_ => throw new ArgumentOutOfRangeException(nameof(change), change, null)
		};
		RecordingAobMatchList matches = new(["400000", "400010"]);
		FakeAobScanPort port = new(matches)
		{
			Outcome = AobHosts.Outcome(AobScanOutcomeKind.Matches, before, after, 2)
		};
		PatternScanner scanner = CreateScanner(port);

		bool succeeded = scanner.TryScan(Request(), out AobScanResult result, out CheatEngineFailure failure,
			TestContext.Current.CancellationToken);

		Assert.False(succeeded);
		Assert.Equal(default, result);
		Assert.Equal(expectedKind, failure.Kind);
		Assert.Equal(CheatEngineHostEffect.Completed, failure.HostEffect);
		Assert.Equal(0, matches.ItemCalls);
		Assert.Equal(1, matches.ReleaseCount);
	}

	[Fact]
	[Trait("Qualification", "Q27")]
	public void ANilResultOnAChangedTargetIsATargetChangeNotAnIndeterminateZero()
	{
		PatternScanner scanner = CreateScanner(new FakeAobScanPort
		{
			Outcome = AobHosts.Outcome(AobScanOutcomeKind.NoResult, AobHosts.Local(), AobHosts.Local(43, 2_000))
		});

		Assert.False(scanner.TryScan(Request(), out _, out CheatEngineFailure failure,
			TestContext.Current.CancellationToken));

		Assert.Equal(CheatEngineFailureKind.TargetChanged, failure.Kind);
		Assert.Equal(CheatEngineHostEffect.Completed, failure.HostEffect);
	}

	/// <summary>An unqualified target that did not visibly change keeps its answer (the scan is unverified, not refused).</summary>
	[Theory]
	[InlineData("FileAsProcess")]
	[InlineData("Remote")]
	public void AnUnqualifiedTargetThatDidNotChangeKeepsItsAnswer(string target)
	{
		TargetSelectionFacts selection = target == "Remote" ? AobHosts.Remote : AobHosts.FileAsProcess;
		RecordingAobMatchList matches = new(["400000"]);
		PatternScanner scanner = CreateScanner(new FakeAobScanPort(matches)
		{
			Outcome = AobHosts.Outcome(AobScanOutcomeKind.Matches, selection, selection, 1)
		});

		bool succeeded = scanner.TryScan(Request(), out AobScanResult result, out CheatEngineFailure failure,
			TestContext.Current.CancellationToken);

		Assert.True(succeeded, failure.Message);
		Assert.Equal([0x400000], result.Matches);
		Assert.Equal(1, matches.ReleaseCount);
	}

	[Fact]
	public void ASuccessfulOutcomeWithoutAListIsAnInvalidHostResult()
	{
		PatternScanner scanner = CreateScanner(new FakeAobScanPort
		{
			Outcome = AobHosts.Outcome(AobScanOutcomeKind.Matches, 3)
		});

		Assert.False(scanner.TryScan(Request(), out _, out CheatEngineFailure failure,
			TestContext.Current.CancellationToken));

		Assert.Equal(CheatEngineFailureKind.InvalidHostResult, failure.Kind);
		Assert.Equal(CheatEngineHostEffect.Completed, failure.HostEffect);
	}

	[Fact]
	public void AListHandedOutWithAFailedOutcomeIsReleasedOnceAndNeverCopied()
	{
		RecordingAobMatchList matches = new(["400000"]);
		PatternScanner scanner = CreateScanner(new FakeAobScanPort(matches)
		{
			Outcome = AobHosts.Outcome(AobScanOutcomeKind.NoResult)
		});

		Assert.False(scanner.TryScan(Request(), out _, out CheatEngineFailure failure,
			TestContext.Current.CancellationToken));

		Assert.Equal(CheatEngineFailureKind.InvalidHostResult, failure.Kind);
		Assert.Equal(0, matches.ItemCalls);
		Assert.Equal(1, matches.ReleaseCount);
	}

	[Fact]
	public void ACopyFaultIsClassifiedThroughTheSdkBoundaryAndStillReleasesTheList()
	{
		LuaException fault = new("the list could not be read");
		RecordingAobMatchList matches = new(["400000"])
		{
			OnTryGetItem = _ => throw fault
		};
		PatternScanner scanner = CreateScanner(new FakeAobScanPort(matches));

		bool succeeded = scanner.TryScan(Request(), out _, out CheatEngineFailure failure,
			TestContext.Current.CancellationToken);

		Assert.False(succeeded);
		Assert.Equal(CheatEngineFailureKind.LuaError, failure.Kind);
		Assert.Equal(CheatEngineHostEffect.Completed, failure.HostEffect);
		Assert.Same(fault, failure.Exception);
		Assert.Equal(1, matches.ReleaseCount);
	}

	/// <summary>
	///     The copy-fault path goes through <c>SdkBoundary</c>, including its activation check: a fault observed after the
	///     activation ended is an expired activation, never an ordinary failure, and the list is still released once.
	/// </summary>
	[Fact]
	public void ACopyFaultAfterTheActivationEndedIsAnExpiredActivationAndStillReleasesTheList()
	{
		using ControlledCoreLifetimeContext context = new();
		using CoreLifetime lifetime = new(context);
		LuaException fault = new("the list could not be read");
		RecordingAobMatchList matches = new(["400000"])
		{
			OnTryGetItem = _ =>
			{
				context.IsCurrent = false;
				throw fault;
			}
		};
		PatternScanner scanner = new(new SdkMainThreadDispatcher(lifetime, new InlineMainThreadInvoker()),
			new FakeAobScanPort(matches));

		CheatEngineActivationExpiredException exception = Assert.Throws<CheatEngineActivationExpiredException>(() =>
			scanner.TryScan(Request(), out _, out _, TestContext.Current.CancellationToken));

		Assert.Equal(ScanOperation, exception.Failure.Operation);
		Assert.Same(fault, exception.Failure.Exception);
		Assert.Equal(1, matches.ReleaseCount);
	}

	/// <summary>
	///     A list the port could not publish, whose release was not confirmed, is reported by its typed release kind as
	///     <see cref="CheatEngineHostEffect.CleanupUnconfirmed" />, keeping the publication fault's classification.
	/// </summary>
	[Theory]
	[InlineData(TargetReleaseStatus.UnconfirmedAfterInvocation, "CleanupUnconfirmed")]
	[InlineData(TargetReleaseStatus.NotInvoked, "CleanupUnavailable")]
	public void AListWhosePublicationFailedAndWhoseReleaseWasNotConfirmedIsCleanupUnconfirmed(
		TargetReleaseStatus releaseStatus, string expectedKind)
	{
		InvalidOperationException publishFailure = new("the result list wrapper could not be published");
		PatternScanner scanner = CreateScanner(new FakeAobScanPort
		{
			OnScan = () => _ = OwnershipHandoff.Adopt<object, object>(new object(), _ => throw publishFailure,
				_ => SdkReleaseOutcomes.FromTarget(releaseStatus))
		});

		Assert.False(scanner.TryScan(Request(), out AobScanResult result, out CheatEngineFailure failure,
			TestContext.Current.CancellationToken));

		Assert.Equal(default, result);
		Assert.Equal(CheatEngineFailureKind.OperationRejected, failure.Kind);
		Assert.Equal(CheatEngineHostEffect.CleanupUnconfirmed, failure.HostEffect);
		Assert.Equal(ScanOperation, failure.Operation);
		Assert.Same(publishFailure, failure.Exception);
		Assert.EndsWith($"The AOB result list release was not confirmed ({expectedKind}).", failure.Message,
			StringComparison.Ordinal);
	}

	[Fact]
	[Trait("Qualification", "Q48")]
	public void EveryAobScanOutcomeKindWithoutAListIsMappedAndAnUnknownKindFailsClosed()
	{
		MappingTotality.AssertTotal<AobScanOutcomeKind>(
			static kind => WithoutListTable.TryGetValue(kind, out (CheatEngineFailureKind Kind,
								CheatEngineHostEffect Effect) expected) &&
							Matches(AobScanMapping.ToFailure(ScanOperation, AobHosts.Outcome(kind)), expected),
			static kind => Matches(AobScanMapping.ToFailure(ScanOperation, AobHosts.Outcome(kind)),
				(CheatEngineFailureKind.IndeterminateHostResult, CheatEngineHostEffect.Unknown)));
	}

	/// <summary>Only a confirmed release lets a copied result through; every other status is CleanupUnconfirmed.</summary>
	[Fact]
	[Trait("Qualification", "Q48")]
	public void EveryTargetReleaseStatusButReleasedDiscardsTheCopyAsCleanupUnconfirmed()
	{
		MappingTotality.AssertTotal<TargetReleaseStatus>(
			static status => ScanWithRelease(status) is var (succeeded, failure) &&
							 (status == TargetReleaseStatus.Released
								 ? succeeded
								 : !succeeded && failure.HostEffect == CheatEngineHostEffect.CleanupUnconfirmed &&
								   failure.Kind == CheatEngineFailureKind.IndeterminateHostResult),
			static status => ScanWithRelease(status) is (false, { HostEffect: CheatEngineHostEffect.CleanupUnconfirmed }));
	}

	[Theory]
	[InlineData("SameIncarnation", nameof(AobTargetVerdict.Verified))]
	[InlineData("OtherIncarnation", nameof(AobTargetVerdict.Changed))]
	[InlineData("OtherUnqualifiedProcess", nameof(AobTargetVerdict.Changed))]
	[InlineData("QualifiedOnOneSide", nameof(AobTargetVerdict.IdentityUnavailable))]
	[InlineData("UnqualifiedStatusChanged", nameof(AobTargetVerdict.IdentityUnavailable))]
	[InlineData("SameUnqualifiedSelection", nameof(AobTargetVerdict.Unverified))]
	[InlineData("NothingObserved", nameof(AobTargetVerdict.Unverified))]
	public void TheTargetVerdictFollowsTheTwoObservations(string pair, string expected)
	{
		(TargetSelectionFacts before, TargetSelectionFacts after) = pair switch
		{
			"SameIncarnation" => (AobHosts.Local(), AobHosts.Local()),
			"OtherIncarnation" => (AobHosts.Local(), AobHosts.Local(42, 5_000)),
			"OtherUnqualifiedProcess" => (AobHosts.Remote, AobHosts.Remote with { SelectedProcessId = 7 }),
			"QualifiedOnOneSide" => (AobHosts.Local(), AobHosts.Remote),
			"UnqualifiedStatusChanged" => (AobHosts.Remote, AobHosts.Remote with
			{
				Status = TargetSelectionObservationStatus.CurrentTargetUnqualified
			}),
			"SameUnqualifiedSelection" => (AobHosts.FileAsProcess, AobHosts.FileAsProcess),
			"NothingObserved" => (default(TargetSelectionFacts), default(TargetSelectionFacts)),
			_ => throw new ArgumentOutOfRangeException(nameof(pair), pair, null)
		};

		Assert.Equal(Enum.Parse<AobTargetVerdict>(expected), AobScanMapping.JudgeTarget(before, after));
	}

	private static (bool Succeeded, CheatEngineFailure Failure) ScanWithRelease(TargetReleaseStatus status)
	{
		RecordingAobMatchList matches = new(["400000"])
		{
			ReleaseStatus = status
		};
		PatternScanner scanner = CreateScanner(new FakeAobScanPort(matches));
		bool succeeded = scanner.TryScan(Request(), out _, out CheatEngineFailure failure,
			TestContext.Current.CancellationToken);
		Assert.Equal(1, matches.ReleaseCount);
		return (succeeded, failure);
	}

	private static bool Matches(CheatEngineFailure failure,
		(CheatEngineFailureKind Kind, CheatEngineHostEffect Effect) expected)
	{
		return failure.Kind == expected.Kind && failure.HostEffect == expected.Effect &&
			   failure.Operation == ScanOperation && !string.IsNullOrWhiteSpace(failure.Message);
	}

	private static FakeAobScanPort Port(AobScanOutcomeKind kind)
	{
		return new FakeAobScanPort
		{
			Outcome = AobHosts.Outcome(kind)
		};
	}

	private static PatternScanner CreateScanner(FakeAobScanPort port)
	{
		return new PatternScanner(
			new SdkMainThreadDispatcher(InertCoreLifetime.Create(), new InlineMainThreadInvoker()), port);
	}

	private static AobScanRequest Request()
	{
		return new AobScanRequest(new AobPattern("90"), 2);
	}
}
