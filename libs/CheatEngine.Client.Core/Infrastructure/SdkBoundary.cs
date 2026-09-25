using CheatEngine.Client.Dispatching;
using CheatEngine.Client.Results;
using CheatEngine.SDK.Lua.Runtime;

namespace CheatEngine.Client.Core.Infrastructure;

/// <summary>Translates exceptions raised by Client-internal CheatEngine.SDK calls into classified failures.</summary>
/// <remarks>
///     <para>
///         Audit F15 / A11-31: no SDK exception type (<c>LuaException</c>, <c>EngineLuaException</c>,
///         <c>EngineGlobalUnavailableException</c>, <c>EngineMarshallingException</c>, a detached-runtime
///         <see cref="InvalidOperationException" />, ...) may cross a Client <c>Try*</c> method. Core wraps every
///         Client-internal SDK call, whether a domain client makes it or its <c>Sdk*Port</c> adapter does:
///         <c>TargetMemory</c>, <c>EngineInspection</c>, <c>SymbolRegistry</c>, <c>AobScanner</c>,
///         <c>MemoryScanSessions</c>, <c>TargetMemoryAllocator</c>, <c>AutoAssemblerPatcher</c>, the instruction
///         assembler, disassembler and navigator, the runtime, host and process observations, process selection,
///         Address List access and mutations, <c>CheatTableFiles</c>, and protected Lua execution. It maps a fault
///         through <see cref="CoreFailureFactory" />, by exception type and SDK failure category, never by message text.
///     </para>
///     <para>
///         Two kinds of exception are never translated. Client lifecycle exceptions
///         (<see cref="CheatEngineClientException" /> and its subclasses, notably
///         <see cref="CheatEngineActivationExpiredException" />) keep their meaning and propagate. Exceptions from
///         consumer-supplied code (codecs, <c>ILuaOperation</c>, dispatcher callbacks) are not wrapped at all: the
///         dispatcher rethrows them unchanged by contract. A Lua module's <c>ILuaModule.Register</c> is the exception
///         to that rule: a generated module surfaces the CheatEngine.SDK faults of its registration from it (F15), so
///         <c>LuaClient</c> reports the failure a <see cref="CheatEngineClientException" /> or a
///         <see cref="CheatEngineOperationCanceledException" /> carries and classifies any other exception with
///         <see cref="Classify(string, Exception, CheatEngineHostEffect)" />.
///     </para>
///     <para>
///         When the activation is no longer current, an SDK fault is reported as
///         <see cref="CheatEngineActivationExpiredException" /> rather than as an ordinary failure, so an expired activation
///         is never reclassified as a rejection, a cancellation, or an unavailable capability (A10-21).
///     </para>
///     <para>
///         When CheatEngine.SDK has detected that Cheat Engine replaced its Lua state outside the plugin's control
///         (<see cref="LuaRuntime.ExternalStateResetDetected" />, sticky until the next attach), every admission path
///         of the SDK refuses with a plain <see cref="InvalidOperationException" />. Such a fault is reported as
///         <see cref="CheatEngineFailureKind.RuntimeChanged" />, never as a rejection. SDK exceptions that carry their
///         own category keep it.
///     </para>
/// </remarks>
internal static class SdkBoundary
{
	/// <summary>
	///     Gets the SDK's sticky external Lua state reset fact (<see cref="LuaRuntime.ExternalStateResetDetected" />), the
	///     same fact this boundary classifies faults with. Hosting reads it through the dependency-injection cleanup bridge
	///     after the releases of a deactivation and logs event 8 when it is set (A8).
	/// </summary>
	internal static bool ExternalStateResetDetected => LuaRuntime.ExternalStateResetDetected;

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
		return Classify(operation, exception, hostEffect, LuaRuntime.ExternalStateResetDetected);
	}

	/// <summary>Classifies an SDK fault with the SDK's current external Lua state reset fact.</summary>
	/// <param name="operation">The public Client operation name.</param>
	/// <param name="exception">The SDK fault.</param>
	/// <param name="hostEffect">What is known about the Cheat Engine side effect when the fault was observed.</param>
	/// <returns>The classified failure; see <see cref="Classify(string, Exception, CheatEngineHostEffect, bool)" />.</returns>
	/// <remarks>
	///     For callers that already handled the activation lifetime themselves, such as the dispatcher, so every SDK
	///     fault observed after an external reset is reported the same way.
	/// </remarks>
	internal static CheatEngineFailure Classify(string operation, Exception exception, CheatEngineHostEffect hostEffect)
	{
		return Classify(operation, exception, hostEffect, LuaRuntime.ExternalStateResetDetected);
	}

	/// <summary>Classifies an SDK fault given the SDK's external Lua state reset fact.</summary>
	/// <param name="operation">The public Client operation name.</param>
	/// <param name="exception">The SDK fault.</param>
	/// <param name="hostEffect">What is known about the Cheat Engine side effect when the fault was observed.</param>
	/// <param name="externalStateResetDetected">
	///     The value of <see cref="LuaRuntime.ExternalStateResetDetected" /> when the fault was observed; a parameter so
	///     the rule is testable without a host.
	/// </param>
	/// <returns>
	///     <see cref="CheatEngineFailureKind.RuntimeChanged" /> for an otherwise unclassified
	///     <see cref="InvalidOperationException" /> observed after an external reset; otherwise the
	///     <see cref="CoreFailureFactory" /> classification.
	/// </returns>
	internal static CheatEngineFailure Classify(string operation, Exception exception, CheatEngineHostEffect hostEffect,
		bool externalStateResetDetected)
	{
		CheatEngineFailure failure = CoreFailureFactory.FromException(operation, exception, hostEffect);
		return externalStateResetDetected && exception is InvalidOperationException &&
			   failure.Kind == CheatEngineFailureKind.OperationRejected
			? new CheatEngineFailure(CheatEngineFailureKind.RuntimeChanged, failure.Operation, failure.Message,
				exception, failure.HostEffect)
			: failure;
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
	///     <see cref="CheatEngineInvalidStateException" />) and Client exceptions raised by <paramref name="work" />
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
			new CheatEngineFailure(CheatEngineFailureKind.ActivationExpired, operation,
				"The Cheat Engine plugin lifecycle changed while Client work was calling the SDK.", exception).Throw();
		}
	}
}
