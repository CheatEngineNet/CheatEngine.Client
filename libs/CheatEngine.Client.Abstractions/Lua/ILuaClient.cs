using System.Diagnostics.CodeAnalysis;

using CheatEngine.Client.Results;

namespace CheatEngine.Client.Lua;

/// <summary>Executes typed Lua operations without exposing a Lua state or CE object handle.</summary>
public interface ILuaClient
{
	/// <summary>Tries to register one explicitly supplied application Lua module on Cheat Engine's main thread.</summary>
	/// <remarks>
	///     The Client neither scans assemblies nor discovers exports by reflection. The module owns its generated SDK
	///     calls; the returned lease gives the Client deterministic, activation-scoped cleanup ownership.
	/// </remarks>
	public bool TryRegisterModule(
		ILuaModule luaModule,
		[NotNullWhen(true)] out ILuaModuleLease? lease,
		out CheatEngineFailure failure,
		CancellationToken cancellationToken = default);

	/// <summary>Registers one explicitly supplied application Lua module or throws when registration fails.</summary>
	public ILuaModuleLease RegisterModule(ILuaModule luaModule, CancellationToken cancellationToken = default);

	/// <summary>Tries to execute one typed operation on Cheat Engine's main thread.</summary>
	public bool TryExecute<TResult>(ILuaOperation<TResult> operation, [MaybeNullWhen(false)] out TResult result,
		out CheatEngineFailure failure, CancellationToken cancellationToken = default);

	/// <summary>Executes one typed operation or throws when it fails.</summary>
	public TResult Execute<TResult>(ILuaOperation<TResult> operation, CancellationToken cancellationToken = default);
}
