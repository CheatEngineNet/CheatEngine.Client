using System.Diagnostics.CodeAnalysis;

using CheatEngine.Client.Allocations;
using CheatEngine.Client.Assembly;
using CheatEngine.Client.Dispatching;
using CheatEngine.Client.Inspection;
using CheatEngine.Client.Lua;
using CheatEngine.Client.Memory;
using CheatEngine.Client.Processes;
using CheatEngine.Client.Results;
using CheatEngine.Client.Runtime;
using CheatEngine.Client.Scanning;
using CheatEngine.Client.Tables;

namespace CheatEngine.Client;

/// <summary>A scoped, high-level client for the active Cheat Engine plugin lifecycle.</summary>
/// <remarks>
///     <para>
///         <b>Call-only.</b> The Client implements this interface and applications call it. A minor release can add
///         members to it, so implement it only in a test double.
///     </para>
///     <para>
///         <b>Handle-free surface.</b> No member of this client, and no service, value or lease it returns, exposes a Lua
///         state, a Lua reference, a CE object handle or an SDK ownership wrapper. That guarantee covers the Client
///         surface only. A plugin that derives from <c>CheatEngineClientPlugin</c> (CheatEngine.Client.Hosting) also
///         inherits CheatEngine.SDK's <c>protected static</c> <c>CheatEnginePlugin.Context</c>, the raw SDK plugin context
///         of the current enable, which <c>CheatEngineClientPlugin</c> documents as a raw SDK escape hatch: it is outside
///         every guarantee of this client (activation epochs, main-thread dispatch, failure classification, resource
///         ownership and release, redaction). Code that uses it, or any other CheatEngine.SDK API directly, follows the
///         CheatEngine.SDK contract instead.
///     </para>
///     <para>
///         <b>Try and throwing forms.</b> An operation that can fail for an expected reason has a <c>TryX</c> form, which
///         returns <see langword="false" /> with a classified <see cref="CheatEngineFailure" />, and a throwing <c>X</c>
///         form, which returns the same value or throws that same failure through
///         <see cref="CheatEngineFailure.Throw(CancellationToken)" />. Use the <c>Try</c> form when the failure is an
///         expected condition. <c>Try</c> does not mean "never throws": both forms throw for an expired or stopping
///         activation and for an invalid argument (a programming error), and both rethrow, unchanged, an exception thrown
///         by application code that the client calls (a dispatcher callback, a memory codec, a Lua operation). No
///         CheatEngine.SDK exception is thrown by a <c>Try</c> form. Some operations also have an <c>XDetailed</c> form
///         that returns an outcome (<c>IsSuccess</c>, <c>Failure</c> and the operation's facts) instead of throwing an
///         expected failure; it throws exactly what the <c>Try</c> form throws.
///     </para>
///     <para>
///         <b>Exceptions.</b> The exception type depends only on <see cref="CheatEngineFailure.Kind" />:
///         <see cref="CheatEngineFailureKind.Cancelled" /> throws <see cref="CheatEngineOperationCanceledException" />, an
///         <see cref="OperationCanceledException" />; <see cref="CheatEngineFailureKind.ActivationExpired" /> throws
///         <see cref="CheatEngineActivationExpiredException" />; <see cref="CheatEngineFailureKind.InvalidState" /> throws
///         <see cref="CheatEngineInvalidStateException" />; every other kind throws
///         <see cref="CheatEngineOperationException" />. Each exception keeps the complete failure, and none has a public
///         constructor: <see cref="CheatEngineFailure.Throw(CancellationToken)" /> throws one and
///         <see cref="CheatEngineFailure.ToException(CancellationToken)" /> creates one. Classify a failure by
///         its <see cref="CheatEngineFailure.Kind" /> and <see cref="CheatEngineFailure.HostEffect" />, never by
///         <see cref="CheatEngineFailure.Message" /> or exception text, which are not contractual and may contain user
///         data. Releasing a lease never throws: <see cref="ICheatEngineLease.Release" /> returns an outcome.
///     </para>
///     <para>
///         <b>Cancellation.</b> A <see cref="CancellationToken" /> is observed before Cheat Engine work is dispatched and
///         between Client-managed steps. It never interrupts a Cheat Engine call that has started and never removes an
///         effect that such a call produced: <see cref="CheatEngineFailure.HostEffect" /> says whether the work started.
///         <see cref="Stopping" /> is cancelled when the plugin begins to disable; the client then admits no new work.
///     </para>
///     <para>
///         <b>Threading.</b> Every member is synchronous, because an attached Cheat Engine Lua runtime cannot safely be
///         retained across an <c>await</c> boundary. Members can be called from any thread while the activation is
///         active: Cheat Engine work always runs on Cheat Engine's main thread through <see cref="Dispatcher" />, and the
///         calling thread waits for it. Do not make a worker wait for the main thread if that worker can call back into
///         the client. Returned values are copies that stay valid after the activation ends; leases and sessions belong
///         to the activation (<see cref="Epoch" />) and, when bound to the selected target, to that target.
///     </para>
/// </remarks>
public interface ICheatEngineClient
{
	/// <summary>Gets the activation epoch captured for this scoped client.</summary>
	public long Epoch
	{
		get;
	}

	/// <summary>Gets a token cancelled when the active plugin lifecycle begins stopping.</summary>
	public CancellationToken Stopping
	{
		get;
	}

	/// <summary>Gets the runtime and capability service.</summary>
	public ICheatEngineRuntime Runtime
	{
		get;
	}

	/// <summary>Gets the explicit main-thread dispatcher.</summary>
	public ICheatEngineDispatcher Dispatcher
	{
		get;
	}

	/// <summary>Gets the selected-target process service.</summary>
	public IProcessClient Processes
	{
		get;
	}

	/// <summary>Gets typed target-memory operations.</summary>
	public IMemoryClient Memory
	{
		get;
	}

	/// <summary>Gets AOB scan operations.</summary>
	public IPatternScanner Patterns
	{
		get;
	}

	/// <summary>Gets value-scan operations over Cheat Engine's scanner.</summary>
	/// <remarks>Experimental (<c>CECLIENT5001</c>): see the Abstractions README.</remarks>
	[Experimental(ClientExperimentalDiagnostics.ValueScans, UrlFormat = ClientExperimentalDiagnostics.UrlFormat)]
	public IValueScanner ValueScans
	{
		get;
	}

	/// <summary>Gets copied module, symbol, and region inspection operations.</summary>
	public IInspectionClient Inspection
	{
		get;
	}

	/// <summary>Gets copied address-table and memory-record operations.</summary>
	public ITableClient Tables
	{
		get;
	}

	/// <summary>Gets the protected, handle-free Lua execution service.</summary>
	public ILuaClient Lua
	{
		get;
	}

	/// <summary>Gets target-memory allocations, each owned by a lease bound to the selected target.</summary>
	/// <remarks>Experimental (<c>CECLIENT5002</c>): see the Abstractions README.</remarks>
	[Experimental(ClientExperimentalDiagnostics.Allocations, UrlFormat = ClientExperimentalDiagnostics.UrlFormat)]
	public IAllocationClient Allocations
	{
		get;
	}

	/// <summary>Gets copied single-instruction assembly, disassembly and length operations.</summary>
	/// <remarks>
	///     Experimental (<c>CECLIENT5003</c>). Auto Assembler patches are not part of it: they are applied through
	///     <c>IAutoAssemblerClient</c>, which only the <c>EnableAutoAssemblerPatches()</c> opt-in registers.
	/// </remarks>
	[Experimental(ClientExperimentalDiagnostics.Instructions, UrlFormat = ClientExperimentalDiagnostics.UrlFormat)]
	public IAssemblyClient Assembly
	{
		get;
	}
}
