using CheatEngine.Client.Core.Infrastructure;
using CheatEngine.Client.Dispatching;
using CheatEngine.Client.Inspection;
using CheatEngine.Client.Lua;
using CheatEngine.Client.Memory;
using CheatEngine.Client.Processes;
using CheatEngine.Client.Runtime;
using CheatEngine.Client.Scanning;
using CheatEngine.Client.Tables;

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
}

/// <summary>Internal grouping of the façade services that describe the active runtime and its dispatch boundary.</summary>
internal sealed record CheatEngineClientRuntimeServices(
	ICheatEngineRuntime Runtime,
	ICheatEngineDispatcher Dispatcher);

/// <summary>Internal grouping of the independently consumable high-level Client domains.</summary>
internal sealed record CheatEngineClientDomainServices(
	IProcessClient Processes,
	IMemoryClient Memory,
	IPatternScanner Patterns,
	IValueScanner Scans,
	IInspectionClient Inspection,
	ITableClient Tables,
	ILuaClient Lua);
