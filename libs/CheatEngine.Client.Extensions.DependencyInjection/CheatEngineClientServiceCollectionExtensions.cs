using CheatEngine.Client.Allocations;
using CheatEngine.Client.Assembly;
using CheatEngine.Client.Core;
using CheatEngine.Client.Core.Dispatching;
using CheatEngine.Client.Core.Domains;
using CheatEngine.Client.Core.Domains.Allocations;
using CheatEngine.Client.Core.Domains.Assembly;
using CheatEngine.Client.Core.Domains.Dbvm;
using CheatEngine.Client.Core.Domains.Debugger;
using CheatEngine.Client.Core.Domains.Hashing;
using CheatEngine.Client.Core.Domains.Hotkeys;
using CheatEngine.Client.Core.Domains.RemoteExecution;
using CheatEngine.Client.Core.Domains.Speed;
using CheatEngine.Client.Core.Domains.Timers;
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

using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;

namespace CheatEngine.Client.Extensions.DependencyInjection;

/// <summary>Registers the high-level Cheat Engine client without building a nested service provider.</summary>
public static class CheatEngineClientServiceCollectionExtensions
{
	/// <summary>Adds the client options, deterministic memory codecs, and explicit Client service registrations.</summary>
	public static CheatEngineClientBuilder AddCheatEngineClient(this IServiceCollection services)
	{
		ArgumentNullException.ThrowIfNull(services);

		services.AddLogging();
		services.AddOptions<CheatEngineClientOptions>();
		services.TryAddEnumerable(
			ServiceDescriptor
				.Singleton<IValidateOptions<CheatEngineClientOptions>, ValidateCheatEngineClientOptions>());
		services.TryAddEnumerable(
			ServiceDescriptor
				.Singleton<IValidateOptions<CheatEngineClientOptions>, CheatEngineClientOptionsSemanticValidator>());
		DefaultMemoryCodecs.Add(services);
		AddCoreServices(services);

		return new CheatEngineClientBuilder(services);
	}

	/// <summary>Adds the client and binds its options from the default client section.</summary>
	public static CheatEngineClientBuilder AddCheatEngineClient(
		this IServiceCollection services,
		IConfiguration configuration)
	{
		ArgumentNullException.ThrowIfNull(configuration);
		return services.AddCheatEngineClient().BindConfiguration(configuration);
	}

	/// <summary>Adds the client and binds its options from an explicitly selected configuration section.</summary>
	public static CheatEngineClientBuilder AddCheatEngineClient(
		this IServiceCollection services,
		IConfigurationSection section)
	{
		ArgumentNullException.ThrowIfNull(section);
		return services.AddCheatEngineClient().BindConfiguration(section);
	}

	/// <summary>Adds the client and applies a programmatic options configuration.</summary>
	public static CheatEngineClientBuilder AddCheatEngineClient(
		this IServiceCollection services,
		Action<CheatEngineClientOptions> configure)
	{
		ArgumentNullException.ThrowIfNull(configure);
		return services.AddCheatEngineClient().Configure(configure);
	}

	private static void AddCoreServices(IServiceCollection services)
	{
		// Every descriptor is a direct construction path. The client never scans assemblies, resolves arbitrary types,
		// or creates a nested provider; Core internals are visible only to this composition assembly.
		services.TryAddSingleton<CoreLifetime>(static _ => CoreLifetime.Capture());
		services.TryAddSingleton<ICheatEngineClientActivationCleanup>(static serviceProvider =>
			new CheatEngineClientActivationCleanup(serviceProvider.GetRequiredService<CoreLifetime>()));
		services.TryAddSingleton<CoreClientPolicy>(static serviceProvider =>
		{
			CheatEngineClientOptions options =
				serviceProvider.GetRequiredService<IOptions<CheatEngineClientOptions>>().Value;
			string[] allowedTableRoots = options.AllowedTableRoots
			                             ?? throw new InvalidOperationException(
				                             "AllowedTableRoots must be validated before the Client policy is created.");
			bool enableUnsafeLuaExecution = serviceProvider
				.GetService<UnsafeLuaExecutionRegistration>()?
				.IsEnabled == true;
			return new CoreClientPolicy(allowedTableRoots, enableUnsafeLuaExecution);
		});

		services.TryAddSingleton<SdkMainThreadDispatcher>(static serviceProvider =>
			new SdkMainThreadDispatcher(serviceProvider.GetRequiredService<CoreLifetime>()));
		services.TryAddSingleton<ICheatEngineDispatcher>(static serviceProvider =>
			serviceProvider.GetRequiredService<SdkMainThreadDispatcher>());

		services.TryAddSingleton<RuntimeClient>(static serviceProvider =>
			new RuntimeClient(
				serviceProvider.GetRequiredService<SdkMainThreadDispatcher>(),
				serviceProvider.GetRequiredService<CoreLifetime>(),
				serviceProvider.GetRequiredService<CoreClientPolicy>()));
		services.TryAddSingleton<ICheatEngineRuntime>(static serviceProvider =>
			serviceProvider.GetRequiredService<RuntimeClient>());

		services.TryAddSingleton<ProcessClient>(static serviceProvider =>
			new ProcessClient(
				serviceProvider.GetRequiredService<SdkMainThreadDispatcher>(),
				serviceProvider.GetRequiredService<CoreLifetime>()));
		services.TryAddSingleton<IProcessClient>(static serviceProvider =>
			serviceProvider.GetRequiredService<ProcessClient>());

		services.TryAddSingleton<MemoryClient>(static serviceProvider =>
			new MemoryClient(serviceProvider.GetRequiredService<SdkMainThreadDispatcher>()));
		services.TryAddSingleton<IMemoryClient>(static serviceProvider =>
			serviceProvider.GetRequiredService<MemoryClient>());

		services.TryAddSingleton<PatternScanner>(static serviceProvider =>
			new PatternScanner(serviceProvider.GetRequiredService<SdkMainThreadDispatcher>()));
		services.TryAddSingleton<IPatternScanner>(static serviceProvider =>
			serviceProvider.GetRequiredService<PatternScanner>());

		services.TryAddSingleton<IValueScanner, UnavailableValueScanner>();
		services.TryAddSingleton<IAllocationClient, UnavailableAllocationClient>();
		services.TryAddSingleton<IAssemblyClient, UnavailableAssemblyClient>();
		services.TryAddSingleton<IRemoteExecutionClient, UnavailableRemoteExecutionClient>();
		services.TryAddSingleton<IDebuggerClient, UnavailableDebuggerClient>();
		services.TryAddSingleton<IHotkeyClient, UnavailableHotkeyClient>();
		services.TryAddSingleton<ITimerClient, UnavailableTimerClient>();
		services.TryAddSingleton<ISpeedClient, UnavailableSpeedClient>();
		services.TryAddSingleton<IHashingClient, UnavailableHashingClient>();
		services.TryAddSingleton<IDbvmClient, UnavailableDbvmClient>();

		services.TryAddSingleton<InspectionClient>(static serviceProvider =>
			new InspectionClient(
				serviceProvider.GetRequiredService<SdkMainThreadDispatcher>(),
				serviceProvider.GetRequiredService<CoreLifetime>()));
		services.TryAddSingleton<IInspectionClient>(static serviceProvider =>
			serviceProvider.GetRequiredService<InspectionClient>());

		services.TryAddSingleton<TableClient>(static serviceProvider =>
			new TableClient(
				serviceProvider.GetRequiredService<SdkMainThreadDispatcher>(),
				serviceProvider.GetRequiredService<CoreClientPolicy>()));
		services.TryAddSingleton<ITableClient>(static serviceProvider =>
			serviceProvider.GetRequiredService<TableClient>());

		services.TryAddSingleton<LuaClient>(static serviceProvider =>
			new LuaClient(
				serviceProvider.GetRequiredService<SdkMainThreadDispatcher>(),
				serviceProvider.GetRequiredService<CoreLifetime>()));
		services.TryAddSingleton<ILuaClient>(static serviceProvider =>
			serviceProvider.GetRequiredService<LuaClient>());

		services.TryAddSingleton<CheatEngineClient>(static serviceProvider =>
		{
			CoreLifetime lifetime = serviceProvider.GetRequiredService<CoreLifetime>();
			ICheatEngineRuntime runtime = serviceProvider.GetRequiredService<ICheatEngineRuntime>();
			ICheatEngineDispatcher dispatcher = serviceProvider.GetRequiredService<ICheatEngineDispatcher>();
			IProcessClient processes = serviceProvider.GetRequiredService<IProcessClient>();
			IMemoryClient memory = serviceProvider.GetRequiredService<IMemoryClient>();
			IPatternScanner patterns = serviceProvider.GetRequiredService<IPatternScanner>();
			IValueScanner scans = serviceProvider.GetRequiredService<IValueScanner>();
			IInspectionClient inspection = serviceProvider.GetRequiredService<IInspectionClient>();
			ITableClient tables = serviceProvider.GetRequiredService<ITableClient>();
			ILuaClient lua = serviceProvider.GetRequiredService<ILuaClient>();
			IAllocationClient allocations = serviceProvider.GetRequiredService<IAllocationClient>();
			IAssemblyClient assembly = serviceProvider.GetRequiredService<IAssemblyClient>();
			IRemoteExecutionClient remoteExecution = serviceProvider.GetRequiredService<IRemoteExecutionClient>();
			IDebuggerClient debugger = serviceProvider.GetRequiredService<IDebuggerClient>();
			IHotkeyClient hotkeys = serviceProvider.GetRequiredService<IHotkeyClient>();
			ITimerClient timers = serviceProvider.GetRequiredService<ITimerClient>();
			ISpeedClient speed = serviceProvider.GetRequiredService<ISpeedClient>();
			IHashingClient hashing = serviceProvider.GetRequiredService<IHashingClient>();
			IDbvmClient dbvm = serviceProvider.GetRequiredService<IDbvmClient>();

			return new CheatEngineClient(
				lifetime,
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
		});
		services.TryAddSingleton<ICheatEngineClient>(static serviceProvider =>
			serviceProvider.GetRequiredService<CheatEngineClient>());
	}
}
