using System.Collections.Immutable;

using CheatEngine.Client.Tables;
using CheatEngine.SDK.Engine.AddressList;
using CheatEngine.SDK.Engine.Enums;
using CheatEngine.SDK.Engine.Values;

namespace CheatEngine.Client.Tests.Tables;

public sealed class TableContractTests
{
	[Theory]
	[InlineData(0)]
	[InlineData(-1)]
	public void MemoryRecordCollectionRequestRejectsNonPositiveBounds(int maximumItems)
	{
		Assert.Throws<ArgumentOutOfRangeException>(() => new MemoryRecordCollectionRequest(maximumItems));
	}

	[Fact]
	public void MemoryRecordCollectionRequestRetainsItsPositiveBound()
	{
		MemoryRecordCollectionRequest request = new(32);

		Assert.Equal(32, request.MaximumItems);
	}

	[Fact]
	public void CardinalityOnlyAddressTableSnapshotRetainsTheCountWithoutMaterializingRecords()
	{
		AddressTableSnapshot snapshot = new(3);

		Assert.Equal(3, snapshot.RecordCount);
		Assert.Empty(snapshot.Records);
	}

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

	[Fact]
	public void MemoryRecordSearchRequiresAtLeastOneMeaningfulPredicate()
	{
		Assert.Throws<ArgumentException>(() => new MemoryRecordSearch(null));
		Assert.Throws<ArgumentException>(() => new MemoryRecordSearch(string.Empty));
		Assert.Throws<ArgumentException>(() => new MemoryRecordSearch(addressExpression: string.Empty));
	}

	[Fact]
	public void MemoryRecordSearchPreservesConjunctivePredicates()
	{
		MemoryRecordSearch search = new("health", "game.exe+20", VariableType.Dword, true);

		Assert.Equal("health", search.DescriptionContains);
		Assert.Equal("game.exe+20", search.AddressExpression);
		Assert.Equal(VariableType.Dword, search.VariableType);
		Assert.True(search.IsActive);
	}

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
		Assert.True(onlyRecord.IsActive);
		Assert.Equal(2, onlyRecord.ChildCount);
	}

	[Fact]
	public void MemoryRecordSnapshotForwardsContentAndStateComponentsToItsExistingLeafProperties()
	{
		MemoryRecordId id = new(12);
		Address address = Address.FromUInt64(0x1400);
		MemoryRecordContentSnapshot content = CreateContent();
		MemoryRecordStateSnapshot state = CreateState(address, true, 2);
		MemoryRecordSnapshot snapshot = CreateSnapshot(id, 0, content, state);

		Assert.Equal(id, snapshot.Id);
		Assert.Equal(0, snapshot.Index);
		Assert.Equal(content, snapshot.Content);
		Assert.Equal(state, snapshot.State);
		Assert.Equal(content.Description, snapshot.Description);
		Assert.Equal(content.AddressExpression, snapshot.AddressExpression);
		Assert.Equal(content.Value, snapshot.Value);
		Assert.Equal(content.VariableType, snapshot.VariableType);
		Assert.Equal(state.CurrentAddress, snapshot.CurrentAddress);
		Assert.Equal(state.IsActive, snapshot.IsActive);
		Assert.Equal(state.ChildCount, snapshot.ChildCount);
	}

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

	[Theory]
	[InlineData(-1)]
	[InlineData(int.MinValue)]
	public void StateSnapshotRejectsNegativeChildCount(int childCount)
	{
		Assert.Throws<ArgumentOutOfRangeException>(() => CreateState(childCount: childCount));
	}

	[Fact]
	public void ContentSnapshotRejectsNullRequiredValues()
	{
		ArgumentNullException description = Assert.Throws<ArgumentNullException>(() => new MemoryRecordContentSnapshot(
			Null<string>(),
			"game.exe+20",
			"100",
			VariableType.Dword));
		ArgumentNullException addressExpression = Assert.Throws<ArgumentNullException>(() => new MemoryRecordContentSnapshot(
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

	[Fact]
	public void MemoryRecordDefinitionRetainsAnOptionalParentId()
	{
		MemoryRecordDefinition definition = new("Ammo", "game.exe+24", "50", VariableType.Dword,
			new MemoryRecordId(12));

		Assert.Equal(new MemoryRecordId(12), definition.ParentId);
	}

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

	private static MemoryRecordStateSnapshot CreateState(Address? currentAddress = null, bool isActive = false, int childCount = 0)
	{
		return new MemoryRecordStateSnapshot(currentAddress, isActive, childCount);
	}

	private static T Null<T>() where T : class => default!;
}
