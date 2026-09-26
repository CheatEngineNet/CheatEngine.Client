namespace CheatEngine.Client.Runtime;

/// <summary>An immutable, allocation-free-to-enumerate collection of Client capability observations.</summary>
public sealed class ClientCapabilities : IEquatable<ClientCapabilities>
{
	private readonly ClientCapabilityAvailability[] _entries;

	private ClientCapabilities(ClientCapabilityAvailability[] entries)
	{
		_entries = entries;
	}

	/// <summary>Gets an empty capability collection.</summary>
	public static ClientCapabilities Empty
	{
		get;
	} = new([]);

	/// <summary>Gets the number of explicit capability observations.</summary>
	public int Count => _entries.Length;

	/// <summary>Gets the ordered observations as a read-only span.</summary>
	public ReadOnlySpan<ClientCapabilityAvailability> Entries => _entries;

	/// <summary>Tests this collection and another one for equal observations in the same order.</summary>
	/// <param name="other">The collection to compare with.</param>
	/// <returns>
	///     <see langword="true" /> when <paramref name="other" /> holds equal observations in the same order.
	/// </returns>
	public bool Equals(ClientCapabilities? other)
	{
		if (ReferenceEquals(this, other))
		{
			return true;
		}

		if (other is null || _entries.Length != other._entries.Length)
		{
			return false;
		}

		for (int index = 0; index < _entries.Length; index++)
		{
			if (_entries[index] != other._entries[index])
			{
				return false;
			}
		}

		return true;
	}

	/// <summary>Tests this collection and an object for equal observations in the same order.</summary>
	/// <param name="obj">The object to compare with.</param>
	/// <returns>
	///     <see langword="true" /> when <paramref name="obj" /> is a <see cref="ClientCapabilities" /> with equal
	///     observations in the same order.
	/// </returns>
	public override bool Equals(object? obj)
	{
		return obj is ClientCapabilities other && Equals(other);
	}

	/// <summary>Copies and validates a set of distinct Client capability observations.</summary>
	/// <param name="entries">The observations, one per capability, in the order to keep.</param>
	/// <returns>The collection, or <see cref="Empty" /> when <paramref name="entries" /> is empty.</returns>
	/// <exception cref="ArgumentException">
	///     <paramref name="entries" /> holds a <see langword="default" /> observation or names a capability twice.
	/// </exception>
	public static ClientCapabilities Create(ReadOnlySpan<ClientCapabilityAvailability> entries)
	{
		if (entries.IsEmpty)
		{
			return Empty;
		}

		ClientCapabilityAvailability[] copy = entries.ToArray();
		for (int index = 0; index < copy.Length; index++)
		{
			_ = new ClientCapabilityAvailability(copy[index].Capability, copy[index].Evidence);
			for (int previous = 0; previous < index; previous++)
			{
				if (copy[previous].Capability == copy[index].Capability)
				{
					throw new ArgumentException("A Client capability identifier occurs more than once.",
						nameof(entries));
				}
			}
		}

		return new ClientCapabilities(copy);
	}

	/// <summary>Tries to get one explicit capability observation.</summary>
	/// <param name="capability">The identifier of the capability.</param>
	/// <param name="availability">The observation when the collection holds one; otherwise the default value.</param>
	/// <returns>
	///     <see langword="true" /> when the collection holds an observation of <paramref name="capability" />.
	/// </returns>
	public bool TryGet(ClientCapabilityId capability, out ClientCapabilityAvailability availability)
	{
		for (int index = 0; index < _entries.Length; index++)
		{
			if (_entries[index].Capability == capability)
			{
				availability = _entries[index];
				return true;
			}
		}

		availability = default;
		return false;
	}

	/// <summary>Returns a hash code consistent with the equality of the observations.</summary>
	/// <returns>A hash code combined from every observation, in order.</returns>
	public override int GetHashCode()
	{
		HashCode hash = new();
		for (int index = 0; index < _entries.Length; index++)
		{
			hash.Add(_entries[index]);
		}

		return hash.ToHashCode();
	}
}
