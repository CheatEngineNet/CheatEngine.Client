using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Reflection;

using BenchmarkDotNet.Attributes;

using CheatEngine.Client.Core.Domains;
using CheatEngine.Client.Scanning;
using CheatEngine.SDK.Engine.Inspection;
using CheatEngine.SDK.Engine.Runtime;
using CheatEngine.SDK.Engine.Scanning.Aob;
using CheatEngine.SDK.Engine.Scanning.Values;
using CheatEngine.SDK.Engine.Targets;
using CheatEngine.SDK.Engine.Values;
using CheatEngine.SDK.Lua.Calls;

namespace CheatEngine.Client.Benchmarks;

/// <summary>
///     Compares the Client cost of the two routes a module request can take (CRIT-16): the global route, which copies,
///     parses and post-filters every row of a global result list, and the bounded route, which receives only the
///     in-module addresses. Both run the production scanner over a fake port.
/// </summary>
/// <remarks>
///     <para>
///         Cheat Engine's scan time is deliberately excluded (audit ch.24 "separate the costs"): the bounded route's real
///         advantage is the Cheat Engine work it avoids, which only a live host can measure, and this suite publishes no
///         host number. What it shows is the managed side of the same answer: the global route pays for every row
///         Cheat Engine returned, the bounded route only for the rows inside the module.
///     </para>
///     <para>
///         As in <see cref="PatternScannerMaterializationBenchmarks" />, 1 % of the global rows fall inside the module.
///     </para>
/// </remarks>
[MemoryDiagnoser]
[BenchmarkCategory("AobRouteComparison", "Informational")]
public class AobRouteComparisonBenchmarks
{
	private const ulong ModuleBase = 0x1000_0000;
	private const ulong ModuleSize = 0x1000_0000;

	private AobScanRequest _request;
	private PatternScanner _scanner = null!;

	/// <summary>Gets or sets the number of rows the global route's result list holds.</summary>
	[Params(10_000, 1_000_000)]
	public int HostRowCount
	{
		get;
		set;
	}

	/// <summary>Gets or sets whether the target is qualified, so the module request takes the bounded route.</summary>
	[Params(false, true)]
	public bool BoundedRoute
	{
		get;
		set;
	}

	/// <summary>Builds the global rows, their in-module subset and a scanner whose dispatcher runs inline.</summary>
	[GlobalSetup]
	public void Setup()
	{
		string[] rows = new string[HostRowCount];
		List<Address> inModule = [];
		for (int index = 0; index < rows.Length; index++)
		{
			ulong offset = (ulong) index * 16;
			if (index % 100 == 0)
			{
				ulong address = ModuleBase + offset;
				rows[index] = address.ToString("X8", CultureInfo.InvariantCulture);
				inModule.Add(new Address(address));
				continue;
			}

			rows[index] = (0x7FFC_0000_0000UL + offset).ToString("X", CultureInfo.InvariantCulture);
		}

		ModuleInfo module = new("game.exe", new Address(ModuleBase), new MemorySize(ModuleSize), true, "game.exe");
		_scanner = new PatternScanner(InlineCoreHost.CreateDispatcher(InlineCoreHost.CreateLifetime()),
			new RoutePort(rows, [.. inModule], module, BoundedRoute));
		_request = new AobScanRequest(new AobPattern("48 8B ?? ?? ?? 89"), inModule.Count, new ModuleName("game.exe"));
	}

	/// <summary>Runs one module request through the production scanner on the configured route.</summary>
	/// <returns>The number of copied addresses, so the JIT cannot discard the work.</returns>
	[Benchmark]
	public int ScanModule()
	{
		if (!_scanner.TryScan(_request, out AobScanResult result, out _))
		{
			throw new InvalidOperationException("The benchmark scan unexpectedly failed.");
		}

		return result.Matches.Length;
	}

	/// <summary>A port that answers both routes from memory; the selection is qualified only for the bounded route.</summary>
	private sealed class RoutePort(string[] rows, Address[] inModule, ModuleInfo module, bool qualified) : IAobScanPort
	{
		private readonly TargetSelectionFacts _selection = qualified
			? new TargetSelectionFacts(TargetSelectionObservationStatus.CurrentTargetQualified, TargetBackend.LocalProcess,
				42, Incarnation(42, 1_000))
			: default;

		public AobHostOutcome TryScan(string pattern, AobScanOptions options, out IAobMatchList? matches)
		{
			matches = new RowList(rows);
			return new AobHostOutcome(AobScanOutcomeKind.Matches, LuaStatus.Ok, rows.Length, default, default);
		}

		public AobBoundedHostResult TryScanWithinBounds(string pattern, AobScanBounds bounds, AobScanOptions options,
			Span<Address> destination, CancellationToken cancellationToken)
		{
			int written = Math.Min(inModule.Length, destination.Length);
			inModule.AsSpan(0, written).CopyTo(destination);
			return new AobBoundedHostResult
			{
				Kind = written > 0 ? AobBoundedScanOutcomeKind.Matches : AobBoundedScanOutcomeKind.NoMatches,
				CreationStatus = MemoryScanCreationStatus.Success,
				LuaStatus = LuaStatus.Ok,
				HostResultCount = (ulong) inModule.Length,
				Written = written,
				RowsRead = (ulong) written,
				UnreadHostRows = (ulong) (inModule.Length - written),
				IsMaterializationLimitReached = written == destination.Length && inModule.Length > written,
				HostScanElapsed = TimeSpan.FromTicks(1),
				FoundListRelease = TargetReleaseStatus.Released,
				MemScanRelease = TargetReleaseStatus.Released,
				ReleaseTermination = MemoryScanTerminationStatus.NotRequired
			};
		}

		public TargetSelectionFacts ObserveSelection()
		{
			return _selection;
		}

		public InspectionStatus EnumerateModules(ModuleInfo[] destination, out int written)
		{
			destination[0] = module;
			written = 1;
			return InspectionStatus.Success;
		}

		/// <summary>The SDK's incarnation constructor is internal; a benchmark double builds one like the SDK would.</summary>
		private static TargetProcessIncarnation Incarnation(int processId, long startedAtUtcTicks)
		{
			ConstructorInfo constructor = typeof(TargetProcessIncarnation).GetConstructor(
											  BindingFlags.Instance | BindingFlags.NonPublic, [typeof(int), typeof(long)])
										  ?? throw new InvalidOperationException(
											  "CheatEngine.SDK no longer declares TargetProcessIncarnation(int, long).");
			return (TargetProcessIncarnation) constructor.Invoke([processId, startedAtUtcTicks]);
		}
	}

	private sealed class RowList(string[] rows) : IAobMatchList
	{
		public bool TryGetCount(out int count)
		{
			count = rows.Length;
			return true;
		}

		public bool TryGetItem(int index, [NotNullWhen(true)] out string? value)
		{
			value = (uint) index < (uint) rows.Length ? rows[index] : null;
			return value is not null;
		}

		public TargetReleaseStatus Release()
		{
			return TargetReleaseStatus.Released;
		}
	}
}
