using System.Text;

using CheatEngine.Client.Core.Dispatching;
using CheatEngine.Client.Core.Infrastructure;
using CheatEngine.Client.Dispatching;
using CheatEngine.Client.Lua;
using CheatEngine.Client.Results;
using CheatEngine.SDK.Lua.Calls;
using CheatEngine.SDK.Lua.Runtime;
using CheatEngine.SDK.Lua.State;

namespace CheatEngine.Client.Core.Domains;

internal sealed class UnsafeLuaClient : IUnsafeLuaClient
{
	private readonly ICheatEngineDispatcher _dispatcher;
	private readonly CoreLifetime? _lifetime;
	private readonly CoreClientPolicy _policy;

	internal UnsafeLuaClient(SdkMainThreadDispatcher dispatcher, CoreClientPolicy policy, CoreLifetime lifetime)
		: this((ICheatEngineDispatcher) (dispatcher ?? throw new ArgumentNullException(nameof(dispatcher))), policy, lifetime)
	{
	}

	/// <summary>Deterministic internal seam for policy tests; production construction uses the SDK dispatcher overload.</summary>
	internal UnsafeLuaClient(ICheatEngineDispatcher dispatcher, CoreClientPolicy policy, CoreLifetime? lifetime = null)
	{
		_dispatcher = dispatcher ?? throw new ArgumentNullException(nameof(dispatcher));
		_policy = policy ?? throw new ArgumentNullException(nameof(policy));
		_lifetime = lifetime;
	}

	public bool TryExecute(LuaScript script, out CheatEngineFailure failure,
		CancellationToken cancellationToken = default)
	{
		ArgumentNullException.ThrowIfNull(script.Source);
		if (script.ChunkName is { Length: 0 })
		{
			throw new ArgumentException("A Lua chunk name must be null or non-empty.", nameof(script));
		}

		_lifetime?.ThrowIfInactive("Lua.ExecuteUnsafe");

		if (!_policy.EnableUnsafeLuaExecution)
		{
			failure = new CheatEngineFailure(CheatEngineFailureKind.CapabilityUnavailable, "Lua.ExecuteUnsafe",
				"Arbitrary Lua execution was not enabled for this activation.");
			return false;
		}

		string luaStatus = "unknown";
		string? luaMessage = null;
		bool succeeded = false;
		if (!_dispatcher.TryInvoke(() =>
		    {
			    using LuaRuntimeOperation operation = LuaRuntime.AcquireOperation();
			    LuaState state = operation.State;
			    using LuaFrame frame = new(state);
			    byte[] source = Encoding.UTF8.GetBytes(script.Source);
			    ReadOnlySpan<byte> name = script.ChunkName is null
				    ? ReadOnlySpan<byte>.Empty
				    : Encoding.UTF8.GetBytes(script.ChunkName);
			    LuaStatus status = state.TryExecute(source, 0, name);
			    succeeded = status.IsOk;
			    luaStatus = status.ToString();
			    if (!succeeded)
			    {
				    luaMessage = LuaError.FromStack(state, status).Message;
			    }
		    }, out failure, cancellationToken))
		{
			return false;
		}

		if (succeeded)
		{
			return true;
		}

		failure = new CheatEngineFailure(CheatEngineFailureKind.LuaError, "Lua.ExecuteUnsafe",
			luaMessage is { Length: > 0 }
				? $"The protected Lua call failed with status '{luaStatus}': {luaMessage}"
				: $"The protected Lua call failed with status '{luaStatus}'.");
		return false;
	}

	public void Execute(LuaScript script, CancellationToken cancellationToken = default)
	{
		if (!TryExecute(script, out CheatEngineFailure failure, cancellationToken))
		{
			failure.Throw();
		}
	}
}
