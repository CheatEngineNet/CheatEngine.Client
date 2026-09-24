using CheatEngine.SDK.Lua.Calls;

namespace CheatEngine.Client.Core.Domains;

/// <summary>
///     The copied result of <c>SymbolRegistry.TryRegisterOwned</c>: the protected registration status and, only when
///     Cheat Engine registered the name and the SDK published its lease, the handle that releases it.
/// </summary>
/// <param name="Status">The protected <c>registerSymbol</c> status that CheatEngine.SDK reported.</param>
/// <param name="Handle">The release handle, or <see langword="null" /> when no lease was created.</param>
internal readonly record struct SymbolRegistrationAttempt(LuaOperationStatus Status, ISymbolRegistrationHandle? Handle);
