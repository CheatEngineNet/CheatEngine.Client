using CheatEngine.Client.Allocations;
using CheatEngine.Client.Assembly;
using CheatEngine.Client.Core;
using CheatEngine.Client.Core.Dispatching;
using CheatEngine.Client.Core.Domains;
using CheatEngine.Client.Core.Domains.Allocations;
using CheatEngine.Client.Core.Domains.Assembly;
using CheatEngine.Client.Core.Domains.ValueScanning;
using CheatEngine.Client.Core.Infrastructure;
using CheatEngine.Client.Dispatching;
using CheatEngine.Client.Inspection;
using CheatEngine.Client.Lua;
using CheatEngine.Client.Memory;
using CheatEngine.Client.Processes;
using CheatEngine.Client.Runtime;
using CheatEngine.Client.Scanning;
using CheatEngine.Client.Tables;

using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace CheatEngine.Client.Extensions.DependencyInjection;

/// <summary>Registers the high-level Cheat Engine client without building a nested service provider.</summary>
/// <remarks>
///     This package is the composition layer of CheatEngine.Client.Hosting: <c>CheatEnginePluginBuilder</c> calls
///     <c>AddCheatEngineClient</c> once for each activation provider, which Hosting builds, validates and disposes around
///     one Cheat Engine enable epoch. Calling these methods on a collection whose provider Hosting does not own is not
///     supported in 1.0: the Client services would outlive or precede the SDK plugin context they capture.
/// </remarks>
public static class CheatEngineClientServiceCollectionExtensions
{
	/// <summary>Adds the client options, their validators, logging, and explicit Client service registrations.</summary>
	/// <remarks>No memory codec is registered: a codec is an application service passed with each codec request.</remarks>
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
		// The Core diagnostics of this activation log through the activation's own logger factory (audit ch.24); Core
		// itself references no logging assembly. The sink is a registration of its own, which the lifetime resolves.
		services.TryAddSingleton<LoggerCoreDiagnostics>(static serviceProvider =>
			new LoggerCoreDiagnostics(serviceProvider.GetRequiredService<ILoggerFactory>()));
		services.TryAddSingleton<CoreLifetime>(static serviceProvider =>
			CoreLifetime.Capture(serviceProvider.GetRequiredService<LoggerCoreDiagnostics>()));
		services.TryAddSingleton<ICheatEngineClientActivationCleanup>(static serviceProvider =>
			new CheatEngineClientActivationCleanup(serviceProvider.GetRequiredService<CoreLifetime>()));
		services.TryAddSingleton<CoreClientPolicy>(static serviceProvider =>
		{
			CheatEngineClientOptions options =
				serviceProvider.GetRequiredService<IOptions<CheatEngineClientOptions>>().Value;
			bool enableUnsafeLuaExecution = serviceProvider
				.GetService<UnsafeLuaExecutionRegistration>()?
				.IsEnabled == true;
			bool enableAutoAssemblerPatches = serviceProvider
				.GetService<AutoAssemblerPatchesRegistration>()?
				.IsEnabled == true;
			return new CoreClientPolicy(options.AllowedTableRoots, enableUnsafeLuaExecution,
				enableAutoAssemblerPatches);
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

		services.TryAddSingleton<LocalProcessHost>();
		services.TryAddSingleton<ProcessClient>(static serviceProvider =>
			new ProcessClient(
				serviceProvider.GetRequiredService<SdkMainThreadDispatcher>(),
				serviceProvider.GetRequiredService<LocalProcessHost>(),
				SdkRuntimeObservationPort.Instance,
				SdkProcessSelectionPort.Instance,
				serviceProvider.GetRequiredService<CoreLifetime>()));
		services.TryAddSingleton<IProcessClient>(static serviceProvider =>
			serviceProvider.GetRequiredService<ProcessClient>());

		services.TryAddSingleton<MemoryClient>(static serviceProvider =>
		{
			CheatEngineClientOptions options =
				serviceProvider.GetRequiredService<IOptions<CheatEngineClientOptions>>().Value;
			return new MemoryClient(
				serviceProvider.GetRequiredService<SdkMainThreadDispatcher>(),
				serviceProvider.GetRequiredService<CoreLifetime>(),
				options.MemoryResourceLimits);
		});
		services.TryAddSingleton<IMemoryClient>(static serviceProvider =>
			serviceProvider.GetRequiredService<MemoryClient>());

		services.TryAddSingleton<PatternScanner>(static serviceProvider =>
			new PatternScanner(serviceProvider.GetRequiredService<SdkMainThreadDispatcher>()));
		services.TryAddSingleton<IPatternScanner>(static serviceProvider =>
			serviceProvider.GetRequiredService<PatternScanner>());

		services.TryAddSingleton<ValueScanner>(static serviceProvider =>
			new ValueScanner(serviceProvider.GetRequiredService<SdkMainThreadDispatcher>(),
				serviceProvider.GetRequiredService<ProcessClient>()));
		services.TryAddSingleton<IValueScanner>(static serviceProvider =>
			serviceProvider.GetRequiredService<ValueScanner>());

		services.TryAddSingleton<AllocationClient>(static serviceProvider =>
			new AllocationClient(serviceProvider.GetRequiredService<SdkMainThreadDispatcher>(),
				serviceProvider.GetRequiredService<ProcessClient>()));
		services.TryAddSingleton<IAllocationClient>(static serviceProvider =>
			serviceProvider.GetRequiredService<AllocationClient>());

		services.TryAddSingleton<AssemblyClient>(static serviceProvider =>
		{
			CheatEngineClientOptions options =
				serviceProvider.GetRequiredService<IOptions<CheatEngineClientOptions>>().Value;
			return new AssemblyClient(
				serviceProvider.GetRequiredService<SdkMainThreadDispatcher>(),
				serviceProvider.GetRequiredService<CoreLifetime>(),
				options.MemoryResourceLimits);
		});
		services.TryAddSingleton<IAssemblyClient>(static serviceProvider =>
			serviceProvider.GetRequiredService<AssemblyClient>());

		services.TryAddSingleton<InspectionClient>(static serviceProvider =>
			new InspectionClient(
				serviceProvider.GetRequiredService<SdkMainThreadDispatcher>(),
				serviceProvider.GetRequiredService<CoreLifetime>()));
		services.TryAddSingleton<IInspectionClient>(static serviceProvider =>
			serviceProvider.GetRequiredService<InspectionClient>());

		services.TryAddSingleton<TableClient>(static serviceProvider =>
			new TableClient(
				serviceProvider.GetRequiredService<SdkMainThreadDispatcher>(),
				serviceProvider.GetRequiredService<CoreClientPolicy>(),
				lifetime: serviceProvider.GetRequiredService<CoreLifetime>()));
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
					assembly));
		});
		services.TryAddSingleton<ICheatEngineClient>(static serviceProvider =>
			serviceProvider.GetRequiredService<CheatEngineClient>());
	}
}
