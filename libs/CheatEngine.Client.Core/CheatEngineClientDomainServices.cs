using CheatEngine.Client.Allocations;
using CheatEngine.Client.Assembly;
using CheatEngine.Client.Dbvm;
using CheatEngine.Client.Debugger;
using CheatEngine.Client.Hashing;
using CheatEngine.Client.Hotkeys;
using CheatEngine.Client.Inspection;
using CheatEngine.Client.Lua;
using CheatEngine.Client.Memory;
using CheatEngine.Client.Processes;
using CheatEngine.Client.RemoteExecution;
using CheatEngine.Client.Scanning;
using CheatEngine.Client.Speed;
using CheatEngine.Client.Tables;
using CheatEngine.Client.Timers;

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
	IAssemblyClient Assembly,
	IRemoteExecutionClient RemoteExecution,
	IDebuggerClient Debugger,
	IHotkeyClient Hotkeys,
	ITimerClient Timers,
	ISpeedClient Speed,
	IHashingClient Hashing,
	IDbvmClient Dbvm);
