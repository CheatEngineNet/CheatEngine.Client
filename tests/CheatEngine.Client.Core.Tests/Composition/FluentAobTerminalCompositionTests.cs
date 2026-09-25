using CheatEngine.Client.Core.Dispatching;
using CheatEngine.Client.Core.Domains;
using CheatEngine.Client.Core.Tests.TestSupport;
using CheatEngine.Client.Results;
using CheatEngine.Client.Scanning;
using CheatEngine.SDK.Engine.Inspection;
using CheatEngine.SDK.Engine.Scanning.Aob;
using CheatEngine.SDK.Engine.Values;

namespace CheatEngine.Client.Core.Tests.Composition;

/// <summary>
///     Runs the Fluent AOB terminals, compiled in from <c>libs/CheatEngine.Client.Fluent/Scanning</c>, against Core's real
///     <see cref="PatternScanner" /> and the AOB port doubles, route by route: <c>FirstOrNone</c>, <c>RequireSingle</c>
///     and <c>Take</c> judge the shapes Core publishes, not a scanner double's model of them.
/// </summary>
/// <remarks>
///     The host rows are space-separated hexadecimal addresses, and a <see langword="null" /> row list is the global
///     route's <c>nil</c> result. The request is a 4-byte pattern scoped to the module <c>game.exe</c>
///     <c>[0x4000, 0x4100)</c>, except on the unscoped route: <c>40FC</c> is the module's last whole match, <c>40FD</c>
///     to <c>40FF</c> straddle its end and <c>4100</c> starts after it, so the Client's scope rule drops them on every
///     route.
/// </remarks>
public sealed class FluentAobTerminalCompositionTests
{
	/// <summary>A factual zero on each route: every row Cheat Engine returned was read and none lay inside the request.</summary>
	[Theory]
	[InlineData(PatternScanScope.HostBoundedRange, "")]
	[InlineData(PatternScanScope.HostBoundedRange, "40FD")]
	[InlineData(PatternScanScope.GlobalHostScanWithManagedFilter, "3000 40FD 5000")]
	[InlineData(PatternScanScope.GlobalHostScan, "")]
	public void EveryTerminalReportsTheFactualZeroOfEveryRoute(PatternScanScope route, string rows)
	{
		Address? first = new Address(1);
		CheatEngineFailure firstFailure = default;
		AobScanResult many = default;

		bool firstSucceeded = Run(route, rows, builder => builder.FirstOrNone().TryExecute(out first,
			out firstFailure, TestContext.Current.CancellationToken));
		CheatEngineFailure single = RequireSingleFailure(route, rows);
		bool manySucceeded = Run(route, rows, builder => builder.Take(2).TryExecute(out many, out _,
			TestContext.Current.CancellationToken));

		Assert.True(firstSucceeded, firstFailure.Message);
		Assert.Null(first);
		Assert.Equal((CheatEngineFailureKind.NotFound, "Patterns.Scan"), (single.Kind, single.Operation));
		Assert.True(manySucceeded);
		Assert.Empty(many.Matches);
		Assert.False(many.IsTruncated);
	}

	/// <summary>
	///     A zero that Core cannot prove is never <see langword="null" />, <see cref="CheatEngineFailureKind.NotFound" />
	///     or an empty result: the bounded route's full destination of rows outside the request (a row stays unread),
	///     and the global routes' <c>nil</c> result. Core fails both itself, and every terminal returns that failure.
	/// </summary>
	[Theory]
	[Trait("Qualification", "Q27")]
	[InlineData(PatternScanScope.HostBoundedRange, "40FD 40FE 40FF 4100")]
	[InlineData(PatternScanScope.GlobalHostScanWithManagedFilter, null)]
	[InlineData(PatternScanScope.GlobalHostScan, null)]
	public void NoTerminalTurnsAnUnprovenZeroIntoAnAnswer(PatternScanScope route, string? rows)
	{
		CheatEngineFailure first = default;
		CheatEngineFailure many = default;

		bool firstSucceeded = Run(route, rows, builder => builder.FirstOrNone().TryExecute(out _, out first,
			TestContext.Current.CancellationToken));
		CheatEngineFailure single = RequireSingleFailure(route, rows);
		bool manySucceeded = Run(route, rows, builder => builder.Take(2).TryExecute(out _, out many,
			TestContext.Current.CancellationToken));

		Assert.False(firstSucceeded);
		Assert.False(manySucceeded);
		Assert.All([first, single, many], static failure =>
		{
			Assert.Equal((CheatEngineFailureKind.IndeterminateHostResult, "Patterns.Scan"),
				(failure.Kind, failure.Operation));
			// The terminals name their own failures Patterns.Scan too: the message tells Core's failure from theirs.
			Assert.NotEqual(AobTerminal.Unread("Patterns.Scan").Message, failure.Message);
		});
	}

	/// <summary>One match inside the request, next to a row outside it on the scoped routes, is proven unique.</summary>
	[Theory]
	[InlineData(PatternScanScope.HostBoundedRange, "3000 4010")]
	[InlineData(PatternScanScope.GlobalHostScanWithManagedFilter, "3000 4010")]
	[InlineData(PatternScanScope.GlobalHostScan, "4010")]
	public void RequireSingleReturnsTheOnlyMatchOnEveryRoute(PatternScanScope route, string rows)
	{
		Address address = default;
		CheatEngineFailure failure = default;

		bool succeeded = Run(route, rows, builder => builder.RequireSingle().TryExecute(out address, out failure,
			TestContext.Current.CancellationToken));

		Assert.True(succeeded, failure.Message);
		Assert.Equal(new Address(0x4010), address);
	}

	/// <summary>
	///     A second copied match is ambiguous on every route, whether Core copied exactly two matches or cut the copy at
	///     two because a third exists.
	/// </summary>
	[Theory]
	[InlineData(PatternScanScope.HostBoundedRange, "4010 4020")]
	[InlineData(PatternScanScope.HostBoundedRange, "4010 4020 4030")]
	[InlineData(PatternScanScope.GlobalHostScanWithManagedFilter, "4010 4020")]
	[InlineData(PatternScanScope.GlobalHostScanWithManagedFilter, "4010 4020 4030")]
	[InlineData(PatternScanScope.GlobalHostScan, "4010 4020")]
	[InlineData(PatternScanScope.GlobalHostScan, "4010 4020 4030")]
	public void RequireSingleReportsASecondCopiedMatchAsAmbiguousOnEveryRoute(PatternScanScope route, string rows)
	{
		CheatEngineFailure failure = RequireSingleFailure(route, rows);

		Assert.Equal((CheatEngineFailureKind.AmbiguousMatch, "Patterns.Scan"), (failure.Kind, failure.Operation));
	}

	/// <summary>
	///     The bounded route's destination for <c>RequireSingle</c> holds three rows: <c>40FC</c> and two rows the Client
	///     drops, with <c>4100</c> unread. Core publishes one match, truncated, without an exact count: whether a second
	///     match exists is unknown, which is never reported as ambiguous. The global route reads every row and proves
	///     <c>40FC</c> unique. <c>FirstOrNone</c> and <c>Take</c> return <c>40FC</c> on both routes.
	/// </summary>
	[Theory]
	[Trait("Qualification", "Q28")]
	[InlineData(PatternScanScope.HostBoundedRange)]
	[InlineData(PatternScanScope.GlobalHostScanWithManagedFilter)]
	public void AMatchNextToRowsStraddlingTheModuleEndIsNeverReportedAsAmbiguous(PatternScanScope route)
	{
		const string rows = "40FC 40FD 40FF 4100";
		Address? first = null;
		Address single = default;
		CheatEngineFailure singleFailure = default;
		AobScanResult many = default;

		bool firstSucceeded = Run(route, rows, builder => builder.FirstOrNone().TryExecute(out first, out _,
			TestContext.Current.CancellationToken));
		bool singleSucceeded = Run(route, rows, builder => builder.RequireSingle().TryExecute(out single,
			out singleFailure, TestContext.Current.CancellationToken));
		bool manySucceeded = Run(route, rows, builder => builder.Take(2).TryExecute(out many, out _,
			TestContext.Current.CancellationToken));

		Assert.True(firstSucceeded);
		Assert.Equal(new Address(0x40FC), first);
		Assert.True(manySucceeded);
		Assert.Equal([new Address(0x40FC)], many.Matches);
		if (route == PatternScanScope.HostBoundedRange)
		{
			Assert.False(singleSucceeded);
			Assert.Equal((CheatEngineFailureKind.IndeterminateHostResult, "Patterns.Scan"),
				(singleFailure.Kind, singleFailure.Operation));
			Assert.Equal(CheatEngineHostEffect.Completed, singleFailure.HostEffect);
		}
		else
		{
			Assert.True(singleSucceeded, singleFailure.Message);
			Assert.Equal(new Address(0x40FC), single);
		}
	}

	/// <summary>Runs one terminal on a fresh port and checks that the scan took the requested route.</summary>
	private static bool Run(PatternScanScope route, string? rows, Func<AobScanBuilder, bool> terminal)
	{
		FakeAobScanPort port = Port(route, rows);
		PatternScanner scanner = new(
			new SdkMainThreadDispatcher(InertCoreLifetime.Create(), new InlineMainThreadInvoker()), port);
		AobScanBuilder builder = scanner.Aob("90 90 90 90");

		bool succeeded = terminal(route == PatternScanScope.GlobalHostScan ? builder : builder.InModule("game.exe"));

		Assert.Equal(route == PatternScanScope.HostBoundedRange ? 1 : 0, port.BoundedCalls);
		Assert.Equal(route == PatternScanScope.HostBoundedRange ? 0 : 1, port.ScanCalls);
		return succeeded;
	}

	private static CheatEngineFailure RequireSingleFailure(PatternScanScope route, string? rows)
	{
		CheatEngineFailure failure = default;

		Assert.False(Run(route, rows, builder => builder.RequireSingle().TryExecute(out _, out failure,
			TestContext.Current.CancellationToken)));

		return failure;
	}

	/// <summary>
	///     Builds the port of one route: a qualified local target whose bounded found list holds the rows, a CEServer
	///     target whose global list the Client filters, or an unscoped global scan.
	/// </summary>
	private static FakeAobScanPort Port(PatternScanScope route, string? rows)
	{
		string[]? items = rows?.Split(' ', StringSplitOptions.RemoveEmptyEntries);
		RecordingAobMatchList? list = items is null ? null : new RecordingAobMatchList(items);
		ModuleInfo module = new("game.exe", new Address(0x4000), new MemorySize(0x100), true, "game.exe");
		return route switch
		{
			PatternScanScope.HostBoundedRange => new FakeAobScanPort(list)
			{
				Modules = [module],
				Selection = AobHosts.Local(),
				BoundedRows = [.. (items ?? []).Select(static item => Address.Parse(item))]
			},
			PatternScanScope.GlobalHostScanWithManagedFilter => new FakeAobScanPort(list)
			{
				Modules = [module],
				Selection = AobHosts.Remote,
				Outcome = list is null
					? AobHosts.Outcome(AobScanOutcomeKind.NoResult, AobHosts.Remote, AobHosts.Remote)
					: null
			},
			_ => new FakeAobScanPort(list)
			{
				Outcome = list is null ? AobHosts.Outcome(AobScanOutcomeKind.NoResult) : null
			}
		};
	}
}
