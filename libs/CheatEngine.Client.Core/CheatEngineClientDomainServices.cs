#pragma warning disable CECLIENT5003 // Core composes the experimental instruction client it serves.

using CheatEngine.Client.Allocations;
using CheatEngine.Client.Assembly;
using CheatEngine.Client.Inspection;
using CheatEngine.Client.Lua;
using CheatEngine.Client.Memory;
using CheatEngine.Client.Processes;
using CheatEngine.Client.Scanning;
using CheatEngine.Client.Tables;

namespace CheatEngine.Client.Core;

/// <summary>Internal grouping of the independently consumable high-level Client domains.</summary>
internal sealed record CheatEngineClientDomainServices(
	IProcessClient Processes,
	IMemoryClient Memory,
	IPatternScanner Patterns,
	IValueScanner Scans,
	IInspectionClient Inspection,
	ITableClient Tables,
	ILuaClient Lua,
	IAllocationClient Allocations,
	IAssemblyClient Assembly);
