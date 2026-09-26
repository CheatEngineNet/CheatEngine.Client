using System.Globalization;

namespace CheatEngine.Client.Core.Tests.TestSupport;

/// <summary>Proves that a Client mapping over an SDK outcome enum is total and fails closed.</summary>
/// <remarks>
///     Every value that the consumed CheatEngine.SDK defines must reach its dedicated Client value, and a value the SDK
///     could add later must reach the conservative fallback. A new value in a candidate SDK package therefore fails the
///     calling test instead of silently becoming a fallback in production.
/// </remarks>
internal static class MappingTotality
{
	/// <summary>Asserts that <typeparamref name="TEnum" /> is mapped totally and that an unknown value fails closed.</summary>
	/// <typeparam name="TEnum">The SDK enum the mapping consumes.</typeparam>
	/// <param name="isMapped">Whether a defined value reaches its dedicated Client value.</param>
	/// <param name="failsClosed">Whether a value outside <typeparamref name="TEnum" /> reaches the conservative fallback.</param>
	internal static void AssertTotal<TEnum>(Func<TEnum, bool> isMapped, Func<TEnum, bool> failsClosed)
		where TEnum : struct, Enum
	{
		ArgumentNullException.ThrowIfNull(isMapped);
		ArgumentNullException.ThrowIfNull(failsClosed);

		TEnum[] values = Enum.GetValues<TEnum>();
		string[] unmapped = [.. values.Where(value => !isMapped(value)).Select(static value => value.ToString())];
		TEnum undefined = Undefined<TEnum>();

		Assert.NotEmpty(values);
		Assert.True(unmapped.Length == 0,
			$"These {typeof(TEnum).FullName} values are not mapped: {string.Join(", ", unmapped)}");
		Assert.False(Enum.IsDefined(undefined));
		Assert.True(failsClosed(undefined),
			$"The undefined {typeof(TEnum).FullName} value {undefined} does not fail closed.");
	}

	/// <summary>Returns the value one past the largest value that <typeparamref name="TEnum" /> defines.</summary>
	/// <typeparam name="TEnum">The enum type.</typeparam>
	/// <returns>A value that the consumed package does not define.</returns>
	internal static TEnum Undefined<TEnum>()
		where TEnum : struct, Enum
	{
		long largest = Enum.GetValues<TEnum>()
			.Select(static value => Convert.ToInt64(value, CultureInfo.InvariantCulture))
			.Max();
		return (TEnum) Enum.ToObject(typeof(TEnum), largest + 1);
	}
}
