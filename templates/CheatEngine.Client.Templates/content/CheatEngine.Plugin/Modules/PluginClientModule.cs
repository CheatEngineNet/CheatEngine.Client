using CheatEngine.Client;
using CheatEngine.Client.Extensions.DependencyInjection;
using CheatEngine.Client.Memory;
using CheatEngine.Client.Modules;
using CheatEngine.Client.Processes;
using CheatEngine.Client.Results;
using CheatEngine.Client.Scanning;
using CheatEngine.SDK.Engine.Values;

using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace CheatEngine.Plugin.Modules;

/// <summary>
///     Demonstrates DI, options, materialization-bounded AOB and typed memory access, the Address List record count,
///     and generated Lua exports.
/// </summary>
internal sealed partial class PluginClientModule(
	IOptions<CheatEngineClientOptions> options,
	ILogger<PluginClientModule> logger) : ICheatEngineClientModule
{
	private readonly CheatEngineClientOptions _options = options.Value;

	/// <summary>Demonstrates bounded Client operations after generated Lua modules have been registered.</summary>
	public void OnEnabled(ICheatEngineClient client)
	{
		ArgumentNullException.ThrowIfNull(client);

		int allowedTableRootCount = _options.AllowedTableRoots.Count;
		LogEnabled(logger, client.Epoch, allowedTableRootCount);

		if (client.Tables.TryGetRecordCount(out int recordCount, out CheatEngineFailure tableFailure))
		{
			LogAddressList(logger, recordCount);
		}
		else
		{
			LogSkipped("Address List", tableFailure);
		}

		if (!client.Processes.TryGetCurrentProcess(out ProcessSnapshot process, out CheatEngineFailure processFailure))
		{
			LogSkipped("AOB/memory probe", processFailure);
			return;
		}

		// Cost and order: InModule keeps only matches that lie entirely inside the module, on every target. On a local
		// target it limits Cheat Engine's scan to the module (a bounded MemScan that blocks Cheat Engine's main thread);
		// on a CEServer or file-as-process target Cheat Engine scans the whole target and Client applies the module
		// while copying. FirstOrNone copies one address but never stops Cheat Engine early. "First" is Cheat Engine's
		// result-list order, which is not specified: it is not guaranteed to be the lowest address. A cancellation token
		// cannot interrupt a scan that has started.
		AobScanBuilder scan = client.Patterns.Aob("48 8B ?? ?? ?? 89");
		if (process.Name is { } processName)
		{
			scan = scan.InModule(processName);
		}

		// A scan that finds no match inside the module returns null: a factual zero on the bounded route, or a global
		// result list without an in-module address. When a global scan returns no result list at all, it fails with
		// IndeterminateHostResult, because that route cannot tell zero matches from a host failure, so it is logged as a
		// skipped probe, never treated as "not found".
		if (!scan.Executable()
				.FirstOrNone()
				.TryExecute(out Address? address, out CheatEngineFailure scanFailure))
		{
			LogSkipped("AOB probe", scanFailure);
			return;
		}

		if (address is not { } match)
		{
			return;
		}

		// A built-in primitive needs no codec. A custom type reads through a codec registered as an application service
		// and passed with each request (MemoryReadRequest<T>); the Client never resolves a codec implicitly.
		if (client.Memory.At(match + 0x14).TryRead<int>(out _, out CheatEngineFailure readFailure))
		{
			// Addresses and values are user data: this default log records only that the probe succeeded.
			LogMemoryReadSucceeded(logger);
		}
		else
		{
			LogSkipped("Memory probe", readFailure);
		}
	}

	/// <summary>Completes the application module lifecycle before generated Lua modules are released.</summary>
	public void OnDisabling(ICheatEngineClient client)
	{
		ArgumentNullException.ThrowIfNull(client);
	}

	/// <summary>Writes a classified Client failure without exposing user data.</summary>
	/// <remarks>
	///     Only <see cref="CheatEngineFailure.Kind" />, <see cref="CheatEngineFailure.Operation" /> and
	///     <see cref="CheatEngineFailure.HostEffect" /> are logged. <see cref="CheatEngineFailure.Message" /> and
	///     <see cref="CheatEngineFailure.Exception" /> can contain addresses, expressions, paths, or Lua text; log them only
	///     behind an explicit opt-in chosen by your application.
	/// </remarks>
	private void LogSkipped(string probe, CheatEngineFailure failure)
	{
		LogClientFailure(logger, probe, failure.Kind, failure.Operation, failure.HostEffect);
	}

	/// <summary>Logs the activation epoch and configured count of trusted table-file roots.</summary>
	[LoggerMessage(Level = LogLevel.Information,
		Message = "CheatEngine.Plugin enabled at epoch {Epoch}; " +
				  "configured trusted table-file root count is {AllowedTableRootCount}.")]
	private static partial void LogEnabled(ILogger logger, long epoch, int allowedTableRootCount);

	/// <summary>Logs the number of top-level records in the current Address List.</summary>
	[LoggerMessage(Level = LogLevel.Information, Message = "Current Address List contains {RecordCount} record(s).")]
	private static partial void LogAddressList(ILogger logger, int recordCount);

	/// <summary>Logs a successful bounded Int32 memory probe without logging the address or the value read.</summary>
	[LoggerMessage(Level = LogLevel.Information, Message = "A typed Int32 memory read near the AOB match succeeded.")]
	private static partial void LogMemoryReadSucceeded(ILogger logger);

	/// <summary>Logs the safe fields of a classified Client failure for an optional demonstration operation.</summary>
	[LoggerMessage(Level = LogLevel.Debug,
		Message = "Skipped {Probe}: {FailureKind} in {FailedOperation} (host effect: {HostEffect}).")]
	private static partial void LogClientFailure(ILogger logger, string probe, CheatEngineFailureKind failureKind,
		string failedOperation, CheatEngineHostEffect hostEffect);
}
