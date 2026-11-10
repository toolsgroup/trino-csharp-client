using System;
using System.Numerics;
using System.Globalization;

namespace Trino.Client.Types
{
    public struct TrinoBigDecimal
    {
        /// <summary>
        /// Magnitude of the integer part; always non-negative. The sign lives in <see cref="isNegative"/>,
        /// because BigInteger has no negative zero and a value like "-0.6" would otherwise lose its sign.
        /// </summary>
        private BigInteger integerPart;
        private BigInteger fractionalPart;

        /// <summary>
        /// The scale represents the number of digits to the right of the decimal point.
        /// It is used to determine the precision of the fractional part of the BigDecimal.
        /// </summary>
        /// <example>
        /// For example, if the BigDecimal is "123.00456", the scale is 5 because there are five digits to the right of the decimal point, including the leading zeros.
        /// </example>
        private int scale;

        /// <summary>
        /// Explicit sign, so "-0.xxx" values keep it. A bool rather than an int so that
        /// default(TrinoBigDecimal) is a well-formed positive zero. Always false for a zero value.
        /// </summary>
        private bool isNegative;

        public TrinoBigDecimal(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
                throw new ArgumentException("Invalid decimal string.");

            value = value.Trim();

            // Strip exactly one leading sign; any further sign character is rejected by the
            // digits-only parse below.
            isNegative = value.StartsWith("-", StringComparison.Ordinal);
            if (value.StartsWith("+", StringComparison.Ordinal) || value.StartsWith("-", StringComparison.Ordinal))
                value = value.Substring(1);

            var parts = value.Split('.');
            if (parts.Length > 2)
                throw new FormatException($"Invalid decimal string: '{value}'.");

            // An empty integer part (e.g. "-.5") means zero, but there must be digits somewhere,
            // and a '.' must be followed by digits.
            string integerDigits = parts[0];
            string fractionalDigits = parts.Length > 1 ? parts[1] : null;
            if ((integerDigits.Length == 0 && string.IsNullOrEmpty(fractionalDigits)) || fractionalDigits?.Length == 0)
                throw new FormatException($"Invalid decimal string: '{value}'.");

            integerPart = integerDigits.Length == 0 ? BigInteger.Zero : ParseDigits(integerDigits);
            fractionalPart = fractionalDigits != null ? ParseDigits(fractionalDigits) : BigInteger.Zero;
            scale = fractionalDigits?.Length ?? 0;

            Validate();
            NormalizeZeroSign();
        }

        public TrinoBigDecimal(BigInteger integerPart, BigInteger fractionalPart, int scale)
        {
            this.isNegative = integerPart.Sign < 0;
            this.integerPart = BigInteger.Abs(integerPart);
            this.fractionalPart = fractionalPart;
            this.scale = scale;
            Validate();
            NormalizeZeroSign();
        }

        /// <summary>
        /// Parses an unsigned run of ASCII digits, independent of the current culture.
        /// </summary>
        private static BigInteger ParseDigits(string digits)
        {
            return BigInteger.Parse(digits, NumberStyles.None, CultureInfo.InvariantCulture);
        }

        private void Validate()
        {
            if (fractionalPart < 0)
                throw new ArgumentException("fractionalPart cannot be negative.");
        }

        private bool IsZero => integerPart.IsZero && fractionalPart.IsZero;

        /// <summary>
        /// "-0" and "-0.00" are the same value as "0" and "0.00": drop the sign so they compare,
        /// hash and print identically.
        /// </summary>
        private void NormalizeZeroSign()
        {
            if (IsZero)
                isNegative = false;
        }

        public override string ToString()
        {
            string core = scale > 0
                ? $"{integerPart}.{fractionalPart.ToString().PadLeft(scale, '0')}"
                : integerPart.ToString();
            return isNegative ? "-" + core : core;
        }

        /// <summary>
        /// Converts to a decimal, throws overflow exception.
        /// </summary>
        public decimal ToDecimal()
        {
            const int maxPrecisionForDecimal = 28; // 29 is limit in some circumstances
            if (GetPrecision() > maxPrecisionForDecimal)
            {
                throw new OverflowException("The precision exceeds the allowable limit for a decimal.");
            }

            // Both parts are non-negative magnitudes and together fit in 28 digits, so this is exact;
            // the sign is applied once, at the end.
            var integerDecimal = (decimal)integerPart;
            var fractionalDecimal = (decimal)fractionalPart / (decimal)BigInteger.Pow(10, scale);
            var magnitude = integerDecimal + fractionalDecimal;
            return isNegative ? -magnitude : magnitude;
        }

        /// <summary>
        /// Converts to a double. TrinoBigDecimal implements neither IConvertible nor any
        /// conversion operator, so Convert.ToDouble()/(double) casts fail; this covers the
        /// common case of using a Trino DECIMAL/aggregation result (e.g. SUM(qty * price)) as a
        /// plain floating-point number, at the cost of double's usual floating-point precision.
        /// Unlike ToDecimal(), this has no ~28-significant-digit ceiling, since it goes through
        /// ToString().
        /// </summary>
        public double ToDouble()
        {
            return double.Parse(ToString(), NumberStyles.Float, CultureInfo.InvariantCulture);
        }

        public int GetScale() => scale;

        public int GetPrecision()
        {
            var integerDigits = integerPart.IsZero ? 0 : (int)Math.Floor(BigInteger.Log10(integerPart)) + 1;
            return integerDigits + scale;
        }

        /// <summary>
        /// -1, 0 or 1, like <see cref="BigInteger.Sign"/>: 0 only when the whole value is zero.
        /// </summary>
        public int GetSign() => IsZero ? 0 : (isNegative ? -1 : 1);

        /// <summary>
        /// The signed integer part. For a value between -1 and 0 (e.g. "-0.6") this is 0 and
        /// carries no sign; use <see cref="GetSign"/> to get the sign of the whole value.
        /// </summary>
        public BigInteger GetIntegerPart() => isNegative ? -integerPart : integerPart;

        public BigInteger GetFractionalPart() => fractionalPart;

        public override bool Equals(object obj)
        {
            if (obj is TrinoBigDecimal other)
            {
                AlignScales(ref this, ref other);
                return isNegative == other.isNegative &&
                       integerPart == other.integerPart &&
                       fractionalPart == other.fractionalPart;
            }
            return false;
        }

        public override int GetHashCode()
        {
            unchecked
            {
                int hash = 17;
                hash = hash * 31 + integerPart.GetHashCode();
                hash = hash * 31 + fractionalPart.GetHashCode();
                hash = hash * 31 + scale.GetHashCode();
                hash = hash * 31 + isNegative.GetHashCode();
                return hash;
            }
        }

        /// <summary>
        /// The AlignScales method ensures that two BigDecimal instances have the same scale before performing arithmetic operations.
        /// a = 1.23 (scale = 2)
        /// b = 4.567 (scale = 3)
        /// a's fractional part is adjusted to 230 (by multiplying by 10) to match the scale of 3.
        /// </summary>
        private static void AlignScales(ref TrinoBigDecimal a, ref TrinoBigDecimal b)
        {
            if (a.scale > b.scale)
            {
                b.fractionalPart *= BigInteger.Pow(10, a.scale - b.scale);
                b.scale = a.scale;
            }
            else if (b.scale > a.scale)
            {
                a.fractionalPart *= BigInteger.Pow(10, b.scale - a.scale);
                a.scale = b.scale;
            }
        }
    }
}
