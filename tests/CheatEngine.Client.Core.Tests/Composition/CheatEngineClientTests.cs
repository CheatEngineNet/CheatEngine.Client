#pragma warning disable CECLIENT5003 // The composition tests cover the experimental instruction client property.

using System.Reflection;

using CheatEngine.Client.Allocations;
using CheatEngine.Client.Assembly;
using CheatEngine.Client.Core.Tests.TestSupport;
using CheatEngine.Client.Dispatching;
using CheatEngine.Client.Inspection;
using CheatEngine.Client.Lua;
using CheatEngine.Client.Memory;
using CheatEngine.Client.Processes;
using CheatEngine.Client.Runtime;
using CheatEngine.Client.Scanning;
using CheatEngine.Client.Tables;

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
				assembly));

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
