using CheatEngine.SDK.Engine.Enums;

namespace CheatEngine.Client.Tables;

/// <summary>Conjunctive managed predicates used to search a bounded address-list snapshot.</summary>
public readonly record struct MemoryRecordSearch
{
	/// <summary>Creates a search that matches all supplied predicates.</summary>
	public MemoryRecordSearch(
		string? descriptionContains = null,
		string? addressExpression = null,
		VariableType? variableType = null,
		bool? isActive = null)
	{
		if (descriptionContains is { Length: 0 })
		{
			throw new ArgumentException("A description search value must be null or non-empty.",
				nameof(descriptionContains));
		}

		if (addressExpression is { Length: 0 })
		{
			throw new ArgumentException("An address expression search value must be null or non-empty.",
				nameof(addressExpression));
		}

		if (descriptionContains is null && addressExpression is null && variableType is null && isActive is null)
		{
			throw new ArgumentException("A memory-record search must specify at least one predicate.",
				nameof(descriptionContains));
		}

		DescriptionContains = descriptionContains;
		AddressExpression = addressExpression;
		VariableType = variableType;
		IsActive = isActive;
	}

	/// <summary>Gets the case-insensitive description substring predicate.</summary>
	public string? DescriptionContains { get; }

	/// <summary>Gets the case-insensitive exact address-expression predicate.</summary>
	public string? AddressExpression { get; }

	/// <summary>Gets the optional exact Cheat Engine value-type predicate.</summary>
	public VariableType? VariableType { get; }

	/// <summary>Gets the optional active/frozen state predicate.</summary>
	public bool? IsActive { get; }
}
