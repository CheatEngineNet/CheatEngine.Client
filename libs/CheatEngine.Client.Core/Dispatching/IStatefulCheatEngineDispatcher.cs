using System.Diagnostics.CodeAnalysis;

using CheatEngine.Client.Results;

namespace CheatEngine.Client.Core.Dispatching;

/// <summary>Internal fast path that carries explicit state into the SDK main-thread dispatcher.</summary>
/// <remarks>
///     The public dispatcher contract deliberately remains small and source-compatible for third-party
///     implementations. Core services retain the closure-based public fallback only for non-SDK dispatchers.
/// </remarks>
internal interface IStatefulCheatEngineDispatcher
{
	public bool TryInvoke<TState, TResult>(TState state, Func<TState, TResult> callback,
		[MaybeNullWhen(false)] out TResult result, out CheatEngineFailure failure,
		CancellationToken cancellationToken);
}
