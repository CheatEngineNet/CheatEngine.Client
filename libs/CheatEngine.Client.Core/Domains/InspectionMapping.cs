using CheatEngine.Client.Inspection;
using CheatEngine.Client.Results;
using CheatEngine.SDK.Engine.Inspection;
using CheatEngine.SDK.Lua.Calls;

namespace CheatEngine.Client.Core.Domains;

/// <summary>
///     Maps every <see cref="LuaOperationStatusKind" /> that the CheatEngine.SDK 2.0.0 symbol registry reports
///     (<c>SymbolRegistry.TryGetName</c> and <c>SymbolRegistry.TryRegisterOwned</c>) to the Client vocabulary, value by
///     value, and the Client address resolution mode to the SDK options.
/// </summary>
/// <remarks>
///     <para>
///         A name lookup changes nothing, so its failure keeps an unknown host effect, except an unavailable global,
///         which Cheat Engine never called (<c>NotStarted</c>):
///     </para>
///     <list type="table">
///         <listheader>
///             <term>SDK status</term>
///             <description>Name lookup failure kind</description>
///         </listheader>
///         <item><term><c>Success</c> without a name</term><description><c>InvalidHostResult</c></description></item>
///         <item><term><c>NilResult</c></term><description><c>NotFound</c>: Cheat Engine returned no name</description></item>
///         <item><term><c>GlobalUnavailable</c></term><description><c>CapabilityUnavailable</c></description></item>
///         <item><term><c>LuaFailure</c>, <c>StackUnavailable</c></term><description><c>LuaError</c></description></item>
///         <item>
///             <term><c>InvalidResult</c>, <c>MissingResult</c>, <c>ResultCapacityExceeded</c></term>
///             <description><c>InvalidHostResult</c></description>
///         </item>
///         <item>
///             <term><c>Unknown</c> or an undefined status</term>
///             <description><c>IndeterminateHostResult</c></description>
///         </item>
///     </list>
///     <para>
///         A registration that returned no lease maps to a failure kind and to what is known about the registration:
///     </para>
///     <list type="table">
///         <listheader>
///             <term>SDK status</term>
///             <description>Registration failure kind and host effect</description>
///         </listheader>
///         <item>
///             <term><c>Success</c> without a lease</term>
///             <description>
///                 <c>IndeterminateHostResult</c>, <c>CleanupUnconfirmed</c>: Cheat Engine accepted a registration
///                 that no lease owns.
///             </description>
///         </item>
///         <item>
///             <term><c>GlobalUnavailable</c></term>
///             <description>
///                 <c>CapabilityUnavailable</c>, <c>Unknown</c>: CheatEngine.SDK also reports it when the Lua runtime
///                 changed after Cheat Engine registered the name, which the Client cannot tell apart.
///             </description>
///         </item>
///         <item>
///             <term><c>StackUnavailable</c></term>
///             <description><c>LuaError</c>, <c>NotStarted</c>: the call could not begin.</description>
///         </item>
///         <item>
///             <term><c>LuaFailure</c></term>
///             <description><c>LuaError</c>, <c>Started</c>: <c>registerSymbol</c> ran and failed.</description>
///         </item>
///         <item>
///             <term><c>NilResult</c>, <c>InvalidResult</c>, <c>MissingResult</c>, <c>ResultCapacityExceeded</c></term>
///             <description><c>InvalidHostResult</c>, <c>Started</c>: <c>registerSymbol</c> declares no result.</description>
///         </item>
///         <item>
///             <term><c>Unknown</c> or an undefined status</term>
///             <description><c>IndeterminateHostResult</c>, <c>Unknown</c></description>
///         </item>
///     </list>
///     <para>
///         Messages name the category only, never a symbol name or an address. The mapping-totality tests fail when the
///         consumed SDK adds a value.
///     </para>
/// </remarks>
internal static class InspectionMapping
{
	/// <summary>Returns the CheatEngine.SDK options of an address resolution mode.</summary>
	/// <param name="mode">A defined resolution mode; <see cref="InspectionClient" /> refuses any other value.</param>
	/// <returns>
	///     The options whose <see cref="AddressResolutionOptions.Shallow" /> is <see langword="true" /> only for
	///     <see cref="AddressResolutionMode.Shallow" />.
	/// </returns>
	internal static AddressResolutionOptions ToSdkResolutionOptions(AddressResolutionMode mode)
	{
		return new AddressResolutionOptions(mode == AddressResolutionMode.Shallow);
	}

	/// <summary>Returns the failure kind of a name lookup that returned no name.</summary>
	/// <param name="status">The status CheatEngine.SDK reported.</param>
	/// <returns>The failure kind; <see cref="CheatEngineFailureKind.IndeterminateHostResult" /> for an unrecognized value.</returns>
	internal static CheatEngineFailureKind ToNameLookupFailureKind(LuaOperationStatusKind status)
	{
		return status switch
		{
			LuaOperationStatusKind.Success => CheatEngineFailureKind.InvalidHostResult,
			LuaOperationStatusKind.NilResult => CheatEngineFailureKind.NotFound,
			LuaOperationStatusKind.GlobalUnavailable => CheatEngineFailureKind.CapabilityUnavailable,
			LuaOperationStatusKind.LuaFailure => CheatEngineFailureKind.LuaError,
			LuaOperationStatusKind.StackUnavailable => CheatEngineFailureKind.LuaError,
			LuaOperationStatusKind.InvalidResult => CheatEngineFailureKind.InvalidHostResult,
			LuaOperationStatusKind.MissingResult => CheatEngineFailureKind.InvalidHostResult,
			LuaOperationStatusKind.ResultCapacityExceeded => CheatEngineFailureKind.InvalidHostResult,
			_ => CheatEngineFailureKind.IndeterminateHostResult
		};
	}

	/// <summary>Creates the failure of a name lookup that returned no name.</summary>
	/// <param name="operation">The public operation name.</param>
	/// <param name="status">The status CheatEngine.SDK reported.</param>
	/// <returns>
	///     The failure: <see cref="CheatEngineHostEffect.NotStarted" /> for an unavailable global, which Cheat Engine never
	///     called, like every other inspection lookup; otherwise an unknown host effect, since a lookup changes nothing.
	/// </returns>
	internal static CheatEngineFailure NameLookupFailure(string operation, LuaOperationStatusKind status)
	{
		CheatEngineFailureKind kind = ToNameLookupFailureKind(status);
		string message = kind switch
		{
			CheatEngineFailureKind.NotFound => "Cheat Engine did not return a symbol name for the requested address.",
			CheatEngineFailureKind.InvalidHostResult => "Cheat Engine returned an invalid symbol-name result.",
			_ => $"The Cheat Engine symbol-name lookup returned '{status}'."
		};
		CheatEngineHostEffect effect = status == LuaOperationStatusKind.GlobalUnavailable
			? CheatEngineHostEffect.NotStarted
			: CheatEngineHostEffect.Unknown;
		return new CheatEngineFailure(kind, operation, message, null, effect);
	}

	/// <summary>Returns the failure kind of a registration that returned no lease.</summary>
	/// <param name="status">The status CheatEngine.SDK reported.</param>
	/// <returns>The failure kind; <see cref="CheatEngineFailureKind.IndeterminateHostResult" /> for an unrecognized value.</returns>
	internal static CheatEngineFailureKind ToRegistrationFailureKind(LuaOperationStatusKind status)
	{
		return status switch
		{
			LuaOperationStatusKind.Success => CheatEngineFailureKind.IndeterminateHostResult,
			LuaOperationStatusKind.GlobalUnavailable => CheatEngineFailureKind.CapabilityUnavailable,
			LuaOperationStatusKind.LuaFailure => CheatEngineFailureKind.LuaError,
			LuaOperationStatusKind.StackUnavailable => CheatEngineFailureKind.LuaError,
			LuaOperationStatusKind.NilResult => CheatEngineFailureKind.InvalidHostResult,
			LuaOperationStatusKind.InvalidResult => CheatEngineFailureKind.InvalidHostResult,
			LuaOperationStatusKind.MissingResult => CheatEngineFailureKind.InvalidHostResult,
			LuaOperationStatusKind.ResultCapacityExceeded => CheatEngineFailureKind.InvalidHostResult,
			_ => CheatEngineFailureKind.IndeterminateHostResult
		};
	}

	/// <summary>Returns what a registration that returned no lease establishes about the Cheat Engine side effect.</summary>
	/// <param name="status">The status CheatEngine.SDK reported.</param>
	/// <returns>The host effect; <see cref="CheatEngineHostEffect.Unknown" /> unless the status proves more.</returns>
	internal static CheatEngineHostEffect ToRegistrationHostEffect(LuaOperationStatusKind status)
	{
		return status switch
		{
			LuaOperationStatusKind.Success => CheatEngineHostEffect.CleanupUnconfirmed,
			LuaOperationStatusKind.GlobalUnavailable => CheatEngineHostEffect.Unknown,
			LuaOperationStatusKind.LuaFailure => CheatEngineHostEffect.Started,
			LuaOperationStatusKind.StackUnavailable => CheatEngineHostEffect.NotStarted,
			LuaOperationStatusKind.NilResult => CheatEngineHostEffect.Started,
			LuaOperationStatusKind.InvalidResult => CheatEngineHostEffect.Started,
			LuaOperationStatusKind.MissingResult => CheatEngineHostEffect.Started,
			LuaOperationStatusKind.ResultCapacityExceeded => CheatEngineHostEffect.Started,
			_ => CheatEngineHostEffect.Unknown
		};
	}

	/// <summary>Creates the failure of a registration that returned no lease.</summary>
	/// <param name="operation">The public operation name.</param>
	/// <param name="status">The status CheatEngine.SDK reported.</param>
	/// <returns>The failure.</returns>
	internal static CheatEngineFailure RegistrationFailure(string operation, LuaOperationStatusKind status)
	{
		return new CheatEngineFailure(ToRegistrationFailureKind(status), operation,
			$"The symbol registration through the CheatEngine.SDK ownership coordinator returned '{status}' without " +
			"a lease.", null, ToRegistrationHostEffect(status));
	}
}
