using CheatEngine.SDK.Engine.Inspection;
using CheatEngine.SDK.Engine.Scanning.Aob;

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

	public InspectionStatus EnumerateModules(ModuleInfo[] destination, out int written);
}
