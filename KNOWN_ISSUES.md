# Known issues

These are bugs we've confirmed in this fork but have chosen not to fix yet. For each one, this file records how to reproduce it, who it affects today, how to fix it, and what should make us look at it again. Fixes that have already shipped are listed in [FORK_CHANGES.md](FORK_CHANGES.md).

Last reviewed: 2026-11-10.

## 1. `TrinoBigDecimal.Equals` modifies the value it is called on

**Where:** `trino-csharp/Trino.Client/Types/BigDecimal.cs`, `Equals(object)`. The bug comes from upstream, not from this fork.

`Equals` calls `AlignScales(ref this, ref other)` to bring both values to the same scale before comparing them. Inside a struct method, `this` refers to the caller's own copy of the value, so when the other value has more decimal places, the caller's value is padded to match:

```csharp
var a = new TrinoBigDecimal("1.5");
var b = new TrinoBigDecimal("1.50");
a.Equals(b);   // true
a.ToString();  // "1.50" (was "1.5")
```

Comparing two values should never change either of them. Here, calling `Equals` changes what `a` prints, and therefore what `ToDouble()` parses. The numeric value is the same, but the formatting is not.

**Fix:** copy `this` into a local variable and align the scales on the copy:

```csharp
var self = this;
AlignScales(ref self, ref other);
// then compare `self` with `other`
```

## 2. `TrinoBigDecimal.GetHashCode` disagrees with `Equals`

**Where:** the same file, `GetHashCode()`. The bug comes from upstream.

`Equals` treats `1.5` and `1.50` as equal because it aligns their scales first. `GetHashCode` instead hashes the raw `fractionalPart` (5 vs 50) and the `scale` (1 vs 2), so equal values get different hash codes. That breaks the rule .NET hash-based collections depend on:

```csharp
var set = new HashSet<TrinoBigDecimal> { new TrinoBigDecimal("1.5") };
set.Contains(new TrinoBigDecimal("1.50"));  // false
```

The same thing happens with `Dictionary` keys and with LINQ's `Distinct`, `GroupBy` and `ToLookup`.

**Fix:** compute the hash from a normalized form. Strip trailing zeros from the fractional part, lower the scale to match, and leave `scale` itself out of the hash. Equal values then always hash the same.

## Why these aren't fixed yet

Both bugs only show up if someone keeps the raw `TrinoBigDecimal` and compares it, or uses it as a collection key. On 2026-11-10 we checked every consumer we know of, and none does:

| Consumer | How it handles `TrinoBigDecimal` |
| --- | --- |
| `sales-and-operations-planning-api` | Every DECIMAL is read through `MartSql.ReadDecimal`, which converts it to `decimal` via `ToString()` straight away. The MartExplorer demo converts it to a string. |
| `platform-snowlake-client-dotnet` library (`src/`) | Never refers to the type. |
| `platform-snowlake-client-dotnet` QueryUI app and samples | QueryUI shows each value with `ToString()`; the samples call `ToDouble()`. |

Every consumer converts the value to `decimal`, `double` or `string` as soon as it's read. They do this because `TrinoBigDecimal` doesn't implement `IConvertible`, and it also keeps them clear of both bugs.

## When to look at these again

- A consumer starts keeping `TrinoBigDecimal` values rather than converting them right away. For example, it uses them as dictionary keys, groups by them, compares them, or caches raw `GetValue()` results.
- We add `IConvertible`, conversion operators or comparison operators to `TrinoBigDecimal`. That makes the raw type easier to keep around, so both bugs become more likely to bite.
- An upstream sync touches `BigDecimal.cs`.
