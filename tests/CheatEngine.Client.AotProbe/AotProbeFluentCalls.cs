using System.Collections.Immutable;

using CheatEngine.Client.Memory;
using CheatEngine.Client.Scanning;
using CheatEngine.SDK.Engine.Inspection;
using CheatEngine.SDK.Engine.Values;

namespace CheatEngine.Client.AotProbe;

/// <summary>
///     Calls every public member of CheatEngine.Client.Fluent under Native AOT, against in-process fakes of the memory
///     service and the pattern scanner, and checks what each call returns.
/// </summary>
/// <remarks>
///     AotProbeCoverageTests (CheatEngine.Client.Repository.Tests) reads this file: every public Fluent type has an
///     <c>Exercise&lt;TypeName&gt;</c> method here that calls each public member of that type, and <see cref="Run" />
///     calls every <c>Exercise</c> method. The test counts call sites, not overloads, so each overload keeps its own
///     call with an argument of that overload's type. Each call's effect is checked before a later call replaces it.
/// </remarks>
internal static class AotProbeFluentCalls
{
	private const int Int32Value = 0x1234_5678;
	private const long Int64Value = 0x0102_0304_0506_0708;
	private const string Text = "probe";
	private const int Utf8Bytes = 16;
	private const int Utf16Bytes = 32;
	private const string Pattern = "48 8B ?? 89";

	/// <summary>Runs every Fluent call and reports whether each one returned what the fakes hold.</summary>
	/// <returns><see langword="true" /> when every call behaved as expected.</returns>
	internal static bool Run()
	{
		AotProbeTargetMemory memory = new();
		AotProbePatternScanner scanner = new();
		return ExerciseCheatEngineMemoryFluentExtensions(memory)
			   && ExerciseMemoryAddressBuilder(memory)
			   && ExerciseMemoryPointerChainBuilder(memory)
			   && ExerciseMemoryPrimitiveBatchBuilder(memory)
			   && ExerciseCheatEngineAobFluentExtensions(scanner)
			   && ExerciseAobScanBuilder(scanner)
			   && ExerciseAobFirstMatchBuilder(scanner)
			   && ExerciseAobManyMatchBuilder(scanner)
			   && ExerciseAobSingleMatchBuilder(scanner);
	}

	private static Address Slot(int offset)
	{
		return AotProbeTargetMemory.BaseAddress + offset;
	}

	private static bool ExerciseCheatEngineMemoryFluentExtensions(AotProbeTargetMemory memory)
	{
		MemoryAddressBuilder address = memory.At(Slot(0x00));
		MemoryPrimitiveBatchBuilder<byte> batch = memory.Batch<byte>();
		return address.Address == Slot(0x00)
			   && batch.TryRead([Slot(0x00)], out ImmutableArray<byte> values, out _)
			   && values.Length == 1;
	}

	private static bool ExerciseMemoryAddressBuilder(AotProbeTargetMemory memory)
	{
		MemoryAddressBuilder int32 = memory.At(Slot(0x00));
		int32.Write(Int32Value);
		MemoryAddressBuilder int64 = memory.At(Slot(0x08));
		bool primitives = int32.Address == Slot(0x00)
						  && int32.Read<int>() == Int32Value
						  && int64.TryWrite(Int64Value, out _)
						  && int64.TryRead(out long readInt64, out _)
						  && readInt64 == Int64Value;

		MemoryAddressBuilder bytes = memory.At(Slot(0x10));
		bytes.WriteBytes([1, 2, 3, 4]);
		MemoryAddressBuilder moreBytes = memory.At(Slot(0x18));
		bool byteRuns = bytes.ReadBytes(4) is [1, 2, 3, 4]
						&& moreBytes.TryWriteBytes([5, 6], out _)
						&& moreBytes.TryReadBytes(2, out ImmutableArray<byte> readBytes, out _)
						&& readBytes is [5, 6];

		MemoryAddressBuilder utf8 = memory.At(Slot(0x20));
		utf8.WriteString(Text, Utf8Bytes, MemoryStringEncoding.Utf8);
		MemoryAddressBuilder utf16 = memory.At(Slot(0x30));
		bool strings = utf8.ReadString(Utf8Bytes, MemoryStringEncoding.Utf8) == Text
					   && utf16.TryWriteString(Text, Utf16Bytes, MemoryStringEncoding.Utf16, out _)
					   && utf16.TryReadString(Utf16Bytes, MemoryStringEncoding.Utf16, out string? readUtf16, out _)
					   && readUtf16 == Text;

		AotProbePointCodec codec = new();
		AotProbePoint point = new(-7, 11);
		AotProbePoint moved = new(point.X, 12);
		MemoryAddressBuilder custom = memory.At(Slot(0x50));
		custom.WriteWith(point, codec);
		MemoryAddressBuilder moreCustom = memory.At(Slot(0x58));
		bool codecs = custom.ReadWith(codec) == point
					  && moreCustom.TryWriteWith(moved, codec, out _)
					  && moreCustom.TryReadWith(codec, out AotProbePoint readPoint, out _)
					  && readPoint == moved;

		MemoryPointerChainBuilder chain = memory.At(Slot(0x60)).Follow([0x08]);
		return primitives && byteRuns && strings && codecs && chain.Request.BaseAddress == Slot(0x60);
	}

	private static bool ExerciseMemoryPointerChainBuilder(AotProbeTargetMemory memory)
	{
		memory.At(Slot(0x60)).Write(Slot(0x70).Value);
		MemoryPointerChainBuilder chain = memory.At(Slot(0x60)).Follow([0x08]);
		return chain.Request.Offsets is [0x08]
			   && chain.Resolve() == Slot(0x78)
			   && chain.TryResolve(out Address resolved, out _)
			   && resolved == Slot(0x78);
	}

	private static bool ExerciseMemoryPrimitiveBatchBuilder(AotProbeTargetMemory memory)
	{
		MemoryPrimitiveBatchBuilder<int> batch = memory.Batch<int>();
		batch.Write([new MemoryAddressValue<int>(Slot(0x80), 1), new MemoryAddressValue<int>(Slot(0x84), 2)]);
		return batch.Read([Slot(0x80), Slot(0x84)]) is [1, 2]
			   && batch.TryWrite([new MemoryAddressValue<int>(Slot(0x88), 3)], out _)
			   && batch.TryRead([Slot(0x84), Slot(0x88)], out ImmutableArray<int> values, out _)
			   && values is [2, 3];
	}

	private static bool ExerciseCheatEngineAobFluentExtensions(AotProbePatternScanner scanner)
	{
		AobPattern pattern = new(Pattern);
		return scanner.Aob(Pattern).Pattern == pattern && scanner.Aob(pattern).Pattern == pattern;
	}

	private static bool ExerciseAobScanBuilder(AotProbePatternScanner scanner)
	{
		ModuleName named = new("game.exe");
		ModuleName module = new("other.dll");
		AobScanRange range = new(new Address(0x1000), new Address(0x9000));

		// Each builder replaces a setting of the previous one, so each step is checked on its own builder.
		AobScanBuilder byName = scanner.Aob(Pattern).InModule(named.Value);
		AobScanBuilder byModule = byName.InModule(module).InRange(range.Start, range.End);
		AobScanBuilder executable = byModule.Executable();
		AobScanBuilder writable = executable.Writable();
		AobScanBuilder anyProtection = writable.WithProtection(default);
		AobScanBuilder lastDigits = anyProtection.LastDigits("0f");
		AobScanBuilder alignedTo = lastDigits.AlignedTo(4);
		AobScanBuilder scan = alignedTo.WithAlignment(ScanAlignment.AlignedTo(8));
		bool eachStep = byName.Module == named
						&& byModule.Module == module
						&& byModule.Range == range
						&& executable.Protection == new ScanProtectionFilter(ScanProtectionRequirement.Required,
							ScanProtectionRequirement.Excluded, ScanProtectionRequirement.Excluded)
						&& writable.Protection == new ScanProtectionFilter(ScanProtectionRequirement.Unspecified,
							ScanProtectionRequirement.Excluded, ScanProtectionRequirement.Required)
						&& anyProtection.Protection.IsUnspecified
						&& lastDigits.Alignment is { Mode: ScanAlignmentMode.LastDigits, Digits: "0F" }
						&& alignedTo.Alignment is { Mode: ScanAlignmentMode.AlignedTo, Divisor: 4 }
						&& scan.Alignment is { Mode: ScanAlignmentMode.AlignedTo, Divisor: 8 };
		AobFirstMatchBuilder first = scan.FirstOrNone();
		bool firstCopiesOne = first.Execute() == AotProbePatternScanner.Match
							  && scanner.LastRequest.MaximumResults == 1;
		AobManyMatchBuilder many = scan.Take(3);
		bool manyCopiesThree = many.Execute().Matches.Length == 1 && scanner.LastRequest.MaximumResults == 3;
		AobSingleMatchBuilder single = scan.RequireSingle();
		bool singleCopiesTwo = single.Execute() == AotProbePatternScanner.Match
							   && scanner.LastRequest.MaximumResults == 2;
		return eachStep
			   && scan.Pattern == new AobPattern(Pattern)
			   && scan.Module == module
			   && scan.Range == range
			   && scan.Protection.IsUnspecified
			   && firstCopiesOne
			   && manyCopiesThree
			   && singleCopiesTwo;
	}

	private static bool ExerciseAobFirstMatchBuilder(AotProbePatternScanner scanner)
	{
		AobFirstMatchBuilder first = scanner.Aob("90").FirstOrNone();
		return first.Execute() == AotProbePatternScanner.Match
			   && first.TryExecute(out Address? address, out _)
			   && address == AotProbePatternScanner.Match;
	}

	private static bool ExerciseAobManyMatchBuilder(AotProbePatternScanner scanner)
	{
		AobManyMatchBuilder many = scanner.Aob("90").Take(2);
		AobScanResult executed = many.Execute();
		return executed.Matches.Length == 1
			   && executed.Matches[0] == AotProbePatternScanner.Match
			   && many.TryExecute(out AobScanResult result, out _)
			   && result is { IsTruncated: false, Matches.Length: 1 };
	}

	private static bool ExerciseAobSingleMatchBuilder(AotProbePatternScanner scanner)
	{
		AobSingleMatchBuilder single = scanner.Aob("90").RequireSingle();
		return single.Execute() == AotProbePatternScanner.Match
			   && single.TryExecute(out Address address, out _)
			   && address == AotProbePatternScanner.Match;
	}
}
