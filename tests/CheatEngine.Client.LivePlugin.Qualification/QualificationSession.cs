using System.Diagnostics.CodeAnalysis;

using CheatEngine.Client;
using CheatEngine.Client.Inspection;
using CheatEngine.Client.Memory;
using CheatEngine.Client.Scanning;

using LivePlugin.Qualification.Harness;

namespace LivePlugin.Qualification;

/// <summary>
///     The harness state the Lua functions read. It holds the Client of the current activation (set by the scenario
///     module's <c>OnEnabled</c>, cleared by its <c>OnDisabling</c>, so a Lua call never reaches an expired activation),
///     the gate decision of the current enable, the declared writable region, the symbol leases the harness created and
///     the log sink. Everything here outlives a disable on purpose: Cheat Engine never unloads a managed plugin, and the
///     scenarios read after a re-enable what the previous enable did.
/// </summary>
internal static class QualificationSession
{
	private static readonly Lock Gate = new();
	private static readonly Dictionary<string, ISymbolRegistrationLease> SymbolLeases = new(StringComparer.Ordinal);
	private static ActiveClient? _active;
	private static AuthorizationDecision _authorization = AuthorizationDecision.Denied(AuthorizationDenial.ManifestMissing);
	private static TargetDeclaration? _declaration;
	private static uint _pluginId;

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

	/// <summary>Records the facts of a new enable, before any activation exists.</summary>
	internal static void BeginEnable(uint pluginId, AuthorizationDecision authorization)
	{
		lock (Gate)
		{
			_pluginId = pluginId;
			_authorization = authorization;
		}
	}

	/// <summary>Publishes the Client of the activation that just enabled.</summary>
	internal static void Attach(ICheatEngineClient client, IPatternScanOutcomeClient scans, IMemoryClient batches)
	{
		lock (Gate)
		{
			_active = new ActiveClient(client, scans, batches);
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

	/// <summary>The Client services of one activation.</summary>
	internal sealed record ActiveClient(
		ICheatEngineClient Client,
		IPatternScanOutcomeClient Scans,
		IMemoryClient Batches);
}
