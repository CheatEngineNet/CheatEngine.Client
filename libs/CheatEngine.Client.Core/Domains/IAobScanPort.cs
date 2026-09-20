using System.Diagnostics.CodeAnalysis;

using CheatEngine.SDK.Engine.Inspection;
using CheatEngine.SDK.Engine.Scanning.Aob;

namespace CheatEngine.Client.Core.Domains;

/// <summary>Internal adapter boundary that copies SDK-owned AOB results before Client materializes them.</summary>
internal interface IAobScanPort
{
	public AobScanHostStatus TryScan(string pattern, AobScanOptions options,
		[NotNullWhen(true)] out IAobMatchList? matches);

	public InspectionStatus EnumerateModules(ModuleInfo[] destination, out int written);
}
