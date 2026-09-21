using CheatEngine.Client.Allocations;
using CheatEngine.Client.Assembly;
using CheatEngine.Client.Core.Infrastructure;
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

namespace CheatEngine.Client.Core;

internal sealed class CheatEngineClient : ICheatEngineClient
{
	private readonly CoreLifetime _lifetime;

	internal CheatEngineClient(
		CoreLifetime lifetime,
		CheatEngineClientRuntimeServices runtimeServices,
		CheatEngineClientDomainServices domainServices)
	{
		_lifetime = lifetime ?? throw new ArgumentNullException(nameof(lifetime));
		ArgumentNullException.ThrowIfNull(runtimeServices);
		ArgumentNullException.ThrowIfNull(domainServices);

		Runtime = runtimeServices.Runtime ?? throw new ArgumentNullException(nameof(runtimeServices));
		Dispatcher = runtimeServices.Dispatcher ?? throw new ArgumentNullException(nameof(runtimeServices));
		Processes = domainServices.Processes ?? throw new ArgumentNullException(nameof(domainServices));
		Memory = domainServices.Memory ?? throw new ArgumentNullException(nameof(domainServices));
		Patterns = domainServices.Patterns ?? throw new ArgumentNullException(nameof(domainServices));
		Scans = domainServices.Scans ?? throw new ArgumentNullException(nameof(domainServices));
		Inspection = domainServices.Inspection ?? throw new ArgumentNullException(nameof(domainServices));
		Tables = domainServices.Tables ?? throw new ArgumentNullException(nameof(domainServices));
		Lua = domainServices.Lua ?? throw new ArgumentNullException(nameof(domainServices));
		Allocations = domainServices.Allocations ?? throw new ArgumentNullException(nameof(domainServices));
		Assembly = domainServices.Assembly ?? throw new ArgumentNullException(nameof(domainServices));
		RemoteExecution = domainServices.RemoteExecution ?? throw new ArgumentNullException(nameof(domainServices));
		Debugger = domainServices.Debugger ?? throw new ArgumentNullException(nameof(domainServices));
		Hotkeys = domainServices.Hotkeys ?? throw new ArgumentNullException(nameof(domainServices));
		Timers = domainServices.Timers ?? throw new ArgumentNullException(nameof(domainServices));
		Speed = domainServices.Speed ?? throw new ArgumentNullException(nameof(domainServices));
		Hashing = domainServices.Hashing ?? throw new ArgumentNullException(nameof(domainServices));
		Dbvm = domainServices.Dbvm ?? throw new ArgumentNullException(nameof(domainServices));
	}

	public long Epoch => _lifetime.Epoch;
	public CancellationToken Stopping => _lifetime.Stopping;

	public ICheatEngineRuntime Runtime
	{
		get;
	}

	public ICheatEngineDispatcher Dispatcher
	{
		get;
	}

	public IProcessClient Processes
	{
		get;
	}

	public IMemoryClient Memory
	{
		get;
	}

	public IPatternScanner Patterns
	{
		get;
	}

	public IValueScanner Scans
	{
		get;
	}

	public IInspectionClient Inspection
	{
		get;
	}

	public ITableClient Tables
	{
		get;
	}

	public ILuaClient Lua
	{
		get;
	}

	public IAllocationClient Allocations
	{
		get;
	}

	public IAssemblyClient Assembly
	{
		get;
	}

	public IRemoteExecutionClient RemoteExecution
	{
		get;
	}

	public IDebuggerClient Debugger
	{
		get;
	}

	public IHotkeyClient Hotkeys
	{
		get;
	}

	public ITimerClient Timers
	{
		get;
	}

	public ISpeedClient Speed
	{
		get;
	}

	public IHashingClient Hashing
	{
		get;
	}

	public IDbvmClient Dbvm
	{
		get;
	}
}
