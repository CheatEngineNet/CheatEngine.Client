using System.Diagnostics.CodeAnalysis;

using CheatEngine.Client.Results;

namespace CheatEngine.Client.Dispatching;

/// <summary>Synchronously dispatches managed work to Cheat Engine's captured main thread.</summary>
/// <remarks>
///     <para>
///         <b>Try is not "never throws".</b> The <c>TryInvoke</c> overloads return <see langword="false" /> only for a
///         pre-admission cancellation or a dispatcher admission/infrastructure failure. They <b>throw</b>
///         <see cref="CheatEngineActivationExpiredException" /> when the plugin activation has ended,
///         <see cref="CheatEngineClientLifecycleException" /> when the activation is stopping and no new work is admitted,
///         and <see cref="ArgumentNullException" /> for a <see langword="null" /> callback. An exception thrown by the
///         callback is rethrown as the same instance with its original stack trace, never converted into a failure.
///     </para>
///     <para>
///         The cancellation token is observed before dispatch admission only. A <see langword="false" /> result with
///         <see cref="CheatEngineFailureKind.Cancelled" /> therefore proves that the callback did not run
///         (<see cref="CheatEngineHostEffect.NotStarted" />). The token never interrupts a callback or a Lua primitive that
///         has begun on Cheat Engine's main thread and never removes an effect that such work produced.
///     </para>
///     <para>
///         Heavy computation on copied managed data may run on a worker, but creating, destroying, or accessing Cheat
///         Engine objects remains subject to the host's thread affinity: <see cref="Task.Run(Action)" /> does not make
///         driving the Cheat Engine GUI or Lua state safe.
///     </para>
/// </remarks>
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
	///     Exceptions thrown by <paramref name="callback" /> are rethrown unchanged (same instance). Lifecycle faults throw
	///     <see cref="CheatEngineActivationExpiredException" /> or <see cref="CheatEngineClientLifecycleException" />
	///     instead of returning a failure.
	/// </remarks>
	/// <exception cref="ArgumentNullException"><paramref name="callback" /> is <see langword="null" />.</exception>
	/// <exception cref="CheatEngineActivationExpiredException">The plugin activation has ended.</exception>
	/// <exception cref="CheatEngineClientLifecycleException">The activation is stopping and admits no new work.</exception>
	public bool TryInvoke(Action callback, out CheatEngineFailure failure,
		CancellationToken cancellationToken = default);

	/// <summary>Runs a callback on the captured main thread and returns its managed result.</summary>
	/// <remarks>
	///     Cancellation is observed before dispatch admission and never while the callback is running. A
	///     <see langword="false" /> result represents cancellation or a dispatcher admission/infrastructure failure;
	///     exceptions thrown by <paramref name="callback" /> are rethrown unchanged (same instance). Lifecycle faults throw
	///     instead of returning a failure.
	/// </remarks>
	/// <exception cref="ArgumentNullException"><paramref name="callback" /> is <see langword="null" />.</exception>
	/// <exception cref="CheatEngineActivationExpiredException">The plugin activation has ended.</exception>
	/// <exception cref="CheatEngineClientLifecycleException">The activation is stopping and admits no new work.</exception>
	public bool TryInvoke<T>(Func<T> callback, [MaybeNullWhen(false)] out T result,
		out CheatEngineFailure failure,
		CancellationToken cancellationToken = default);

	/// <summary>Runs a callback on the captured main thread or throws when dispatch fails.</summary>
	/// <remarks>
	///     Cancellation is observed before dispatch admission only, never while a callback is running. Callback
	///     exceptions are rethrown unchanged.
	/// </remarks>
	public void Invoke(Action callback, CancellationToken cancellationToken = default);

	/// <summary>Runs a callback on the captured main thread or throws when dispatch fails.</summary>
	/// <remarks>
	///     Cancellation is observed before dispatch admission only, never while a callback is running. Callback
	///     exceptions are rethrown unchanged.
	/// </remarks>
	public T Invoke<T>(Func<T> callback, CancellationToken cancellationToken = default);
}
