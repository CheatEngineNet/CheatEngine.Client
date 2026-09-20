using CheatEngine.Client.Dispatching;
using CheatEngine.Client.Lua;

namespace CheatEngine.Client.Core.Domains;

/// <summary>Activation-owned release path for one explicitly registered application Lua module.</summary>
internal sealed class LuaModuleLease(
	ILuaModule module,
	long epoch,
	ICheatEngineDispatcher dispatcher,
	Func<bool> isActivationCurrent,
	Action<ILuaModuleLease> untrack,
	Action<ILuaModule> releaseModule) : ILuaModuleLease
{
	private readonly ICheatEngineDispatcher _dispatcher =
		dispatcher ?? throw new ArgumentNullException(nameof(dispatcher));

	private readonly object _disposeLock = new();

	private readonly Func<bool> _isActivationCurrent =
		isActivationCurrent ?? throw new ArgumentNullException(nameof(isActivationCurrent));

	private readonly ILuaModule _module = module ?? throw new ArgumentNullException(nameof(module));

	private readonly Action<ILuaModule> _releaseModule =
		releaseModule ?? throw new ArgumentNullException(nameof(releaseModule));

	private readonly Action<ILuaModuleLease> _untrack = untrack ?? throw new ArgumentNullException(nameof(untrack));
	private int _released;

	public long Epoch
	{
		get;
	} = epoch;

	public bool IsReleased => Volatile.Read(ref _released) != 0;

	public void Dispose()
	{
		lock (_disposeLock)
		{
			if (Volatile.Read(ref _released) != 0)
			{
				return;
			}

			// A detached activation has no legal SDK dispatch path left. There is nothing more that this lease can
			// safely do, so release managed ownership without attempting to call Cheat Engine.
			if (!_isActivationCurrent())
			{
				CompleteRelease();
				return;
			}

			_dispatcher.Invoke(_module.Unregister);

			// Do not make any ownership transition until the application module confirmed unregistration by returning.
			// The write occurs while holding the lock, so another disposer either observes the completed release or
			// waits and retries after the original dispatch fault.
			CompleteRelease();
		}
	}

	private void CompleteRelease()
	{
		Volatile.Write(ref _released, 1);
		_untrack(this);
		_releaseModule(_module);
	}
}
