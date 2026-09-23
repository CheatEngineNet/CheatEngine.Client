using CheatEngine.SDK.Annotations.Lua;

namespace LivePlugin.Qualification;

/// <summary>
///     The Lua functions of the qualification harness, one per Client C3/C4 scenario step (see the project README).
///     Every function returns one JSON observation. The functions that change the target or Cheat Engine state
///     (<c>capabilities(0)</c>, <c>target_declare</c>, <c>memory_roundtrip</c>, <c>memory_batch_partial</c>,
///     <c>table_create</c>, <c>symbol_register</c>, <c>symbol_release</c>) run through
///     <see cref="QualificationScenarios.RunMutating" />, which refuses them unless the qualification gate authorized the
///     run and the Client observes exactly the authorized target.
/// </summary>
internal static partial class QualificationLuaFunctions
{
	/// <summary>Plugin identity, gate and fault decisions, activation history and bridge hash.</summary>
	[LuaFunction("cheatengine_client_qualification_status")]
	public static string Status()
	{
		return QualificationScenarios.Status();
	}

	/// <summary>The Client process and runtime snapshots, the process id first.</summary>
	[LuaFunction("cheatengine_client_qualification_runtime")]
	public static string Runtime()
	{
		return QualificationScenarios.Runtime();
	}

	/// <summary>Capability availability; with <paramref name="probeOnly" /> = 0, one harmless call per unavailable family.</summary>
	[LuaFunction("cheatengine_client_qualification_capabilities")]
	public static string Capabilities(long probeOnly)
	{
		return QualificationScenarios.Capabilities(probeOnly);
	}

	/// <summary>Declares the driver-allocated scratch region after verifying it through the Client.</summary>
	[LuaFunction("cheatengine_client_qualification_target_declare")]
	public static string TargetDeclare(string symbolName)
	{
		return QualificationScenarios.DeclareTarget(symbolName);
	}

	/// <summary>One Client AOB scan with its detailed outcome.</summary>
	[LuaFunction("cheatengine_client_qualification_aob")]
	public static string Aob(string pattern, string moduleName, long maxResults, long cancelAfterMs)
	{
		return QualificationScenarios.Aob(pattern, moduleName, maxResults, cancelAfterMs);
	}

	/// <summary>Writes, reads back and restores one boundary value in the declared scratch region.</summary>
	[LuaFunction("cheatengine_client_qualification_memory_roundtrip")]
	public static string MemoryRoundTrip(string kind, string symbolName)
	{
		return QualificationScenarios.MemoryRoundTrip(kind, symbolName);
	}

	/// <summary>A four-element batch whose third address is never mapped.</summary>
	[LuaFunction("cheatengine_client_qualification_memory_batch_partial")]
	public static string MemoryBatchPartial(string symbolName, long invalidAddress)
	{
		return QualificationScenarios.MemoryBatchPartial(symbolName, invalidAddress);
	}

	/// <summary>Creates one memory record on the scratch symbol and remembers its id.</summary>
	[LuaFunction("cheatengine_client_qualification_table_create")]
	public static string TableCreate(string symbolName)
	{
		return QualificationScenarios.TableCreate(symbolName);
	}

	/// <summary>Reads the remembered record id again after the driver destroyed the record or reloaded the table.</summary>
	[LuaFunction("cheatengine_client_qualification_table_probe")]
	public static string TableProbe()
	{
		return QualificationScenarios.TableProbe();
	}

	/// <summary>Registers a harness symbol on the scratch address through a Client symbol lease.</summary>
	[LuaFunction("cheatengine_client_qualification_symbol_register")]
	public static string SymbolRegister(string name, string symbolName)
	{
		return QualificationScenarios.SymbolRegister(name, symbolName);
	}

	/// <summary>Disposes the harness's symbol lease.</summary>
	[LuaFunction("cheatengine_client_qualification_symbol_release")]
	public static string SymbolRelease(string name)
	{
		return QualificationScenarios.SymbolRelease(name);
	}

	/// <summary>The harness's symbol lease and the current resolution of the name.</summary>
	[LuaFunction("cheatengine_client_qualification_symbol_state")]
	public static string SymbolState(string name)
	{
		return QualificationScenarios.SymbolState(name);
	}

	/// <summary>The captured log templates and the sensitive-data hit count.</summary>
	[LuaFunction("cheatengine_client_qualification_logs")]
	public static string Logs()
	{
		return QualificationScenarios.Logs();
	}
}
