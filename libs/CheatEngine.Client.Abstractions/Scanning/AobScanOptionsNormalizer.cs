using System.Globalization;

using CheatEngine.SDK.Engine.Enums;
using CheatEngine.SDK.Engine.Scanning.Aob;

namespace CheatEngine.Client.Scanning;

/// <summary>Validates the documented CE AOB protection and alignment text before an SDK call.</summary>
internal static class AobScanOptionsNormalizer
{
	internal static AobScanOptions Normalize(AobScanOptions options)
	{
		string? protection = NormalizeProtection(options.ProtectionFlags);
		string? alignment = NormalizeAlignmentParameter(options.AlignmentMethod, options.AlignmentParameter);
		return new AobScanOptions(protection, options.AlignmentMethod, alignment);
	}

	private static string? NormalizeProtection(string? value)
	{
		if (value is null || value.Length == 0)
		{
			return value;
		}

		Span<char> modes = stackalloc char[3];
		int seen = 0;
		for (int index = 0; index < value.Length;)
		{
			AddProtectionClause(value, ref index, modes, ref seen);
		}

		return FormatProtection(modes, seen);
	}

	private static void AddProtectionClause(string value, ref int index, Span<char> modes, ref int seen)
	{
		if (index + 1 >= value.Length)
		{
			throw new ArgumentException(
				"An AOB protection expression consists of +, -, or * followed by X, C, or W.", nameof(value));
		}

		char mode = value[index++];
		char flag = value[index++];
		if (mode is not ('+' or '-' or '*'))
		{
			throw new ArgumentException(
				"An AOB protection expression consists of unique +, -, or * X/C/W clauses.", nameof(value));
		}

		int slot = GetProtectionSlot(flag);
		if (slot < 0 || IsProtectionSlotPresent(seen, slot))
		{
			throw new ArgumentException(
				"An AOB protection expression consists of unique +, -, or * X/C/W clauses.", nameof(value));
		}

		modes[slot] = mode;
		seen |= 1 << slot;
	}

	private static string FormatProtection(ReadOnlySpan<char> modes, int seen)
	{
		Span<char> normalized = stackalloc char[6];
		int written = 0;
		for (int slot = 0; slot < modes.Length; slot++)
		{
			if (!IsProtectionSlotPresent(seen, slot))
			{
				continue;
			}

			normalized[written++] = modes[slot];
			normalized[written++] = GetProtectionFlag(slot);
		}

		return new string(normalized[..written]);
	}

	private static int GetProtectionSlot(char flag)
	{
		return flag switch
		{
			'X' or 'x' => 0,
			'C' or 'c' => 1,
			'W' or 'w' => 2,
			_ => -1
		};
	}

	private static char GetProtectionFlag(int slot)
	{
		return slot switch { 0 => 'X', 1 => 'C', _ => 'W' };
	}

	private static bool IsProtectionSlotPresent(int seen, int slot)
	{
		return (seen & (1 << slot)) != 0;
	}

	private static string? NormalizeAlignmentParameter(FastScanMethod method, string? value)
	{
		return method switch
		{
			FastScanMethod.NotAligned => value,
			FastScanMethod.Aligned => NormalizeDivisor(value),
			FastScanMethod.LastDigits => NormalizeHexSuffix(value),
			_ => throw new ArgumentOutOfRangeException(nameof(method), method,
				"AOB alignment must use a documented Cheat Engine fast-scan method.")
		};
	}

	private static string NormalizeDivisor(string? value)
	{
		if (string.IsNullOrEmpty(value) ||
		    !ulong.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out ulong divisor) || divisor == 0)
		{
			throw new ArgumentException(
				"An aligned AOB scan requires a positive decimal alignment divisor.", nameof(value));
		}

		return divisor.ToString(CultureInfo.InvariantCulture);
	}

	private static string NormalizeHexSuffix(string? value)
	{
		if (string.IsNullOrEmpty(value) || value.Length > sizeof(ulong) * 2)
		{
			throw new ArgumentException(
				"A last-digits AOB scan requires one to sixteen hexadecimal digits.", nameof(value));
		}

		Span<char> normalized = stackalloc char[value.Length];
		for (int index = 0; index < value.Length; index++)
		{
			char character = value[index];
			if (character is >= '0' and <= '9' or >= 'A' and <= 'F')
			{
				normalized[index] = character;
				continue;
			}

			if (character is >= 'a' and <= 'f')
			{
				normalized[index] = (char) (character - ('a' - 'A'));
				continue;
			}

			throw new ArgumentException(
				"A last-digits AOB scan requires one to sixteen hexadecimal digits.", nameof(value));
		}

		return new string(normalized);
	}
}
