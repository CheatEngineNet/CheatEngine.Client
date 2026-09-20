using CheatEngine.SDK.Engine.Inspection;
using CheatEngine.SDK.Engine.Values;

namespace CheatEngine.Client.Core.Domains;

/// <summary>Internal adapter boundary for the protected SDK inspection primitives.</summary>
internal interface IInspectionPort
{
	public InspectionStatus EnumerateModules(ModuleInfo[] destination, out int written);

	public InspectionStatus EnumerateModules(TargetProcessId processId, ModuleInfo[] destination, out int written);

	public InspectionStatus EnumerateSections(ModuleName moduleName, ModuleSectionInfo[] destination, out int written);

	public InspectionStatus EnumerateMemoryRegions(MemoryRegionInfo[] destination, out int written);

	public InspectionStatus GetMemoryRegion(Address address, out MemoryRegionInfo region);

	public InspectionStatus GetSymbol(SymbolExpression expression, out SymbolInfo symbol);

	public InspectionStatus ResolveAddress(SymbolExpression expression, AddressResolutionOptions options,
		out Address address);

	public bool TryResolveName(nuint address, out string? name);

	public void RegisterSymbol(string name, nuint address, bool doNotSave);

	public void UnregisterSymbol(string name);
}
