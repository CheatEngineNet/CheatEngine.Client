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
		MemoryRecordSnapshot record = new(new MemoryRecordId(12), 0, "Health", "game.exe+20", "100",
			VariableType.Dword, Address.FromUInt64(0x1400), true, 2);
		AddressTableSnapshot snapshot = new(ImmutableArray.Create(record));

		Assert.Equal(1, snapshot.RecordCount);
		Assert.Single(snapshot.Records);
		Assert.Equal(record, snapshot.Records[0]);
		Assert.True(snapshot.Records[0].IsActive);
		Assert.Equal(2, snapshot.Records[0].ChildCount);
	}

	[Fact]
	public void MemoryRecordSnapshotRejectsNegativeChildCount()
	{
		Assert.Throws<ArgumentOutOfRangeException>(() => new MemoryRecordSnapshot(new MemoryRecordId(12), 0, "Health",
			"game.exe+20", "100", VariableType.Dword, null, childCount: -1));
	}

	[Fact]
	public void HierarchySnapshotRetainsItsCopiedRootAndChildren()
	{
		MemoryRecordSnapshot root = new(new MemoryRecordId(12), 0, "Health", "game.exe+20", "100",
			VariableType.Dword, null, childCount: 1);
		MemoryRecordSnapshot child = new(new MemoryRecordId(13), 1, "Ammo", "game.exe+24", "50",
			VariableType.Dword, null);
		MemoryRecordHierarchySnapshot hierarchy = new(root,
			ImmutableArray.Create(new MemoryRecordHierarchySnapshot(child, [])));

		Assert.Equal(root, hierarchy.Record);
		Assert.Single(hierarchy.Children);
		Assert.Equal(child, hierarchy.Children[0].Record);
		Assert.Empty(hierarchy.Children[0].Children);
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
}
