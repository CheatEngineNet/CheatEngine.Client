using System.Reflection;
using System.Runtime.ExceptionServices;

using CheatEngine.Client.Core.Infrastructure;
using CheatEngine.Client.Extensions.DependencyInjection;
using CheatEngine.Client.Modules;
using CheatEngine.Client.Results;
using CheatEngine.SDK.Hosting.Plugin;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace CheatEngine.Client.Hosting;

/// <summary>
///     Base class that activates an activation-owned <see cref="ICheatEngineClient" /> for each Cheat Engine enable
///     epoch.
/// </summary>
/// <remarks>
///     <para>
///         The SDK constructs a plugin through a parameterless factory and reuses that instance across enable/disable
///         cycles. This base class therefore creates a fresh validated provider and scope only from
///         <see cref="OnEnable" />, when the SDK has attached Lua, and disposes them before the SDK detaches Lua in
///         <see cref="OnDisable" />. It does not create a Generic Host, discover assemblies, retain a Lua state, or cross
///         an asynchronous boundary.
///     </para>
///     <para>
///         <b>Raw SDK escape hatch.</b> A derived plugin also inherits CheatEngine.SDK's <c>protected static</c>
///         <c>CheatEnginePlugin.Context</c>, the raw SDK plugin context of the current enable (plugin id, SDK epoch, main
///         thread, shutdown token). It is outside every guarantee of the Client: activation epochs, main-thread dispatch,
///         failure classification, resource ownership and release, and redaction. Code that uses it, or any other
///         CheatEngine.SDK API directly, follows the CheatEngine.SDK contract instead. Use <see cref="GetRequiredClient" />
///         and the <see cref="ICheatEngineClient" /> it returns for Client work.
///     </para>
/// </remarks>
public abstract class CheatEngineClientPlugin : CheatEnginePlugin
{
	private Activation? _activation;

	/// <summary>Initializes the SDK-loadable plugin base for construction by a concrete plugin.</summary>
	/// <remarks>
	///     The SDK creates the concrete plugin type through a public parameterless constructor. A concrete derived class
	///     can use its implicit public parameterless constructor to call this protected base constructor.
	/// </remarks>
	protected CheatEngineClientPlugin()
	{
	}

	/// <summary>Gets the client for the active enable epoch.</summary>
	/// <returns>The client of the current activation.</returns>
	/// <exception cref="CheatEngineInvalidStateException">The plugin is not currently enabled.</exception>
	protected ICheatEngineClient GetRequiredClient()
	{
		return GetActiveClient();
	}

	/// <summary>Adds application services, explicit Client modules, logging, and configuration sources for one activation.</summary>
	/// <param name="builder">The registrations, configuration and logging of the activation being enabled.</param>
	/// <remarks>
	///     Do not build a provider here. The base class builds it after this method returns with scope and build validation
	///     enabled. Application services that use Client APIs should be scoped and receive their dependencies by constructor
	///     injection; the plugin itself is the one unavoidable composition boundary because SDK plugins use parameterless
	///     construction. A memory codec is an ordinary application service: register it in
	///     <see cref="CheatEnginePluginBuilder.Services" /> and pass it with each codec request.
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
	/// <remarks>
	///     When the plugin disables, it runs on Cheat Engine's main thread after
	///     <see cref="ICheatEngineClient.Stopping" /> was cancelled and before the activation releases what it owns.
	///     A call it makes on that thread can still read and change existing state and release leases, but cannot
	///     create a lease, attach, run Lua, instructions or Auto Assembler scripts, or load or save a table: see the
	///     deactivation callbacks of <see cref="ICheatEngineClient" />.
	/// </remarks>
	protected virtual void OnClientDisabling(ICheatEngineClient client)
	{
		ArgumentNullException.ThrowIfNull(client);
	}

	/// <summary>
	///     Creates and publishes the activation of this enable: calls <see cref="Configure" />, builds and validates
	///     the activation provider and scope, enables the Client modules in registration order, then calls
	///     <see cref="OnClientEnabled" />.
	/// </summary>
	/// <remarks>
	///     A failure at any step rolls the partial activation back before the failure is rethrown, so no activation is
	///     published.
	/// </remarks>
	/// <exception cref="CheatEngineInvalidStateException">
	///     A Client activation of this plugin instance is already active, or Cheat Engine has not enabled a current
	///     plugin context for it.
	/// </exception>
	/// <exception cref="OptionsValidationException">
	///     The <see cref="CheatEngineClientOptions" /> that <see cref="Configure" /> bound or configured are invalid,
	///     for example an <see cref="CheatEngineClientOptions.AllowedTableRoots" /> entry that is not a fully qualified
	///     path or a <see cref="CheatEngineClientOptions.MemoryResourceLimits" /> value out of range. The partial
	///     activation is rolled back first.
	/// </exception>
	/// <exception cref="AggregateException">
	///     The activation failed and its rollback failed too: the activation failure comes first, then every rollback
	///     failure. A failure whose rollback succeeded is rethrown unchanged.
	/// </exception>
	protected sealed override void OnEnable()
	{
		if (Volatile.Read(ref _activation) is not null)
		{
			throw new CheatEngineFailure(CheatEngineFailureKind.InvalidState, "Client.Activate",
				"The Cheat Engine client is already active for this plugin instance.", null,
				CheatEngineHostEffect.NotStarted).ToException();
		}

		CheatEnginePluginBuilder builder = new(GetType().Assembly);
		ActivationConstruction construction = new(builder);
		Activation? activation = null;

		try
		{
			Configure(builder);
			activation = CreateActivation(construction);
			Volatile.Write(ref _activation, activation);

			LogIdentification(activation, GetType());
			activation.Lifecycle.Enable(OnClientEnabled);
			SafeLog(activation, static (logger, epoch) => ClientHostingLog.ActivationEnabled(logger, epoch));
		}
		catch (Exception enableFailure)
		{
			if (activation is null)
			{
				RethrowAfterCleanup(enableFailure, CleanupUnpublishedActivation(construction));
			}
			else
			{
				Interlocked.CompareExchange(ref _activation, null, activation);
				SafeLog(activation, static (logger, epoch) => ClientHostingLog.ActivationRollingBack(logger, epoch));
				RethrowAfterCleanup(enableFailure,
					CleanupActivation(activation));
			}
		}
	}

	/// <summary>
	///     Closes the activation of this enable, when there is one: calls <see cref="OnClientDisabling" />, disables
	///     the enabled modules in reverse order and releases the Client-owned Cheat Engine resources while Lua is
	///     attached, then disposes the activation scope, the provider and the configuration.
	/// </summary>
	/// <remarks>
	///     Every stage is attempted even after an earlier one failed. A lease that the application did not release
	///     completely is reported here, never to the code that released it.
	/// </remarks>
	/// <exception cref="CheatEngineClientException">
	///     A Client-owned resource was not released completely, and nothing else failed: the failure has the host
	///     effect <see cref="CheatEngineHostEffect.CleanupUnconfirmed" />.
	/// </exception>
	/// <exception cref="AggregateException">
	///     Several cleanup failures, one per failed stage or incomplete release; a single failure of another stage (a
	///     module callback, for example) is rethrown unchanged.
	/// </exception>
	protected sealed override void OnDisable()
	{
		Activation? activation = Interlocked.Exchange(ref _activation, null);
		if (activation is null)
		{
			return;
		}

		SafeLog(activation, static (logger, epoch) => ClientHostingLog.ActivationDisabling(logger, epoch));
		List<Exception> failures = CleanupActivation(activation);
		if (failures.Count > 0)
		{
			int failureCount = failures.Count;
			SafeLog(activation.Logger, (Epoch: activation.Client.Epoch, Count: failureCount),
				static (logger, state) => ClientHostingLog.ActivationCleanupFailed(logger, state.Epoch, state.Count));
		}
		else
		{
			SafeLog(activation, static (logger, epoch) => ClientHostingLog.ActivationDisabled(logger, epoch));
		}

		ThrowCleanupFailures(failures);
	}

	/// <summary>Builds and resolves the complete activation graph before publishing it to the plugin instance.</summary>
	/// <remarks>
	///     The activation is not returned until options validation, the Client facade, modules, logging, and cleanup support
	///     have all resolved from one scope. The outer activation transaction records each acquired component before the
	///     next step, so its failure-independent rollback can release a partial graph without invoking lifecycle callbacks.
	/// </remarks>
	private static Activation CreateActivation(ActivationConstruction construction)
	{
		ServiceProvider provider = construction.Builder.BuildServiceProvider();
		construction.Provider = provider;
		IServiceScope scope = provider.CreateScope();
		construction.Scope = scope;

		// IOptions<T>.Value invokes the generated validator in plugin hosts that do not run Generic Host startup.
		_ = scope.ServiceProvider.GetRequiredService<IOptions<CheatEngineClientOptions>>().Value;
		ICheatEngineClient client = scope.ServiceProvider.GetRequiredService<ICheatEngineClient>();
		ICheatEngineClientModule[] modules = GetModules(scope.ServiceProvider);
		ILogger<CheatEngineClientPlugin> logger =
			scope.ServiceProvider.GetRequiredService<ILogger<CheatEngineClientPlugin>>();
		ICheatEngineClientActivationCleanup cleanup =
			scope.ServiceProvider.GetRequiredService<ICheatEngineClientActivationCleanup>();

		return new Activation(construction.Builder, provider, scope, client, modules, logger, cleanup);
	}

	private static ICheatEngineClientModule[] GetModules(IServiceProvider services)
	{
		List<ICheatEngineClientModule> result = [];
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

	/// <summary>Releases a partial activation without invoking callbacks that were never admitted.</summary>
	/// <remarks>
	///     Provider construction happens before scope construction, and configuration outlives both. Each completed
	///     construction stage is therefore released once in reverse order. Cleanup failures are recorded instead of
	///     interrupting the remaining stages, so the original construction failure remains the first reported failure.
	/// </remarks>
	private static List<Exception> CleanupUnpublishedActivation(ActivationConstruction construction)
	{
		List<Exception> failures = [];
		if (construction.Scope is not null)
		{
			TryCleanup(failures, construction.Scope.Dispose);
		}

		if (construction.Provider is not null)
		{
			TryCleanup(failures, construction.Provider.Dispose);
		}

		TryCleanup(failures, construction.Builder.ReleaseConfiguration);
		return failures;
	}

	/// <summary>Closes an activation in the only safe disposal order and collects every cleanup failure.</summary>
	/// <remarks>
	///     Module callbacks and Client-owned Cheat Engine resources run while the SDK context is valid. The activation scope,
	///     root provider, and configuration are then released in that order. Each stage is attempted even after an earlier
	///     stage fails, allowing the caller to report one aggregate failure only after all owned resources had a cleanup
	///     opportunity. Each failed stage is logged with its stable stage name and the exception type name only (Q43,
	///     Q46); a throwing logging provider cannot abort the remaining stages. Between the Client-owned resources and the
	///     scope, the external Lua state reset warning is logged when CheatEngine.SDK reports one.
	/// </remarks>
	private List<Exception> CleanupActivation(Activation activation)
	{
		CleanupReport report = new(activation.Logger, activation.Client.Epoch);
		try
		{
			report.Attempt(CleanupStage.CleanupScope);
			using (activation.Cleanup.EnterCleanupScope())
			{
				report.Record(CleanupStage.ModuleCallbacks, activation.Lifecycle.Cleanup(OnClientDisabling));
				report.Run(CleanupStage.ClientResources, activation.Cleanup.DrainOwnedResourcesForDisable);
			}
		}
		catch (Exception exception)
		{
			report.Fail(CleanupStage.CleanupScope, exception);
		}

		LogExternalLuaStateReset(activation);
		report.Run(CleanupStage.Scope, activation.Scope.Dispose);
		report.Run(CleanupStage.Provider, activation.Provider.Dispose);
		report.Run(CleanupStage.Configuration, activation.Builder.ReleaseConfiguration);
		report.Complete();
		return report.Failures;
	}

	/// <summary>
	///     Logs the identity of this activation once per enable (EventId 20): the Client version, the consumed and loaded
	///     CheatEngine.SDK identity, how the loaded one relates to the consumed one, and the supported host profile.
	///     Assembly metadata only, never a path or a Lua call.
	/// </summary>
	private static void LogIdentification(Activation activation, Type pluginType)
	{
		SafeLog(activation.Logger, (Epoch: activation.Client.Epoch, PluginType: pluginType), static (logger, state) =>
		{
			const string NotDeclared = "unknown";
			ConsumedSdkIdentity identity = ConsumedSdkIdentity.Current;
			string clientVersion = typeof(CheatEngineClientPlugin).Assembly
				.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion ?? NotDeclared;
			ClientHostingLog.ActivationIdentified(logger, state.Epoch, state.PluginType.FullName ?? state.PluginType.Name,
				clientVersion, identity.Version ?? NotDeclared, identity.ContentHashSha512 ?? NotDeclared,
				identity.LoadedInformationalVersion ?? NotDeclared, identity.IdentityLabel,
				identity.PackageGate.State, ConsumedSdkIdentity.SupportedHostProfileId);
		});
	}

	/// <summary>
	///     Warns (event 8) when CheatEngine.SDK detected an external Lua state reset during this activation (A8).
	/// </summary>
	/// <remarks>
	///     <para>
	///         The SDK's fact is sticky until the next enable. It is read once per cleanup, after the module callbacks and
	///         the Client-owned resource releases, whose Lua admissions are the last ones the activation makes and can be
	///         the ones that detect the reset, and before the scope and provider that own the logger are disposed. With
	///         the reset, those Lua-bound releases were refused rather than made into the replacement state.
	///     </para>
	///     <para>
	///         The read goes through the cleanup bridge, a lock-free read of the SDK's flag, never through the runtime
	///         snapshot: once CheatEngine.SDK detected the reset it refuses every Lua admission, the snapshot's included,
	///         so a snapshot can never report the reset. The read is diagnostics only: a read or a logging provider that
	///         throws changes no cleanup outcome.
	///     </para>
	/// </remarks>
	private static void LogExternalLuaStateReset(Activation activation)
	{
		try
		{
			if (activation.Cleanup.ExternalLuaStateResetDetected)
			{
				SafeLog(activation, static (logger, epoch) => ClientHostingLog.ExternalLuaStateResetDetected(logger, epoch));
			}
		}
		catch (Exception)
		{
			// Deliberately ignored: diagnostics must never change the lifecycle outcome.
		}
	}

	/// <summary>Writes a lifecycle event without letting a logging provider fault escape the plugin callback.</summary>
	private static void SafeLog(Activation activation, Action<ILogger, long> log)
	{
		SafeLog(activation.Logger, activation.Client.Epoch, log);
	}

	/// <summary>Writes a lifecycle event without letting a logging provider fault escape the plugin callback.</summary>
	/// <remarks>
	///     Logging is best effort: a provider that throws must neither abort enable/disable cleanup nor cross the Cheat
	///     Engine plugin callback (audit ch.24, "handlers never re-enter or throw across the ABI callback").
	/// </remarks>
	private static void SafeLog<TState>(ILogger logger, TState state, Action<ILogger, TState> log)
	{
		try
		{
			log(logger, state);
		}
		catch (Exception)
		{
			// Deliberately ignored: diagnostics must never change the lifecycle outcome.
		}
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

	private static void TryCleanup(List<Exception> failures, Action cleanup)
	{
		try
		{
			cleanup();
		}
		catch (Exception exception)
		{
			failures.Add(exception);
		}
	}

	private ICheatEngineClient GetActiveClient()
	{
		Activation? activation = Volatile.Read(ref _activation);
		if (activation is not null)
		{
			return activation.Client;
		}

		throw new CheatEngineFailure(CheatEngineFailureKind.InvalidState, "Client.GetRequiredClient",
			"The Cheat Engine client is available only while the plugin is enabled.", null,
			CheatEngineHostEffect.NotStarted).ToException();
	}

	/// <summary>The stable, data-free names of the activation cleanup stages, in execution order.</summary>
	private enum CleanupStage
	{
		CleanupScope,
		ModuleCallbacks,
		ClientResources,
		Scope,
		Provider,
		Configuration
	}

	/// <summary>Collects cleanup failures per stage and logs each failed stage without user data.</summary>
	private sealed class CleanupReport(ILogger logger, long epoch)
	{
		private const int StageCount = (int) CleanupStage.Configuration + 1;

		private readonly bool[] _attempted = new bool[StageCount];
		private readonly bool[] _failed = new bool[StageCount];

		internal List<Exception> Failures
		{
			get;
		} = [];

		internal void Attempt(CleanupStage stage)
		{
			_attempted[(int) stage] = true;
		}

		internal void Run(CleanupStage stage, Action cleanup)
		{
			Attempt(stage);
			try
			{
				cleanup();
			}
			catch (Exception exception)
			{
				Fail(stage, exception);
			}
		}

		internal void Record(CleanupStage stage, List<Exception> failures)
		{
			Attempt(stage);
			foreach (Exception failure in failures)
			{
				Fail(stage, failure);
			}
		}

		internal void Fail(CleanupStage stage, Exception exception)
		{
			Failures.Add(exception);
			_failed[(int) stage] = true;
			string exceptionType = exception.GetType().FullName ?? exception.GetType().Name;
			SafeLog(logger, (Epoch: epoch, Stage: GetName(stage), ExceptionType: exceptionType),
				static (target, state) =>
					ClientHostingLog.ActivationCleanupStageFailed(target, state.Epoch, state.Stage, state.ExceptionType));
		}

		internal void Complete()
		{
			SafeLog(logger, (Epoch: epoch, Attempted: Count(_attempted), Failed: Count(_failed)),
				static (target, state) =>
					ClientHostingLog.ActivationCleanupCompleted(target, state.Epoch, state.Attempted, state.Failed));
		}

		private static int Count(bool[] stages)
		{
			int count = 0;
			foreach (bool stage in stages)
			{
				if (stage)
				{
					count++;
				}
			}

			return count;
		}

		private static string GetName(CleanupStage stage)
		{
			return stage switch
			{
				CleanupStage.CleanupScope => "CleanupScope",
				CleanupStage.ModuleCallbacks => "ModuleCallbacks",
				CleanupStage.ClientResources => "ClientResources",
				CleanupStage.Scope => "Scope",
				CleanupStage.Provider => "Provider",
				CleanupStage.Configuration => "Configuration",
				_ => "Unknown"
			};
		}
	}

	private sealed class ActivationConstruction(CheatEnginePluginBuilder builder)
	{
		internal CheatEnginePluginBuilder Builder
		{
			get;
		} = builder;

		internal ServiceProvider? Provider
		{
			get;
			set;
		}

		internal IServiceScope? Scope
		{
			get;
			set;
		}
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
