using CheatEngine.Client.Dispatching;
using CheatEngine.Client.Results;

namespace CheatEngine.Client.Core.Infrastructure;

/// <summary>Translates exceptions raised by Client-internal CheatEngine.SDK calls into classified failures.</summary>
/// <remarks>
///     <para>
///         Audit F15 / A11-31: no SDK exception type (<c>LuaException</c>, <c>EngineLuaException</c>,
///         <c>EngineGlobalUnavailableException</c>, <c>EngineMarshallingException</c>, a detached-runtime
///         <see cref="InvalidOperationException" />, ...) may cross a Client <c>Try*</c> method. Core wraps every
///         Client-internal SDK call (ports, generated <c>ClientLuaGlobals</c> bindings, <c>TargetMemory</c>,
///         <c>EngineInspection</c>, <c>AobScanner</c>, Address List access, protected Lua execution) and maps a fault
///         through <see cref="CoreFailureFactory" />, by exception type only.
///     </para>
///     <para>
///         Two kinds of exception are never translated. Client lifecycle exceptions
///         (<see cref="CheatEngineClientException" /> and its subclasses, notably
///         <see cref="CheatEngineActivationExpiredException" />) keep their meaning and propagate. Exceptions from
///         consumer-supplied code (codecs, <c>ILuaOperation</c>, Lua modules, dispatcher callbacks) are not wrapped at
///         all: the dispatcher rethrows them unchanged by contract.
///     </para>
///     <para>
///         When the activation is no longer current, an SDK fault is reported as
///         <see cref="CheatEngineActivationExpiredException" /> rather than as an ordinary failure, so an expired activation
///         is never reclassified as a rejection, a cancellation, or an unavailable capability (A10-21).
///     </para>
/// </remarks>
internal static class SdkBoundary
{
	/// <summary>Gets whether <paramref name="exception" /> is an SDK or host fault that a <c>Try*</c> method must translate.</summary>
	internal static bool IsSdkFault(Exception exception)
	{
		return exception is not CheatEngineClientException;
	}

	/// <summary>Maps an SDK fault to a failure, or throws the activation-expired exception when the activation ended.</summary>
	/// <param name="operation">The public Client operation name, for example <c>Memory.ReadBytes</c>.</param>
	/// <param name="exception">The SDK fault.</param>
	/// <param name="hostEffect">What is known about the Cheat Engine side effect when the fault was observed.</param>
	/// <param name="lifetime">The owning activation lifetime, when the caller has one.</param>
	/// <returns>The classified failure.</returns>
	/// <exception cref="CheatEngineActivationExpiredException">The activation expired while the SDK call ran.</exception>
	internal static CheatEngineFailure Translate(string operation, Exception exception,
		CheatEngineHostEffect hostEffect, CoreLifetime? lifetime)
	{
		ArgumentNullException.ThrowIfNull(exception);
		ThrowIfActivationEnded(operation, exception, lifetime);
		return CoreFailureFactory.FromException(operation, exception, hostEffect);
	}

	/// <summary>Dispatches Client-internal SDK work and returns an SDK fault as a failure instead of rethrowing it.</summary>
	/// <param name="dispatcher">The dispatcher that runs <paramref name="work" /> on Cheat Engine's main thread.</param>
	/// <param name="operation">The public Client operation name.</param>
	/// <param name="work">Client-internal SDK work only. Never pass consumer-supplied code: it must keep the dispatcher's
	///     unchanged-rethrow rule.</param>
	/// <param name="hostEffectOnFault">What is known about the Cheat Engine side effect when the SDK work faults.</param>
	/// <param name="lifetime">The owning activation lifetime, when the caller has one.</param>
	/// <param name="failure">The dispatcher failure or the translated SDK fault.</param>
	/// <param name="cancellationToken">Observed by the dispatcher before admission.</param>
	/// <returns><see langword="true" /> when the work ran to completion without an SDK fault.</returns>
	/// <remarks>
	///     Lifecycle exceptions from the dispatcher (<see cref="CheatEngineActivationExpiredException" />,
	///     <see cref="CheatEngineClientLifecycleException" />) and Client exceptions raised by <paramref name="work" />
	///     propagate unchanged.
	/// </remarks>
	internal static bool TryInvoke(ICheatEngineDispatcher dispatcher, string operation, Action work,
		CheatEngineHostEffect hostEffectOnFault, CoreLifetime? lifetime, out CheatEngineFailure failure,
		CancellationToken cancellationToken)
	{
		ArgumentNullException.ThrowIfNull(dispatcher);
		ArgumentNullException.ThrowIfNull(work);

		Exception? fault = null;
		if (!dispatcher.TryInvoke(() =>
			{
				try
				{
					work();
				}
				catch (Exception exception) when (IsSdkFault(exception))
				{
					fault = exception;
				}
			}, out failure, cancellationToken))
		{
			return false;
		}

		if (fault is null)
		{
			return true;
		}

		failure = Translate(operation, fault, hostEffectOnFault, lifetime);
		return false;
	}

	/// <summary>Throws the activation-expired exception when an SDK fault was observed after the activation ended.</summary>
	/// <exception cref="CheatEngineActivationExpiredException">The activation is no longer current.</exception>
	internal static void ThrowIfActivationEnded(string operation, Exception exception, CoreLifetime? lifetime)
	{
		if (lifetime is { IsActivationCurrent: false })
		{
			throw new CheatEngineActivationExpiredException(operation,
				"The Cheat Engine plugin lifecycle changed while Client work was calling the SDK.", exception);
		}
	}
}
