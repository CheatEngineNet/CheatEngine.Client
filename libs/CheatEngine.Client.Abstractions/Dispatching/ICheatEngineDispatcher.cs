using System.Diagnostics.CodeAnalysis;

using CheatEngine.Client.Results;

namespace CheatEngine.Client.Dispatching;

/// <summary>Synchronously dispatches managed work to Cheat Engine's captured main thread.</summary>
public interface ICheatEngineDispatcher
{
	/// <summary>Gets whether the caller is already on Cheat Engine's captured main thread.</summary>
	public bool IsMainThread
	{
		get;
	}

	/// <summary>Runs a callback on the captured main thread.</summary>
	/// <remarks>
	///     <paramref name="cancellationToken" /> is observed before dispatch admission only. It never attempts to
	///     interrupt a callback or Lua primitive that has already begun on Cheat Engine's main thread.
	///     A <see langword="false" /> result represents cancellation or a dispatcher admission/infrastructure failure.
	///     Exceptions thrown by <paramref name="callback" /> are rethrown unchanged.
	/// </remarks>
	public bool TryInvoke(Action callback, out CheatEngineFailure failure,
		CancellationToken cancellationToken = default);

	/// <summary>Runs a callback on the captured main thread and returns its managed result.</summary>
	/// <remarks>
	///     Cancellation is observed before dispatch admission and never while the callback is running. A
	///     <see langword="false" /> result represents cancellation or a dispatcher admission/infrastructure failure;
	///     exceptions thrown by <paramref name="callback" /> are rethrown unchanged.
	/// </remarks>
	public bool TryInvoke<T>(Func<T> callback, [MaybeNullWhen(false)] out T result,
		out CheatEngineFailure failure,
		CancellationToken cancellationToken = default);

	/// <summary>Runs a callback on the captured main thread or throws when dispatch fails.</summary>
	/// <remarks>Cancellation is observed before dispatch admission only, never while a callback is running.</remarks>
	public void Invoke(Action callback, CancellationToken cancellationToken = default);

	/// <summary>Runs a callback on the captured main thread or throws when dispatch fails.</summary>
	/// <remarks>Cancellation is observed before dispatch admission only, never while a callback is running.</remarks>
	public T Invoke<T>(Func<T> callback, CancellationToken cancellationToken = default);
}
