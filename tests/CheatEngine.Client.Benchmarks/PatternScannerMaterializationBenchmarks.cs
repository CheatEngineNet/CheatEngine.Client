using System.Diagnostics.CodeAnalysis;
using System.Globalization;

using BenchmarkDotNet.Attributes;

using CheatEngine.Client.Core.Domains;
using CheatEngine.Client.Scanning;
using CheatEngine.SDK.Engine.Inspection;
using CheatEngine.SDK.Engine.Scanning.Aob;
using CheatEngine.SDK.Engine.Targets;
using CheatEngine.SDK.Engine.Values;
using CheatEngine.SDK.Lua.Calls;

namespace CheatEngine.Client.Benchmarks;

/// <summary>
///     Measures only the Client cost of an AOB scan: reading the count, copying, parsing, and post-filtering the result
///     list returned by a fake port. Cheat Engine's scan time is deliberately excluded (audit ch.24 "separate the costs");
///     it is a C3 measurement, not something this benchmark can observe.
/// </summary>
/// <remarks>
///     The fake list mixes the two address formats Cheat Engine returns (spike C3 D4.10): unpadded upper-case hexadecimal
///     on x64 targets and eight-digit zero-padded hexadecimal on x86 targets. With the module filter enabled, 1 % of the
///     addresses fall inside the module, which reproduces the "many global matches, few in the module" shape where the
///     post-filter copies little but the global scan still costs a full scan.
/// </remarks>
[MemoryDiagnoser]
[BenchmarkCategory("AobMaterialization", "Informational")]
public class PatternScannerMaterializationBenchmarks
{
	private const ulong ModuleBase = 0x1000_0000;
	private const ulong ModuleSize = 0x1000_0000;

	private AobScanRequest _request;
	private PatternScanner _scanner = null!;

	/// <summary>Gets or sets the number of entries in the fake Cheat Engine result list.</summary>
	[Params(1_000, 100_000, 1_000_000)]
	public int HostMatchCount
	{
		get;
		set;
	}

	/// <summary>Gets or sets whether the request applies the managed module post-filter.</summary>
	[Params(false, true)]
	public bool ModuleFilter
	{
		get;
		set;
	}

	/// <summary>Builds the fake result list and a scanner whose dispatcher runs inline.</summary>
	[GlobalSetup]
	public void Setup()
	{
		string[] entries = new string[HostMatchCount];
		for (int index = 0; index < entries.Length; index++)
		{
			ulong offset = (ulong) index * 16;
			entries[index] = (index % 100) switch
			{
				0 => (ModuleBase + offset).ToString("X8", CultureInfo.InvariantCulture),
				_ when index % 2 == 0 => (0x7FFC_0000_0000UL + offset).ToString("X", CultureInfo.InvariantCulture),
				_ => (0x0040_0000UL + (offset % 0x0FC0_0000)).ToString("X8", CultureInfo.InvariantCulture)
			};
		}

		ModuleInfo module = new("game.exe", new Address(ModuleBase), new MemorySize(ModuleSize), true, "game.exe");
		_scanner = new PatternScanner(InlineCoreHost.CreateDispatcher(InlineCoreHost.CreateLifetime()),
			new InMemoryAobScanPort(entries, module));
		_request = new AobScanRequest(new AobPattern("48 8B ?? ?? ?? 89"), HostMatchCount,
			ModuleFilter ? new ModuleName("game.exe") : null);
	}

	/// <summary>Copies, parses, and filters the complete fake result list through the production scanner.</summary>
	/// <returns>The number of copied addresses, so the JIT cannot discard the work.</returns>
	[Benchmark]
	public int MaterializeResultList()
	{
		if (!_scanner.TryScan(_request, out AobScanResult result, out _))
		{
			throw new InvalidOperationException("The benchmark scan unexpectedly failed.");
		}

		return result.Matches.Length;
	}

	private sealed class InMemoryAobScanPort(string[] entries, ModuleInfo module) : IAobScanPort
	{
		public AobHostOutcome TryScan(string pattern, AobScanOptions options, out IAobMatchList? matches)
		{
			matches = new InMemoryAobMatchList(entries);
			return new AobHostOutcome(AobScanOutcomeKind.Matches, LuaStatus.Ok, entries.Length, default, default);
		}

		public AobBoundedHostResult TryScanWithinBounds(string pattern, AobScanBounds bounds, AobScanOptions options,
			Span<Address> destination, CancellationToken cancellationToken)
		{
			throw new NotSupportedException("This benchmark measures the global route only.");
		}

		/// <summary>An unobserved selection: a module request takes the global route with its managed post-filter.</summary>
		public TargetSelectionFacts ObserveSelection()
		{
			return default;
		}

		public InspectionStatus EnumerateModules(ModuleInfo[] destination, out int written)
		{
			destination[0] = module;
			written = 1;
			return InspectionStatus.Success;
		}
	}

	private sealed class InMemoryAobMatchList(string[] entries) : IAobMatchList
	{
		public bool TryGetCount(out int count)
		{
			count = entries.Length;
			return true;
		}

		public bool TryGetItem(int index, [NotNullWhen(true)] out string? value)
		{
			value = (uint) index < (uint) entries.Length ? entries[index] : null;
			return value is not null;
		}

		public TargetReleaseStatus Release()
		{
			return TargetReleaseStatus.Released;
		}
	}
}
