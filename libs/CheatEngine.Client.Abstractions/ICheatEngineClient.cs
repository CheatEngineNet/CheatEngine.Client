using CheatEngine.Client.Allocations;
using CheatEngine.Client.Assembly;
using CheatEngine.Client.Dbvm;
using CheatEngine.Client.Debugger;
using CheatEngine.Client.Dispatching;
using CheatEngine.Client.Hashing;
using CheatEngine.Client.Hotkeys;
using CheatEngine.Client.Inspection;
using CheatEngine.Client.Lua;
using CheatEngine.Client.Memory;
using CheatEngine.Client.Processes;
using CheatEngine.Client.RemoteExecution;
using CheatEngine.Client.Runtime;
using CheatEngine.Client.Scanning;
using CheatEngine.Client.Speed;
using CheatEngine.Client.Tables;
using CheatEngine.Client.Timers;

namespace CheatEngine.Client;

/// <summary>A scoped, high-level client for the active Cheat Engine plugin lifecycle.</summary>
/// <remarks>
///     The client never exposes Lua states, CE object handles, or SDK ownership wrappers. Its members are synchronous
///     because
///     an attached Cheat Engine Lua runtime cannot safely be retained across an <c>await</c> boundary.
/// </remarks>
public interface ICheatEngineClient
{
	/// <summary>Gets the SDK lifecycle epoch captured for this scoped client.</summary>
	public long Epoch
	{
		get;
	}

	/// <summary>Gets a token cancelled when the active plugin lifecycle begins stopping.</summary>
	public CancellationToken Stopping
	{
		get;
	}

	/// <summary>Gets the runtime and capability service.</summary>
	public ICheatEngineRuntime Runtime
	{
		get;
	}

	/// <summary>Gets the explicit main-thread dispatcher.</summary>
	public ICheatEngineDispatcher Dispatcher
	{
		get;
	}

	/// <summary>Gets the selected-target process service.</summary>
	public IProcessClient Processes
	{
		get;
	}

	/// <summary>Gets typed target-memory operations.</summary>
	public IMemoryClient Memory
	{
		get;
	}

	/// <summary>Gets AOB and value-scan operations.</summary>
	public IPatternScanner Patterns
	{
		get;
	}

	/// <summary>Gets value-scan operations.</summary>
	public IValueScanner Scans
	{
		get;
	}

	/// <summary>Gets copied module, symbol, and region inspection operations.</summary>
	public IInspectionClient Inspection
	{
		get;
	}

	/// <summary>Gets copied address-table and memory-record operations.</summary>
	public ITableClient Tables
	{
		get;
	}

	/// <summary>Gets the protected, handle-free Lua execution service.</summary>
	public ILuaClient Lua
	{
		get;
	}

	/// <summary>Gets owned target-memory allocation operations.</summary>
	public IAllocationClient Allocations
	{
		get;
	}

	/// <summary>Gets copied assembly, disassembly, comment, and Auto Assembler patch operations.</summary>
	public IAssemblyClient Assembly
	{
		get;
	}

	/// <summary>Gets bounded DLL injection and remote-call operations.</summary>
	public IRemoteExecutionClient RemoteExecution
	{
		get;
	}

	/// <summary>Gets breakpoint and copied debugger-event operations.</summary>
	public IDebuggerClient Debugger
	{
		get;
	}

	/// <summary>Gets activation-scoped hotkey operations.</summary>
	public IHotkeyClient Hotkeys
	{
		get;
	}

	/// <summary>Gets activation-scoped timer operations.</summary>
	public ITimerClient Timers
	{
		get;
	}

	/// <summary>Gets validated target-speed operations.</summary>
	public ISpeedClient Speed
	{
		get;
	}

	/// <summary>Gets explicitly separated target-memory and file hashing operations.</summary>
	public IHashingClient Hashing
	{
		get;
	}

	/// <summary>Gets observational and explicitly initialized DBVM operations.</summary>
	public IDbvmClient Dbvm
	{
		get;
	}
}
