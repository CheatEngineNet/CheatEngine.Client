using CheatEngine.Client;
using CheatEngine.Client.Extensions.DependencyInjection;
using CheatEngine.Client.Memory;
using CheatEngine.Client.Modules;
using CheatEngine.Client.Processes;
using CheatEngine.Client.Results;
using CheatEngine.Client.Scanning;
using CheatEngine.Client.Tables;
using CheatEngine.SDK.Engine.Values;

using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace CheatEngine.Plugin.Modules;

/// <summary>
///     Demonstrates DI, options, materialization-bounded AOB and typed memory access, Address List snapshots, and
///     generated Lua exports.
/// </summary>
internal sealed partial class PluginClientModule(
	IMemoryCodec<int> int32Codec,
	IOptions<CheatEngineClientOptions> options,
	ILogger<PluginClientModule> logger) : ICheatEngineClientModule
{
	private readonly CheatEngineClientOptions _options = options.Value;

	/// <summary>Demonstrates bounded Client operations after generated Lua modules have been registered.</summary>
	public void OnEnabled(ICheatEngineClient client)
	{
		ArgumentNullException.ThrowIfNull(client);

		int allowedTableRootCount = _options.AllowedTableRoots?.Length ?? 0;
		LogEnabled(logger, client.Epoch, allowedTableRootCount);

		if (client.Tables.TryGetCurrent(out AddressTableSnapshot table, out CheatEngineFailure tableFailure))
		{
			LogAddressList(logger, table.RecordCount);
		}
		else
		{
			LogSkipped("Address List", tableFailure);
		}

		if (!client.Processes.TryGetCurrent(out ProcessSnapshot process, out CheatEngineFailure processFailure))
		{
			LogSkipped("AOB/memory probe", processFailure);
			return;
		}

		// Cost and order: InModule is a managed post-filter. Cheat Engine still runs one global AOBScan over the whole
		// target, and Client keeps only the addresses inside the module; FirstOrNone copies one filtered address but
		// never stops Cheat Engine early. "First" is Cheat Engine's result-list order, which is not specified: it is not
		// guaranteed to be the lowest address. A cancellation token cannot interrupt a scan that has started.
		AobScanBuilder scan = client.Patterns.Aob("48 8B ?? ?? ?? 89");
		if (process.Name is { } processName)
		{
			scan = scan.InModule(processName);
		}

		// With CheatEngine.SDK 1.0.0 a scan that finds nothing fails with IndeterminateHostResult: zero matches and a
		// host failure are indistinguishable, so it is logged as a skipped probe, never treated as "not found".
		if (!scan.ReadableExecutable()
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

		if (client.Memory.At(match + 0x14).TryReadWith(int32Codec, out _, out CheatEngineFailure readFailure))
		{
			LogMemoryReadSucceeded(logger, match);
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

	/// <summary>Writes a bounded Client operation failure without exposing target-memory data.</summary>
	private void LogSkipped(string operation, CheatEngineFailure failure)
	{
		LogClientFailure(logger, operation, failure);
	}

	/// <summary>Logs the activation epoch and configured count of trusted table-file roots.</summary>
	[LoggerMessage(Level = LogLevel.Information,
		Message = "CheatEngine.Plugin enabled at epoch {Epoch}; " +
		          "configured trusted table-file root count is {AllowedTableRootCount}.")]
	private static partial void LogEnabled(ILogger logger, long epoch, int allowedTableRootCount);

	/// <summary>Logs the number of records in the current Address List snapshot.</summary>
	[LoggerMessage(Level = LogLevel.Information, Message = "Current Address List contains {RecordCount} record(s).")]
	private static partial void LogAddressList(ILogger logger, int recordCount);

	/// <summary>Logs a successful bounded Int32 memory probe without logging the value read.</summary>
	[LoggerMessage(Level = LogLevel.Information,
		Message = "A typed Int32 memory read succeeded near AOB match {Address}.")]
	private static partial void LogMemoryReadSucceeded(ILogger logger, Address address);

	/// <summary>Logs a classified Client failure for an optional demonstration operation.</summary>
	[LoggerMessage(Level = LogLevel.Debug, Message = "Skipped {Operation}: {Reason}")]
	private static partial void LogClientFailure(ILogger logger, string operation, CheatEngineFailure reason);
}
