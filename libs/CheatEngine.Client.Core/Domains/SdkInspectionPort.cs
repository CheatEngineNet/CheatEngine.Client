using CheatEngine.Client.Core.Infrastructure;
using CheatEngine.SDK.Engine.Inspection;
using CheatEngine.SDK.Engine.Values;

namespace CheatEngine.Client.Core.Domains;

/// <summary>Production adapter that delegates inspection and symbol operations to CheatEngine.SDK.</summary>
internal sealed class SdkInspectionPort : IInspectionPort
{
	public InspectionStatus EnumerateModules(ModuleInfo[] destination, out int written)
	{
		return EngineInspection.EnumerateModules(destination, out written);
	}

	public InspectionStatus EnumerateModules(TargetProcessId processId, ModuleInfo[] destination, out int written)
	{
		return EngineInspection.EnumerateModules(processId, destination, out written);
	}

	public InspectionStatus EnumerateSections(ModuleName moduleName, ModuleSectionInfo[] destination, out int written)
	{
		return EngineInspection.EnumerateSections(moduleName, destination, out written);
	}

	public InspectionStatus EnumerateMemoryRegions(MemoryRegionInfo[] destination, out int written)
	{
		return EngineInspection.EnumerateMemoryRegions(destination, out written);
	}

	public InspectionStatus GetMemoryRegion(Address address, out MemoryRegionInfo region)
	{
		return EngineInspection.GetMemoryRegionInfo(address, out region);
	}

	public InspectionStatus GetSymbol(SymbolExpression expression, out SymbolInfo symbol)
	{
		return EngineInspection.GetSymbolInfo(expression, out symbol);
	}

	public InspectionStatus ResolveAddress(SymbolExpression expression, AddressResolutionOptions options,
		out Address address)
	{
		return EngineInspection.ResolveAddress(expression, options, out address);
	}

	public bool TryResolveName(nuint address, out string? name)
	{
		return ClientLuaGlobals.TryGetNameFromAddress(address, out name);
	}

	public void RegisterSymbol(string name, nuint address, bool doNotSave)
	{
		ClientLuaGlobals.RegisterSymbol(name, address, doNotSave);
	}

	public void UnregisterSymbol(string name)
	{
		ClientLuaGlobals.UnregisterSymbol(name);
	}
}
