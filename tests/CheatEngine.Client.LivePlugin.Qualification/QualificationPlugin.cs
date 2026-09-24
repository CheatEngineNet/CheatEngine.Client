using System.Diagnostics.CodeAnalysis;

using CheatEngine.Client;
using CheatEngine.Client.Extensions.DependencyInjection;
using CheatEngine.Client.Hosting;
using CheatEngine.Client.Lua;
using CheatEngine.Client.Memory;
using CheatEngine.Client.Modules;
using CheatEngine.Client.Scanning;
using CheatEngine.SDK.Annotations.Plugin;

using LivePlugin.Qualification.Harness;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace LivePlugin.Qualification;

/// <summary>
///     The Client qualification harness: a real <see cref="CheatEngineClientPlugin" /> composed like an application, whose
///     Lua functions run one Client C3/C4 scenario each through the public Client API only. It evaluates the
///     qualification gate and the fault switch once per enable, registers the Q46 log sink, and records its own lifecycle
///     so that a re-enable can report what a failed enable or a faulty disable did.
/// </summary>
[CheatEnginePlugin(DisplayName)]
public sealed class QualificationPlugin : CheatEngineClientPlugin
{
	/// <summary>The name Cheat Engine shows in Edit &gt; Settings &gt; Plugins.</summary>
	internal const string DisplayName = "CheatEngine.Client Qualification Plugin";

	/// <summary>The folder the plugin was loaded from, where the runner writes the fault switch.</summary>
	[UnconditionalSuppressMessage("SingleFile", "IL3000:Avoid accessing Assembly file path when publishing as a single file",
		Justification = "The harness is a managed-hostfxr plugin loaded from its bundle folder and is never published as a single file; an empty location reads as no fault switch and no bridge.")]
	internal static string? PluginDirectory()
	{
		string location = typeof(QualificationPlugin).Assembly.Location;
		return location.Length == 0 ? null : Path.GetDirectoryName(location);
	}

	/// <inheritdoc />
	protected override void Configure(CheatEnginePluginBuilder builder)
	{
		ArgumentNullException.ThrowIfNull(builder);
		AuthorizationDecision authorization = QualificationAuthorization.Evaluate(QualificationEnvironment.Instance);
		FaultDecision fault = QualificationFaultSwitch.Read(PluginDirectory(), authorization,
			QualificationEnvironment.Instance);
		QualificationSession.BeginEnable(Context.PluginId, authorization);
		QualificationLedger.BeginEnable(fault);
		if (fault.Stage == FaultStage.Configure)
		{
			InvalidOperationException failure = new("The qualification fault switch selected Configure.");
			QualificationLedger.Record("configure.threw", failure);
			throw failure;
		}

		builder.Services.AddLogging(static logging => logging.AddProvider(QualificationSession.Logs));
		builder.Services.AddSingleton(fault);
		builder.Services.AddScoped<QualificationScopedResource>();
		builder.Client
			.AddLuaModule<QualificationLuaModule>()
			.AddModule<QualificationFirstModule>()
			.AddModule<QualificationFaultModule>()
			.AddModule<QualificationScenarioModule>()
			.AddModule<QualificationLastModule>();
	}

	/// <inheritdoc />
	protected override void OnClientEnabled(ICheatEngineClient client)
	{
		ArgumentNullException.ThrowIfNull(client);
		QualificationLedger.RecordActivated(client.Epoch);
	}
}

/// <summary>Declares the generated, activation-scoped Lua module of the harness functions.</summary>
[CheatEngineLuaModule(typeof(QualificationLuaFunctions), "client_qualification")]
internal sealed partial class QualificationLuaModule : ILuaModule;

/// <summary>The first module: entered first, disabled last, so it proves that cleanup continues past a faulty module.</summary>
internal sealed class QualificationFirstModule : ICheatEngineClientModule
{
	/// <inheritdoc />
	public void OnEnabled(ICheatEngineClient client)
	{
		ArgumentNullException.ThrowIfNull(client);
		QualificationLedger.Record("first.enabled");
	}

	/// <inheritdoc />
	public void OnDisabling(ICheatEngineClient client)
	{
		ArgumentNullException.ThrowIfNull(client);
		QualificationLedger.Record("first.disabling");
	}
}

/// <summary>
///     The module that throws where the fault switch says (Q06, Q43). It holds the activation-scoped resource, so the
///     resource is created with the activation and disposed with its scope.
/// </summary>
internal sealed class QualificationFaultModule(FaultDecision fault, QualificationScopedResource resource)
	: ICheatEngineClientModule
{
	/// <inheritdoc />
	public void OnEnabled(ICheatEngineClient client)
	{
		ArgumentNullException.ThrowIfNull(client);
		resource.Acquire();
		if (fault.FaultsEnable)
		{
			InvalidOperationException failure = new("The qualification fault switch selected OnEnabled.");
			QualificationLedger.Record("fault.enabling.threw", failure);
			throw failure;
		}

		QualificationLedger.Record("fault.enabled");
	}

	/// <inheritdoc />
	public void OnDisabling(ICheatEngineClient client)
	{
		ArgumentNullException.ThrowIfNull(client);
		if (fault.FaultsDisabling)
		{
			InvalidOperationException failure = new("The qualification fault switch selected OnDisabling.");
			QualificationLedger.Record("fault.disabling.threw", failure);
			throw failure;
		}

		QualificationLedger.Record("fault.disabling");
	}
}

/// <summary>Publishes the activation's Client to the Lua functions and withdraws it before the activation ends.</summary>
internal sealed class QualificationScenarioModule(IPatternScanOutcomeClient scans, IMemoryClient batches)
	: ICheatEngineClientModule
{
	/// <inheritdoc />
	public void OnEnabled(ICheatEngineClient client)
	{
		ArgumentNullException.ThrowIfNull(client);
		QualificationSession.Attach(client, scans, batches);
		QualificationLedger.Record("scenario.enabled");
	}

	/// <inheritdoc />
	public void OnDisabling(ICheatEngineClient client)
	{
		ArgumentNullException.ThrowIfNull(client);
		QualificationSession.Detach();
		QualificationLedger.Record("scenario.disabling");
	}
}

/// <summary>The last module: entered last, disabled first.</summary>
internal sealed class QualificationLastModule : ICheatEngineClientModule
{
	/// <inheritdoc />
	public void OnEnabled(ICheatEngineClient client)
	{
		ArgumentNullException.ThrowIfNull(client);
		QualificationLedger.Record("last.enabled");
	}

	/// <inheritdoc />
	public void OnDisabling(ICheatEngineClient client)
	{
		ArgumentNullException.ThrowIfNull(client);
		QualificationLedger.Record("last.disabling");
	}
}

/// <summary>An activation-scoped resource whose disposal throws when the fault switch selects the resource cleanup.</summary>
internal sealed class QualificationScopedResource(FaultDecision fault) : IDisposable
{
	private bool _acquired;
	private bool _disposed;

	/// <summary>Marks the resource as used by the activation.</summary>
	internal void Acquire()
	{
		_acquired = true;
		QualificationLedger.Record("resource.acquired");
	}

	/// <inheritdoc />
	public void Dispose()
	{
		if (_disposed)
		{
			return;
		}

		_disposed = true;
		if (fault.FaultsResourceCleanup)
		{
			InvalidOperationException failure = new("The qualification fault switch selected the resource cleanup.");
			QualificationLedger.Record("resource.dispose.threw", failure);
			throw failure;
		}

		QualificationLedger.Record(_acquired ? "resource.disposed" : "resource.disposed.unused");
	}
}
