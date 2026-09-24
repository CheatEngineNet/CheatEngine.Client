using CheatEngine.SDK.Engine.Inspection;
using CheatEngine.SDK.Engine.Scanning.Aob;
using CheatEngine.SDK.Engine.Values;

namespace CheatEngine.Client.Core.Domains;

/// <summary>Internal adapter boundary that copies SDK-owned AOB results before Client materializes them.</summary>
internal interface IAobScanPort
{
	/// <summary>Runs one global <c>AOBScan</c> (<c>AobScanner.TryScanOutcome</c> with its target context).</summary>
	/// <param name="pattern">The normalized pattern text.</param>
	/// <param name="options">The SDK protection and alignment arguments.</param>
	/// <param name="matches">
	///     The owned result list whenever the SDK handed one out, whatever the outcome; <see langword="null" /> otherwise.
	///     The caller releases a returned list exactly once.
	/// </param>
	/// <returns>The copied SDK outcome and target observations.</returns>
	public AobHostOutcome TryScan(string pattern, AobScanOptions options, out IAobMatchList? matches);

	/// <summary>
	///     Runs one bounded, exhaustive scan (<c>AobScanner.TryScanWithinBounds</c>, the overload without a call
	///     deadline) and copies in-bounds addresses into <paramref name="destination" />.
	/// </summary>
	/// <param name="pattern">The normalized pattern text.</param>
	/// <param name="bounds">The Cheat Engine work limit <c>[Start, Stop)</c>.</param>
	/// <param name="options">The SDK protection and alignment arguments.</param>
	/// <param name="destination">The materialization limit; written only for a successful outcome.</param>
	/// <param name="cancellationToken">Observed by the SDK between Cheat Engine calls.</param>
	/// <returns>The copied SDK result; the session is already released.</returns>
	public AobBoundedHostResult TryScanWithinBounds(string pattern, AobScanBounds bounds, AobScanOptions options,
		Span<Address> destination, CancellationToken cancellationToken);

	/// <summary>Observes what identifies Cheat Engine's selected target (<c>TargetSelection.ObserveCurrent</c>).</summary>
	public TargetSelectionFacts ObserveSelection();

	public InspectionStatus EnumerateModules(ModuleInfo[] destination, out int written);
}
