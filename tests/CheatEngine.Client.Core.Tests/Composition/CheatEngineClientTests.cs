using System.Reflection;

using CheatEngine.Client.Allocations;
using CheatEngine.Client.Assembly;
using CheatEngine.Client.Core.Tests.TestSupport;
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

namespace CheatEngine.Client.Core.Tests.Composition;

public sealed class CheatEngineClientTests
{
	[Fact]
	public void ExposesTheRuntimeAndDomainServicesFromItsActivationComposition()
	{
		ICheatEngineRuntime runtime = CreateProxy<ICheatEngineRuntime>();
		ICheatEngineDispatcher dispatcher = CreateProxy<ICheatEngineDispatcher>();
		IProcessClient processes = CreateProxy<IProcessClient>();
		IMemoryClient memory = CreateProxy<IMemoryClient>();
		IPatternScanner patterns = CreateProxy<IPatternScanner>();
		IValueScanner scans = CreateProxy<IValueScanner>();
		IInspectionClient inspection = CreateProxy<IInspectionClient>();
		ITableClient tables = CreateProxy<ITableClient>();
		ILuaClient lua = CreateProxy<ILuaClient>();
		IAllocationClient allocations = CreateProxy<IAllocationClient>();
		IAssemblyClient assembly = CreateProxy<IAssemblyClient>();
		IRemoteExecutionClient remoteExecution = CreateProxy<IRemoteExecutionClient>();
		IDebuggerClient debugger = CreateProxy<IDebuggerClient>();
		IHotkeyClient hotkeys = CreateProxy<IHotkeyClient>();
		ITimerClient timers = CreateProxy<ITimerClient>();
		ISpeedClient speed = CreateProxy<ISpeedClient>();
		IHashingClient hashing = CreateProxy<IHashingClient>();
		IDbvmClient dbvm = CreateProxy<IDbvmClient>();

		CheatEngineClient client = new(
			InertCoreLifetime.Create(),
			new CheatEngineClientRuntimeServices(runtime, dispatcher),
			new CheatEngineClientDomainServices(
				processes,
				memory,
				patterns,
				scans,
				inspection,
				tables,
				lua,
				allocations,
				assembly,
				remoteExecution,
				debugger,
				hotkeys,
				timers,
				speed,
				hashing,
				dbvm));

		Assert.Same(runtime, client.Runtime);
		Assert.Same(dispatcher, client.Dispatcher);
		Assert.Same(processes, client.Processes);
		Assert.Same(memory, client.Memory);
		Assert.Same(patterns, client.Patterns);
		Assert.Same(scans, client.Scans);
		Assert.Same(inspection, client.Inspection);
		Assert.Same(tables, client.Tables);
		Assert.Same(lua, client.Lua);
		Assert.Same(allocations, client.Allocations);
		Assert.Same(assembly, client.Assembly);
		Assert.Same(remoteExecution, client.RemoteExecution);
		Assert.Same(debugger, client.Debugger);
		Assert.Same(hotkeys, client.Hotkeys);
		Assert.Same(timers, client.Timers);
		Assert.Same(speed, client.Speed);
		Assert.Same(hashing, client.Hashing);
		Assert.Same(dbvm, client.Dbvm);
	}

	private static T CreateProxy<T>()
		where T : class
	{
		return DispatchProxy.Create<T, ThrowingProxy>();
	}

	public class ThrowingProxy : DispatchProxy
	{
		protected override object? Invoke(MethodInfo? targetMethod, object?[]? args)
		{
			throw new NotSupportedException("The façade test must not invoke a composed service.");
		}
	}
}
