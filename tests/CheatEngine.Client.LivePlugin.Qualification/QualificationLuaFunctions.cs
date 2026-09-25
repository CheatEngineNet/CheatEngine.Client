using CheatEngine.SDK.Annotations.Lua;

namespace LivePlugin.Qualification;

/// <summary>
///     The Lua functions of the qualification harness, one per Client C3/C4 scenario step (see the project README).
///     Every function returns one JSON observation. The functions that change the target or Cheat Engine state (the
///     README marks them) run through <see cref="QualificationScenarios.RunMutating" />, which refuses them unless the
///     qualification gate authorized the run and, by default, the Client observes exactly the authorized target.
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

	/// <summary>One value-scan step on the scratch region (Q25, Q26).</summary>
	[LuaFunction("cheatengine_client_qualification_value_scan")]
	public static string ValueScan(string action, string symbolName, long value)
	{
		return QualificationScenarios.ValueScan(action, symbolName, value);
	}

	/// <summary>One allocation step under a harness-prefixed name (Q30.a, Q30.b).</summary>
	[LuaFunction("cheatengine_client_qualification_allocation")]
	public static string Allocation(string action, string name, long size)
	{
		return QualificationScenarios.Allocation(action, name, size);
	}

	/// <summary>The instruction profile of the selected target, with a round trip in the scratch region (Q32).</summary>
	[LuaFunction("cheatengine_client_qualification_instructions")]
	public static string Instructions(string symbolName)
	{
		return QualificationScenarios.Instructions(symbolName);
	}

	/// <summary>One Auto Assembler patch step, only when the run composed the opt-in (Q35, Q44).</summary>
	[LuaFunction("cheatengine_client_qualification_aa_patch")]
	public static string AaPatch(string action, string variant)
	{
		return QualificationScenarios.AutoAssemblerPatch(action, variant);
	}

	/// <summary>Starts the worker admission probe, or reads its result (Q19).</summary>
	[LuaFunction("cheatengine_client_qualification_worker_admission")]
	public static string WorkerAdmission(string action)
	{
		return QualificationScenarios.WorkerAdmission(action);
	}

	/// <summary>Saves the current table below the runner's table root, or outside it to observe the refusal (Q34).</summary>
	[LuaFunction("cheatengine_client_qualification_table_save")]
	public static string TableSave(string fileName, long outsideRoot)
	{
		return QualificationScenarios.TableSave(fileName, outsideRoot);
	}

	/// <summary>Loads a table from the runner's table root (Q34).</summary>
	[LuaFunction("cheatengine_client_qualification_table_load")]
	public static string TableLoad(string fileName)
	{
		return QualificationScenarios.TableLoad(fileName);
	}

	/// <summary>Returns the integer CheatEngine.SDK marshalled (CRIT-07: floats from 2^53 on are refused).</summary>
	[LuaFunction("cheatengine_client_qualification_integer_echo")]
	public static string IntegerEcho(long value)
	{
		return QualificationScenarios.IntegerEcho(value);
	}

	/// <summary>Returns the target address (<see cref="nuint" />) CheatEngine.SDK marshalled (CRIT-07).</summary>
	[LuaFunction("cheatengine_client_qualification_address_echo")]
	public static string AddressEcho(nuint address)
	{
		return QualificationScenarios.AddressEcho(address);
	}
}
