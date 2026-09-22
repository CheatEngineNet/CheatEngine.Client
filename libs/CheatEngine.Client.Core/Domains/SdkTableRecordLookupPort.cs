using CheatEngine.Client.Tables;
using CheatEngine.SDK.Engine.AddressList;

namespace CheatEngine.Client.Core.Domains;

/// <summary>Protected SDK implementation of Address List record lookups.</summary>
internal sealed class SdkTableRecordLookupPort : ITableRecordLookupPort
{
	public RecordLookupStatus TryGetRecord(int index, out MemoryRecordSnapshot record)
	{
		return TryLookup(list => list.TryGetMemoryRecord(index, out MemoryRecord value) ? value : null, out record);
	}

	public RecordLookupStatus TryGetRecord(MemoryRecordId id, out MemoryRecordSnapshot record)
	{
		return TryLookup(list => list.TryGetMemoryRecordById(id, out MemoryRecord value) ? value : null, out record);
	}

	public RecordLookupStatus TryGetSelected(out MemoryRecordSnapshot record)
	{
		return TryLookup(list => list.TryGetSelectedRecord(out MemoryRecord value) ? value : null, out record);
	}

	private static RecordLookupStatus TryLookup(Func<AddressList, MemoryRecord?> selector,
		out MemoryRecordSnapshot record)
	{
		record = default;
		if (!AddressListAccess.TryGetCurrent(out AddressList list))
		{
			return RecordLookupStatus.AddressListUnavailable;
		}

		MemoryRecord? value = selector(list);
		if (!value.HasValue)
		{
			return RecordLookupStatus.NotFound;
		}

		return TableClient.TrySnapshot(value.Value, out record)
			? RecordLookupStatus.Success
			: RecordLookupStatus.InvalidRecord;
	}
}
