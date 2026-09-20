using CheatEngine.Client;
using CheatEngine.Client.Extensions.DependencyInjection;
using CheatEngine.Client.Lua;
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

/// <summary>Demonstrates DI, options, bounded AOB/memory access, Address List snapshots and generated Lua exports.</summary>
internal sealed partial class PluginClientModule(
	IMemoryCodec<int> int32Codec,
	IOptions<CheatEngineClientOptions> options,
	ILogger<PluginClientModule> logger) : ICheatEngineClientModule
{
	private readonly CheatEngineClientOptions _options = options.Value;
	private ILuaModuleLease? _luaModuleLease;

	public void OnEnabled(ICheatEngineClient client)
	{
		ArgumentNullException.ThrowIfNull(client);

		if (_luaModuleLease is not null)
		{
			throw new InvalidOperationException("The Lua module is already registered for this activation.");
		}

		_luaModuleLease = client.Lua.RegisterModule(new PluginLuaModule());

		LogEnabled(logger, client.Epoch, _options.DefaultMaximumAobResults);

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

		AobScanBuilder scan = client.Patterns.Aob("48 8B ?? ?? ?? 89");
		if (process.Name is { } processName)
		{
			scan = scan.InModule(processName);
		}

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

	public void OnDisabling(ICheatEngineClient client)
	{
		ArgumentNullException.ThrowIfNull(client);

		ILuaModuleLease? lease = _luaModuleLease;
		_luaModuleLease = null;
		if (lease is null)
		{
			return;
		}

		try
		{
			lease.Dispose();
		}
		catch (CheatEngineClientException exception)
		{
			LogLuaCleanupFailure(logger, exception.Failure.Message);
		}
		catch (InvalidOperationException exception)
		{
			LogLuaCleanupFailure(logger, exception.Message);
		}
	}

	private void LogSkipped(string operation, CheatEngineFailure failure)
	{
		LogClientFailure(logger, operation, failure);
	}

	[LoggerMessage(Level = LogLevel.Information,
		Message = "CheatEngine.Plugin enabled at epoch {Epoch}; configured AOB result ceiling is {MaximumResults}.")]
	private static partial void LogEnabled(ILogger logger, long epoch, int maximumResults);

	[LoggerMessage(Level = LogLevel.Information, Message = "Current Address List contains {RecordCount} record(s).")]
	private static partial void LogAddressList(ILogger logger, int recordCount);

	[LoggerMessage(Level = LogLevel.Information,
		Message = "A typed Int32 memory read succeeded near AOB match {Address}.")]
	private static partial void LogMemoryReadSucceeded(ILogger logger, Address address);

	[LoggerMessage(Level = LogLevel.Debug, Message = "Skipped {Operation}: {Reason}")]
	private static partial void LogClientFailure(ILogger logger, string operation, CheatEngineFailure reason);

	[LoggerMessage(Level = LogLevel.Debug, Message = "Lua module cleanup did not complete: {Reason}")]
	private static partial void LogLuaCleanupFailure(ILogger logger, string reason);
}
