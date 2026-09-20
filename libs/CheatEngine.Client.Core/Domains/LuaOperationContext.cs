using CheatEngine.Client.Lua;
using CheatEngine.Client.Results;

namespace CheatEngine.Client.Core.Domains;

/// <summary>Epoch-bound context that is invalidated immediately after a typed Lua operation returns.</summary>
internal sealed class LuaOperationContext(long epoch, Func<bool> isCurrent) : ILuaExecutionContext
{
	private readonly Func<bool> _isCurrent = isCurrent ?? throw new ArgumentNullException(nameof(isCurrent));
	private int _expired;

	public long Epoch
	{
		get;
	} = epoch;

	public bool IsActive => Volatile.Read(ref _expired) == 0 && _isCurrent();

	public void ThrowIfExpired()
	{
		if (IsActive)
		{
			return;
		}

		throw new CheatEngineActivationExpiredException(
			"Lua.OperationContext",
			"The Lua operation context is no longer valid for the current Cheat Engine activation.");
	}

	internal void Expire()
	{
		Volatile.Write(ref _expired, 1);
	}
}
