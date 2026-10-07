using System.Diagnostics.CodeAnalysis;

namespace CheatEngine.Client.Lua;

/// <summary>Creates Client-owned optional Lua values without exposing the SDK marshalling type.</summary>
public static class LuaOptional
{
	/// <summary>Creates an omitted value, which passes no Lua argument or represents no Lua result.</summary>
	/// <typeparam name="T">The non-null copied value type.</typeparam>
	/// <returns>An omitted optional value.</returns>
	public static LuaOptional<T> Omitted<T>()
		where T : notnull
	{
		return default;
	}

	/// <summary>Creates an explicit Lua <c>nil</c> value.</summary>
	/// <typeparam name="T">The non-null copied value type.</typeparam>
	/// <returns>An optional value representing explicit Lua <c>nil</c>.</returns>
	public static LuaOptional<T> Nil<T>()
		where T : notnull
	{
		return new LuaOptional<T>(LuaOptionalState.Nil, default!);
	}

	/// <summary>Creates a present Lua value.</summary>
	/// <typeparam name="T">The non-null copied value type.</typeparam>
	/// <param name="value">The present non-null value.</param>
	/// <returns>An optional value containing <paramref name="value" />.</returns>
	/// <exception cref="ArgumentNullException"><paramref name="value" /> is <see langword="null" />.</exception>
	public static LuaOptional<T> Of<T>(T value)
		where T : notnull
	{
		ArgumentNullException.ThrowIfNull(value);
		return new LuaOptional<T>(LuaOptionalState.Value, value);
	}
}

/// <summary>One optional Lua value: omitted, explicit <c>nil</c>, or a present non-null value.</summary>
/// <typeparam name="T">The copied Client value type.</typeparam>
public readonly struct LuaOptional<T> : IEquatable<LuaOptional<T>>
	where T : notnull
{
	private readonly LuaOptionalState _state;
	private readonly T _value;

	internal LuaOptional(LuaOptionalState state, T value)
	{
		_state = state;
		_value = value;
	}

	/// <summary>Gets whether no value was passed or returned.</summary>
	public bool IsOmitted => _state == LuaOptionalState.Omitted;

	/// <summary>Gets whether Lua explicitly passed or returned <c>nil</c>.</summary>
	public bool IsNil => _state == LuaOptionalState.Nil;

	/// <summary>Gets whether a non-null value was passed or returned.</summary>
	public bool HasValue => _state == LuaOptionalState.Value;

	/// <summary>Gets the present value, or the safe default when the value is omitted or <c>nil</c>.</summary>
	/// <remarks>Use <see cref="HasValue" /> or <see cref="TryGetValue" /> to distinguish an absent value from a present default.</remarks>
	public T? Value => HasValue ? _value : default;

	/// <summary>Gets the value when it is present.</summary>
	/// <param name="value">The present value, or the safe default when omitted or <c>nil</c>.</param>
	/// <returns><see langword="true" /> when a present value was copied to <paramref name="value" />.</returns>
	public bool TryGetValue([NotNullWhen(true)] out T? value)
	{
		value = HasValue ? _value : default!;
		return HasValue;
	}

	/// <summary>Compares this optional value's state and present value with another optional value.</summary>
	/// <param name="other">The optional value to compare.</param>
	/// <returns><see langword="true" /> when the states and any present values are equal.</returns>
	public bool Equals(LuaOptional<T> other)
	{
		return _state == other._state && (!HasValue || EqualityComparer<T>.Default.Equals(_value, other._value));
	}

	/// <summary>Compares this optional value with an object of the same optional type.</summary>
	/// <param name="obj">The object to compare.</param>
	/// <returns><see langword="true" /> when the object has the same state and any present value.</returns>
	public override bool Equals(object? obj)
	{
		return obj is LuaOptional<T> other && Equals(other);
	}

	/// <summary>Computes a hash of this optional value's state and any present value.</summary>
	/// <returns>The hash code consistent with optional-value equality.</returns>
	public override int GetHashCode()
	{
		return HasValue ? HashCode.Combine((int) _state, _value) : (int) _state;
	}

	/// <summary>Compares the state and, for present values, the value.</summary>
	/// <param name="left">The first optional value.</param>
	/// <param name="right">The second optional value.</param>
	/// <returns><see langword="true" /> when both optional values are equal.</returns>
	public static bool operator ==(LuaOptional<T> left, LuaOptional<T> right)
	{
		return left.Equals(right);
	}

	/// <summary>Compares the state and, for present values, the value.</summary>
	/// <param name="left">The first optional value.</param>
	/// <param name="right">The second optional value.</param>
	/// <returns><see langword="true" /> when the optional values differ.</returns>
	public static bool operator !=(LuaOptional<T> left, LuaOptional<T> right)
	{
		return !left.Equals(right);
	}
}

internal enum LuaOptionalState : byte
{
	Omitted,
	Nil,
	Value
}
