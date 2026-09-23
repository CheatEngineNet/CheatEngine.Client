using System.Collections;
using System.Collections.Immutable;

namespace CheatEngine.Client.SourceGenerators.Lua.Model;

/// <summary>
///     An immutable array with ordinal, element-wise value equality, so incremental pipeline models that contain sequences
///     compare equal when their content is equal.
/// </summary>
/// <typeparam name="T">The element type; its own equality defines the sequence equality.</typeparam>
/// <remarks>
///     <see cref="ImmutableArray{T}" /> compares by reference, which makes every re-parsed model look modified and defeats
///     generator caching (https://github.com/dotnet/roslyn/blob/main/docs/features/incremental-generators.cookbook.md).
/// </remarks>
internal readonly struct EquatableArray<T> : IEquatable<EquatableArray<T>>, IEnumerable<T>
	where T : IEquatable<T>
{
	private readonly T[]? _items;

	public EquatableArray(ImmutableArray<T> items)
	{
		_items = items.IsDefaultOrEmpty ? null : items.ToArray();
	}

	public static EquatableArray<T> Empty => default;

	public int Length => _items?.Length ?? 0;

	public bool IsEmpty => Length == 0;

	public T this[int index] => (_items ?? throw new ArgumentOutOfRangeException(nameof(index)))[index];

	public static bool operator ==(EquatableArray<T> left, EquatableArray<T> right)
	{
		return left.Equals(right);
	}

	public static bool operator !=(EquatableArray<T> left, EquatableArray<T> right)
	{
		return !left.Equals(right);
	}

	public bool Equals(EquatableArray<T> other)
	{
		int length = Length;
		if (length != other.Length)
		{
			return false;
		}

		for (int index = 0; index < length; index++)
		{
			if (!EqualityComparer<T>.Default.Equals(_items![index], other._items![index]))
			{
				return false;
			}
		}

		return true;
	}

	public override bool Equals(object? obj)
	{
		return obj is EquatableArray<T> other && Equals(other);
	}

	public override int GetHashCode()
	{
		// netstandard2.0 has no System.HashCode: combine with the classic multiplicative scheme.
		unchecked
		{
			int hash = 17;
			if (_items is not null)
			{
				foreach (T item in _items)
				{
					hash = (hash * 31) + (item is null ? 0 : EqualityComparer<T>.Default.GetHashCode(item));
				}
			}

			return hash;
		}
	}

	public IEnumerator<T> GetEnumerator()
	{
		return ((IEnumerable<T>) (_items ?? [])).GetEnumerator();
	}

	IEnumerator IEnumerable.GetEnumerator()
	{
		return GetEnumerator();
	}
}

/// <summary>Factory helpers for <see cref="EquatableArray{T}" />.</summary>
internal static class EquatableArray
{
	public static EquatableArray<T> Create<T>(ImmutableArray<T> items)
		where T : IEquatable<T>
	{
		return new EquatableArray<T>(items);
	}

	public static EquatableArray<T> Create<T>(params T[] items)
		where T : IEquatable<T>
	{
		return new EquatableArray<T>(ImmutableArray.Create(items));
	}
}
