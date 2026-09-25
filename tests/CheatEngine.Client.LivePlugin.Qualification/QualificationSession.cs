using System.Diagnostics.CodeAnalysis;

using CheatEngine.Client;
using CheatEngine.Client.Allocations;
using CheatEngine.Client.Assembly;
using CheatEngine.Client.Inspection;
using CheatEngine.Client.Scanning;

using LivePlugin.Qualification.Harness;

// The harness keeps the leases of the experimental value scans (CECLIENT5001), allocations (CECLIENT5002) and Auto
// Assembler patches (CECLIENT5004) it created; the source is compiled standalone against the packed packages.
#pragma warning disable CECLIENT5001, CECLIENT5002, CECLIENT5004

namespace LivePlugin.Qualification;

/// <summary>
///     The harness state the Lua functions read. It holds the Client of the current activation (set by the scenario
///     module's <c>OnEnabled</c>, cleared by its <c>OnDisabling</c>, so a Lua call never reaches an expired activation),
///     the gate decision and session inputs of the current enable, the declared writable region, the leases the harness
///     created (symbols, allocations, the value-scan session, the Auto Assembler patch) and the log sink. Everything here
///     outlives a disable on purpose: Cheat Engine never unloads a managed plugin, and the scenarios read after a
///     re-enable, or after a target change, what the leases of an earlier step became.
/// </summary>
internal static class QualificationSession
{
	private static readonly Lock Gate = new();
	private static readonly Dictionary<string, ISymbolRegistrationLease> SymbolLeases = new(StringComparer.Ordinal);
	private static readonly Dictionary<string, ITargetMemoryLease> AllocationLeases = new(StringComparer.Ordinal);
	private static ActiveClient? _active;
	private static AuthorizationDecision _authorization = AuthorizationDecision.Denied(AuthorizationDenial.ManifestMissing);
	private static TargetDeclaration? _declaration;
	private static QualificationInputs _inputs = QualificationInputs.None;
	private static IAutoAssemblerPatchLease? _patch;
	private static uint _pluginId;
	private static ValueScanState? _valueScan;

	/// <summary>Gets the log sink registered in every activation's logging pipeline (Q46).</summary>
	internal static CapturingLoggerProvider Logs
	{
		get;
	} = new();

	/// <summary>Gets the gate decision evaluated by the last enable.</summary>
	internal static AuthorizationDecision Authorization
	{
		get
		{
			lock (Gate)
			{
				return _authorization;
			}
		}
	}

	/// <summary>Gets the writable region declaration, or <see langword="null" />.</summary>
	internal static TargetDeclaration? Declaration
	{
		get
		{
			lock (Gate)
			{
				return _declaration;
			}
		}
	}

	/// <summary>Gets the SDK plugin id recorded by the last enable.</summary>
	internal static uint PluginId
	{
		get
		{
			lock (Gate)
			{
				return _pluginId;
			}
		}
	}

	/// <summary>Gets the session inputs read by the last enable.</summary>
	internal static QualificationInputs Inputs
	{
		get
		{
			lock (Gate)
			{
				return _inputs;
			}
		}
	}

	/// <summary>Gets or sets the value-scan session the harness created, with the scratch slot it scans for.</summary>
	internal static ValueScanState? ValueScan
	{
		get
		{
			lock (Gate)
			{
				return _valueScan;
			}
		}
		set
		{
			lock (Gate)
			{
				_valueScan = value;
			}
		}
	}

	/// <summary>Gets or sets the Auto Assembler patch lease the harness applied.</summary>
	internal static IAutoAssemblerPatchLease? Patch
	{
		get
		{
			lock (Gate)
			{
				return _patch;
			}
		}
		set
		{
			lock (Gate)
			{
				_patch = value;
			}
		}
	}

	/// <summary>Records the facts of a new enable, before any activation exists.</summary>
	internal static void BeginEnable(uint pluginId, AuthorizationDecision authorization, QualificationInputs inputs)
	{
		lock (Gate)
		{
			_pluginId = pluginId;
			_authorization = authorization;
			_inputs = inputs;
		}
	}

	/// <summary>Publishes the Client and the service provider of the activation that just enabled.</summary>
	internal static void Attach(ICheatEngineClient client, IServiceProvider services)
	{
		lock (Gate)
		{
			_active = new ActiveClient(client, services);
		}
	}

	/// <summary>Withdraws the Client of the activation that is being disabled.</summary>
	internal static void Detach()
	{
		lock (Gate)
		{
			_active = null;
		}
	}

	/// <summary>The Client of the current activation, if one is enabled.</summary>
	internal static bool TryGetActive([NotNullWhen(true)] out ActiveClient? active)
	{
		lock (Gate)
		{
			active = _active;
			return active is not null;
		}
	}

	/// <summary>Stores the writable regions declared for the authorized target.</summary>
	internal static void Declare(TargetDeclaration declaration)
	{
		lock (Gate)
		{
			_declaration = declaration;
		}
	}

	/// <summary>Keeps a symbol lease created by the harness, replacing (and disposing) an older one of the same name.</summary>
	internal static void KeepSymbolLease(ISymbolRegistrationLease lease)
	{
		ISymbolRegistrationLease? previous;
		lock (Gate)
		{
			SymbolLeases.TryGetValue(lease.Name, out previous);
			SymbolLeases[lease.Name] = lease;
		}

		if (previous is not null && !ReferenceEquals(previous, lease))
		{
			previous.Dispose();
		}
	}

	/// <summary>The symbol lease the harness created for a name, released or not.</summary>
	internal static bool TryGetSymbolLease(string name, [NotNullWhen(true)] out ISymbolRegistrationLease? lease)
	{
		lock (Gate)
		{
			return SymbolLeases.TryGetValue(name, out lease);
		}
	}

	/// <summary>
	///     Keeps an allocation lease under a scenario name. An older lease of the same name is only forgotten, never
	///     released here: a lease of an earlier target must keep the outcome the Client gave it.
	/// </summary>
	internal static void KeepAllocation(string name, ITargetMemoryLease lease)
	{
		lock (Gate)
		{
			AllocationLeases[name] = lease;
		}
	}

	/// <summary>The allocation lease the harness created under a name, released or not.</summary>
	internal static bool TryGetAllocation(string name, [NotNullWhen(true)] out ITargetMemoryLease? lease)
	{
		lock (Gate)
		{
			return AllocationLeases.TryGetValue(name, out lease);
		}
	}

	/// <summary>
	///     The Client of one activation and its service provider, from which the harness resolves the services that
	///     exist only through a DI opt-in (<c>IAutoAssemblerClient</c>, <c>IUnsafeLuaClient</c>).
	/// </summary>
	internal sealed record ActiveClient(
		ICheatEngineClient Client,
		IServiceProvider Services);

	/// <summary>The value-scan session of the harness, the scratch slot it marks and the bytes the marker replaced.</summary>
	internal sealed record ValueScanState(IValueScanSession Session, ulong Slot, byte[] OriginalBytes);
}
