using System.Collections.Immutable;

using CheatEngine.Client.Tables;
using CheatEngine.SDK.Engine.AddressList;
using CheatEngine.SDK.Engine.Enums;
using CheatEngine.SDK.Engine.Values;

namespace CheatEngine.Client.Abstractions.Tests.Tables;

public sealed class TableContractTests
{
	/// <summary>Rejects collection bounds that cannot cap table-record materialization.</summary>
	[Theory]
	[InlineData(0)]
	[InlineData(-1)]
	public void MemoryRecordCollectionRequestRejectsNonPositiveBounds(int maximumItems)
	{
		Assert.Throws<ArgumentOutOfRangeException>(() => new MemoryRecordCollectionRequest(maximumItems));
	}

	/// <summary>Retains a valid upper bound for a bounded memory-record collection request.</summary>
	[Fact]
	public void MemoryRecordCollectionRequestRetainsItsPositiveBound()
	{
		MemoryRecordCollectionRequest request = new(32);

		Assert.Equal(32, request.MaximumItems);
	}

	/// <summary>Treats a default record array as an empty copied table, never as an uninitialized one.</summary>
	[Fact]
	public void AddressTableSnapshotTreatsADefaultRecordArrayAsEmpty()
	{
		AddressTableSnapshot snapshot = new(default(ImmutableArray<MemoryRecordSnapshot>));

		Assert.Equal(0, snapshot.RecordCount);
		Assert.False(snapshot.Records.IsDefault);
		Assert.Empty(snapshot.Records);
	}

	/// <summary>Rejects hierarchy limits that cannot bound either breadth or depth.</summary>
	[Theory]
	[InlineData(0, 1)]
	[InlineData(1, 0)]
	[InlineData(-1, 1)]
	[InlineData(1, -1)]
	public void MemoryRecordHierarchyRequestRejectsNonPositiveBounds(int maximumItems, int maximumDepth)
	{
		Assert.Throws<ArgumentOutOfRangeException>(() =>
			new MemoryRecordHierarchyRequest(maximumItems, maximumDepth));
	}

	/// <summary>Requires a non-empty predicate before a record search can be evaluated.</summary>
	[Fact]
	public void MemoryRecordSearchRequiresAtLeastOneMeaningfulPredicate()
	{
		Assert.Throws<ArgumentException>(() => new MemoryRecordSearch(null));
		Assert.Throws<ArgumentException>(() => new MemoryRecordSearch(string.Empty));
		Assert.Throws<ArgumentException>(() => new MemoryRecordSearch(addressExpression: string.Empty));
	}

	/// <summary>Retains every supplied record-search predicate as one conjunctive request.</summary>
	[Fact]
	public void MemoryRecordSearchPreservesConjunctivePredicates()
	{
		MemoryRecordSearch search = new("health", "game.exe+20", VariableType.Dword, true);

		Assert.Equal("health", search.DescriptionContains);
		Assert.Equal("game.exe+20", search.AddressExpression);
		Assert.Equal(VariableType.Dword, search.VariableType);
		Assert.True(search.IsActive);
	}

	/// <summary>Retains copied record data while preserving the separately reported table cardinality.</summary>
	[Fact]
	public void SnapshotCarriesCopiedRecordsAndCardinality()
	{
		MemoryRecordSnapshot record = CreateSnapshot(
			new MemoryRecordId(12),
			0,
			CreateContent(),
			CreateState(Address.FromUInt64(0x1400), true, 2));
		AddressTableSnapshot snapshot = new(ImmutableArray.Create(record));

		Assert.Equal(1, snapshot.RecordCount);
		MemoryRecordSnapshot onlyRecord = Assert.Single(snapshot.Records);
		Assert.Equal(record, onlyRecord);
		Assert.True(onlyRecord.State.IsActive);
		Assert.Equal(2, onlyRecord.State.ChildCount);
	}

	/// <summary>Keeps each copied field once, in its content or state group.</summary>
	[Fact]
	public void MemoryRecordSnapshotGroupsItsFieldsWithoutFlattenedCopies()
	{
		MemoryRecordId id = new(12);
		MemoryRecordContentSnapshot content = CreateContent();
		MemoryRecordStateSnapshot state = CreateState(Address.FromUInt64(0x1400), true, 2);
		MemoryRecordSnapshot snapshot = CreateSnapshot(id, 0, content, state);

		Assert.Equal(id, snapshot.Id);
		Assert.Equal(0, snapshot.Index);
		Assert.Equal(content, snapshot.Content);
		Assert.Equal(state, snapshot.State);
		Assert.Equal(
			["Content", "Id", "Index", "State"],
			typeof(MemoryRecordSnapshot).GetProperties().Select(static property => property.Name)
				.Order(StringComparer.Ordinal));
	}

	/// <summary>Carries the asynchronous activation facts of a copied record state.</summary>
	[Fact]
	public void StateSnapshotCarriesTheAsynchronousActivationFacts()
	{
		MemoryRecordStateSnapshot state = new(null, isActive: true, childCount: 1, isAsync: true,
			isAsyncProcessing: true);
		MemoryRecordStateSnapshot defaults = CreateState();

		Assert.True(state.IsActive);
		Assert.Equal(1, state.ChildCount);
		Assert.True(state.IsAsync);
		Assert.True(state.IsAsyncProcessing);
		Assert.False(defaults.IsAsync);
		Assert.False(defaults.IsAsyncProcessing);
	}

	/// <summary>Carries the script and pointer-offset count of a copied record content.</summary>
	[Fact]
	public void ContentSnapshotCarriesTheScriptAndTheOffsetCount()
	{
		MemoryRecordContentSnapshot script = new("Infinite ammo", string.Empty, string.Empty, VariableType.Dword,
			"[ENABLE]\n[DISABLE]", 0);
		MemoryRecordContentSnapshot pointer = new("Health", "[game.exe+20]+8", "100", VariableType.Dword,
			offsetCount: 2);

		Assert.Equal("[ENABLE]\n[DISABLE]", script.Script);
		Assert.Null(pointer.Script);
		Assert.Equal(2, pointer.OffsetCount);
		Assert.Equal(0, CreateContent().OffsetCount);
	}

	/// <summary>Rejects a negative pointer-offset count in a copied record content.</summary>
	[Theory]
	[InlineData(-1)]
	[InlineData(int.MinValue)]
	public void ContentSnapshotRejectsANegativeOffsetCount(int offsetCount)
	{
		ArgumentOutOfRangeException exception = Assert.Throws<ArgumentOutOfRangeException>(() =>
			new MemoryRecordContentSnapshot("Health", "game.exe+20", "100", VariableType.Dword, null, offsetCount));

		Assert.Equal("offsetCount", exception.ParamName);
	}

	/// <summary>Rejects a record index that cannot identify a valid address-list position.</summary>
	[Theory]
	[InlineData(-1)]
	[InlineData(int.MinValue)]
	public void MemoryRecordSnapshotRejectsNegativeIndex(int index)
	{
		Assert.Throws<ArgumentOutOfRangeException>(() => CreateSnapshot(
			new MemoryRecordId(12),
			index,
			CreateContent(),
			CreateState()));
	}

	/// <summary>Rejects a negative immediate-child count in a copied record state.</summary>
	[Theory]
	[InlineData(-1)]
	[InlineData(int.MinValue)]
	public void StateSnapshotRejectsNegativeChildCount(int childCount)
	{
		Assert.Throws<ArgumentOutOfRangeException>(() => CreateState(childCount: childCount));
	}

	/// <summary>Rejects required text fields that would make copied record content incomplete.</summary>
	[Fact]
	public void ContentSnapshotRejectsNullRequiredValues()
	{
		ArgumentNullException description = Assert.Throws<ArgumentNullException>(() => new MemoryRecordContentSnapshot(
			Null<string>(),
			"game.exe+20",
			"100",
			VariableType.Dword));
		ArgumentNullException addressExpression = Assert.Throws<ArgumentNullException>(() =>
			new MemoryRecordContentSnapshot(
				"Health",
				Null<string>(),
				"100",
				VariableType.Dword));
		ArgumentNullException value = Assert.Throws<ArgumentNullException>(() => new MemoryRecordContentSnapshot(
			"Health",
			"game.exe+20",
			Null<string>(),
			VariableType.Dword));

		Assert.Equal("description", description.ParamName);
		Assert.Equal("addressExpression", addressExpression.ParamName);
		Assert.Equal("value", value.ParamName);
	}

	/// <summary>Normalizes default child arrays to empty both during construction and with-expression updates.</summary>
	[Fact]
	public void HierarchySnapshotNormalizesDefaultChildrenDuringConstructionAndWithUpdate()
	{
		MemoryRecordHierarchySnapshot hierarchy = new(CreateSnapshot(
			new MemoryRecordId(12),
			0,
			CreateContent(),
			CreateState()), default);
		MemoryRecordHierarchySnapshot updatedHierarchy = hierarchy with
		{
			Children = default
		};
		MemoryRecordHierarchySnapshot uninitializedHierarchy = default;

		Assert.False(hierarchy.Children.IsDefault);
		Assert.Empty(hierarchy.Children);
		Assert.False(updatedHierarchy.Children.IsDefault);
		Assert.Empty(updatedHierarchy.Children);
		Assert.False(uninitializedHierarchy.Children.IsDefault);
		Assert.Empty(uninitializedHierarchy.Children);
	}

	/// <summary>Rejects default record content before it can leak null text fields through a snapshot.</summary>
	[Fact]
	public void MemoryRecordSnapshotRejectsDefaultContent()
	{
		ArgumentException exception = Assert.Throws<ArgumentException>(() => new MemoryRecordSnapshot(
			new MemoryRecordId(12),
			0,
			default,
			CreateState()));

		Assert.Equal("content", exception.ParamName);
	}

	/// <summary>Preserves the root record and recursively copied child snapshots in hierarchy order.</summary>
	[Fact]
	public void HierarchySnapshotRetainsItsCopiedRootAndChildren()
	{
		MemoryRecordSnapshot root = CreateSnapshot(
			new MemoryRecordId(12),
			0,
			CreateContent(),
			CreateState(childCount: 1));
		MemoryRecordSnapshot child = CreateSnapshot(
			new MemoryRecordId(13),
			1,
			new MemoryRecordContentSnapshot("Ammo", "game.exe+24", "50", VariableType.Dword),
			CreateState());
		MemoryRecordHierarchySnapshot hierarchy = new(root,
			ImmutableArray.Create(new MemoryRecordHierarchySnapshot(child, [])));

		Assert.Equal(root, hierarchy.Record);
		MemoryRecordHierarchySnapshot onlyChild = Assert.Single(hierarchy.Children);
		Assert.Equal(child, onlyChild.Record);
		Assert.Empty(onlyChild.Children);
	}

	/// <summary>Retains an explicitly supplied parent record identifier for a new table definition.</summary>
	[Fact]
	public void MemoryRecordDefinitionRetainsAnOptionalParentId()
	{
		MemoryRecordDefinition definition = new("Ammo", "game.exe+24", "50", VariableType.Dword,
			new MemoryRecordId(12));

		Assert.Equal(new MemoryRecordId(12), definition.ParentId);
	}

	/// <summary>Defaults a new record definition to the address-list root when no parent is supplied.</summary>
	[Fact]
	public void MemoryRecordDefinitionDefaultsToTheAddressListRoot()
	{
		MemoryRecordDefinition definition = new("Ammo", "game.exe+24", "50", VariableType.Dword);

		Assert.Null(definition.ParentId);
	}

	private static MemoryRecordSnapshot CreateSnapshot(
		MemoryRecordId id,
		int index,
		MemoryRecordContentSnapshot content,
		MemoryRecordStateSnapshot state)
	{
		return new MemoryRecordSnapshot(id, index, content, state);
	}

	private static MemoryRecordContentSnapshot CreateContent()
	{
		return new MemoryRecordContentSnapshot("Health", "game.exe+20", "100", VariableType.Dword);
	}

	private static MemoryRecordStateSnapshot CreateState(Address? currentAddress = null, bool isActive = false,
		int childCount = 0)
	{
		return new MemoryRecordStateSnapshot(currentAddress, isActive, childCount);
	}

	private static T Null<T>() where T : class
	{
		return default!;
	}
}
