using System.Runtime.ExceptionServices;

using CheatEngine.Client.Extensions.DependencyInjection;
using CheatEngine.Client.Modules;
using CheatEngine.Client.Results;
using CheatEngine.SDK.Hosting.Plugin;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace CheatEngine.Client.Hosting;

/// <summary>Base class that activates a scoped <see cref="ICheatEngineClient" /> for each Cheat Engine enable epoch.</summary>
/// <remarks>
///     The SDK constructs a plugin through a parameterless factory and reuses that instance across enable/disable cycles.
///     This base class therefore creates a fresh validated provider and scope only from <see cref="OnEnable" />, when the
///     SDK has attached Lua, and disposes them before the SDK detaches Lua in <see cref="OnDisable" />. It does not create
///     a
///     Generic Host, discover assemblies, retain a Lua state, or cross an asynchronous boundary.
/// </remarks>
public abstract class CheatEngineClientPlugin : CheatEnginePlugin
{
	private Activation? _activation;

	/// <summary>Initializes the SDK-loadable plugin base through its required public parameterless constructor.</summary>
	/// <remarks>
	///     The SDK creates the concrete plugin type through a public parameterless constructor. Keeping this base
	///     constructor public preserves the constructor chain required by that generated loading path.
	/// </remarks>
	public CheatEngineClientPlugin()
	{
	}

	/// <summary>Gets the client for the active enable epoch.</summary>
	/// <exception cref="CheatEngineClientLifecycleException">The plugin is not currently enabled.</exception>
	protected ICheatEngineClient Client => GetActiveClient();

	/// <summary>Adds application services, explicit Client modules, codecs, and configuration sources for one activation.</summary>
	/// <remarks>
	///     Do not build a provider here. The base class builds it after this method returns with scope and build validation
	///     enabled. Application services that use Client APIs should be scoped and receive their dependencies by constructor
	///     injection; the plugin itself is the one unavoidable composition boundary because SDK plugins use parameterless
	///     construction.
	/// </remarks>
	protected abstract void Configure(CheatEnginePluginBuilder builder);

	/// <summary>Runs after all registered Client modules have enabled successfully.</summary>
	/// <param name="client">The client for the current activation.</param>
	protected virtual void OnClientEnabled(ICheatEngineClient client)
	{
		ArgumentNullException.ThrowIfNull(client);
	}

	/// <summary>Runs before enabled Client modules are disabled in reverse registration order.</summary>
	/// <param name="client">The client for the current activation.</param>
	protected virtual void OnClientDisabling(ICheatEngineClient client)
	{
		ArgumentNullException.ThrowIfNull(client);
	}

	/// <inheritdoc />
	protected sealed override void OnEnable()
	{
		if (Volatile.Read(ref _activation) is not null)
		{
			throw new CheatEngineClientLifecycleException(
				"EnableClient",
				"The Cheat Engine client is already active for this plugin instance.");
		}

		CheatEnginePluginBuilder builder = new();
		Activation? activation = null;

		try
		{
			Configure(builder);
			activation = CreateActivation(builder);
			Volatile.Write(ref _activation, activation);

			activation.Lifecycle.Enable(OnClientEnabled);
			ClientHostingLog.ActivationEnabled(activation.Logger, activation.Client.Epoch);
		}
		catch (Exception enableFailure)
		{
			if (activation is null)
			{
				builder.ReleaseConfiguration();
				throw;
			}

			Interlocked.CompareExchange(ref _activation, null, activation);
			ClientHostingLog.ActivationRollingBack(activation.Logger, activation.Client.Epoch);
			RethrowAfterCleanup(enableFailure,
				CleanupActivation(activation));
		}
	}

	/// <inheritdoc />
	protected sealed override void OnDisable()
	{
		Activation? activation = Interlocked.Exchange(ref _activation, null);
		if (activation is null)
		{
			return;
		}

		ClientHostingLog.ActivationDisabling(activation.Logger, activation.Client.Epoch);
		List<Exception> failures = CleanupActivation(activation);
		if (failures.Count > 0)
		{
			ClientHostingLog.ActivationCleanupFailed(activation.Logger, activation.Client.Epoch, failures.Count);
		}
		else
		{
			ClientHostingLog.ActivationDisabled(activation.Logger, activation.Client.Epoch);
		}

		ThrowCleanupFailures(failures);
	}

	private static Activation CreateActivation(CheatEnginePluginBuilder builder)
	{
		ServiceProvider? provider = null;
		IServiceScope? scope = null;

		try
		{
			provider = builder.BuildServiceProvider();
			scope = provider.CreateScope();

			// IOptions<T>.Value invokes the generated validator in plugin hosts that do not run Generic Host startup.
			_ = scope.ServiceProvider.GetRequiredService<IOptions<CheatEngineClientOptions>>().Value;
			ICheatEngineClient client = scope.ServiceProvider.GetRequiredService<ICheatEngineClient>();
			ICheatEngineClientModule[] modules = GetModules(scope.ServiceProvider);
			ILogger<CheatEngineClientPlugin> logger =
				scope.ServiceProvider.GetRequiredService<ILogger<CheatEngineClientPlugin>>();
			ICheatEngineClientActivationCleanup cleanup =
				scope.ServiceProvider.GetRequiredService<ICheatEngineClientActivationCleanup>();

			return new Activation(builder, provider, scope, client, modules, logger, cleanup);
		}
		catch
		{
			scope?.Dispose();
			provider?.Dispose();
			builder.ReleaseConfiguration();
			throw;
		}
	}

	private static ICheatEngineClientModule[] GetModules(IServiceProvider services)
	{
		List<ICheatEngineClientModule> result = new();
		foreach (ICheatEngineClientModule module in services.GetServices<ICheatEngineClientModule>())
		{
			result.Add(module);
		}

		return result.ToArray();
	}

	private static void RethrowAfterCleanup(Exception enableFailure, List<Exception> cleanupFailures)
	{
		if (cleanupFailures.Count == 0)
		{
			ExceptionDispatchInfo.Capture(enableFailure).Throw();
		}

		cleanupFailures.Insert(0, enableFailure);
		throw new AggregateException("Client activation failed and rollback encountered additional failures.",
			cleanupFailures);
	}

	private List<Exception> CleanupActivation(Activation activation)
	{
		List<Exception> failures = new();
		try
		{
			using (activation.Cleanup.EnterCleanupScope())
			{
				failures.AddRange(activation.Lifecycle.Cleanup(OnClientDisabling));
				try
				{
					activation.Cleanup.DrainOwnedResourcesForDisable();
				}
				catch (Exception exception)
				{
					failures.Add(exception);
				}
			}
		}
		catch (Exception exception)
		{
			failures.Add(exception);
		}

		try
		{
			activation.Scope.Dispose();
		}
		catch (Exception exception)
		{
			failures.Add(exception);
		}

		try
		{
			activation.Provider.Dispose();
		}
		catch (Exception exception)
		{
			failures.Add(exception);
		}

		try
		{
			activation.Builder.ReleaseConfiguration();
		}
		catch (Exception exception)
		{
			failures.Add(exception);
		}

		return failures;
	}

	private static void ThrowCleanupFailures(List<Exception> failures)
	{
		if (failures.Count == 0)
		{
			return;
		}

		if (failures.Count == 1)
		{
			ExceptionDispatchInfo.Capture(failures[0]).Throw();
		}

		throw new AggregateException("Client deactivation encountered one or more cleanup failures.", failures);
	}

	private ICheatEngineClient GetActiveClient()
	{
		Activation? activation = Volatile.Read(ref _activation);
		if (activation is not null)
		{
			return activation.Client;
		}

		throw new CheatEngineClientLifecycleException(
			"GetClient",
			"The Cheat Engine client is available only while the plugin is enabled.");
	}

	private sealed class Activation
	{
		internal Activation(
			CheatEnginePluginBuilder builder,
			ServiceProvider provider,
			IServiceScope scope,
			ICheatEngineClient client,
			ICheatEngineClientModule[] modules,
			ILogger logger,
			ICheatEngineClientActivationCleanup cleanup)
		{
			Builder = builder;
			Provider = provider;
			Scope = scope;
			Client = client;
			Lifecycle = new ClientActivationLifecycle(client, modules);
			Logger = logger;
			Cleanup = cleanup;
		}

		internal CheatEnginePluginBuilder Builder
		{
			get;
		}

		internal ServiceProvider Provider
		{
			get;
		}

		internal IServiceScope Scope
		{
			get;
		}

		internal ICheatEngineClient Client
		{
			get;
		}

		internal ClientActivationLifecycle Lifecycle
		{
			get;
		}

		internal ILogger Logger
		{
			get;
		}

		internal ICheatEngineClientActivationCleanup Cleanup
		{
			get;
		}
	}
}
