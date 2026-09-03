using System;
using System.Globalization;
using System.Numerics;

namespace Trino.Client.Types
{
    public struct TrinoBigDecimal
    {
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

        public TrinoBigDecimal(string value)
        {
            var parts = value.Split('.');
            integerPart = BigInteger.Parse(parts[0]);
            fractionalPart = parts.Length > 1 ? BigInteger.Parse(parts[1]) : BigInteger.Zero;
            scale = parts.Length > 1 ? parts[1].Length : 0;
            Validate();
        }

        public TrinoBigDecimal(BigInteger integerPart, BigInteger fractionalPart, int scale)
        {
            this.integerPart = integerPart;
            this.fractionalPart = fractionalPart;
            this.scale = scale;
            Validate();
        }

        private void Validate()
        {
            if (fractionalPart < 0)
            {
                throw new ArgumentException("fractionalPart cannot be negative.");
            }
        }

        public override string ToString()
        {
            return scale > 0 ? $"{integerPart}.{fractionalPart.ToString().PadLeft(scale, '0')}" : integerPart.ToString();
        }

        public override bool Equals(object obj)
        {
            if (obj is TrinoBigDecimal other)
            {
                AlignScales(ref this, ref other);
                return integerPart == other.integerPart && fractionalPart == other.fractionalPart;
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
                return hash;
            }
        }

        /// <summary>
        /// Converts to a decimal, throws overflow exception.
        /// </summary>
        public decimal ToDecimal()
        {
            const int maxPrecisionForDecimal = 28; // 29 is limit in some circumstances
            int digits = integerPart.IsZero ? 0 : (int)Math.Floor(BigInteger.Log10(integerPart)) + 1;
            if (scale + digits > maxPrecisionForDecimal)
            {
                throw new OverflowException("The precision exceeds the allowable limit for a decimal.");
            }

            // Check if integerPart is within the range of decimal
            if (integerPart < (BigInteger)decimal.MinValue || integerPart > (BigInteger)decimal.MaxValue)
            {
                throw new OverflowException("The integer part is out of range for a decimal.");
            }

            var integerDecimal = (decimal)integerPart;

            // Check if fractionalPart is within the range of decimal
            var fractionalDecimal = (decimal)fractionalPart / (decimal)BigInteger.Pow(10, scale);
            if (fractionalDecimal < decimal.MinValue || fractionalDecimal > decimal.MaxValue)
            {
                throw new OverflowException("The fractional part is out of range for a decimal.");
            }

            // fractionalDecimal is always >= 0 (fractionalPart itself can't be negative, per
            // Validate()), but integerDecimal carries the real sign -- for a negative value with a
            // nonzero integer part (e.g. "-123.456", integerPart=-123, fractionalPart=456), adding
            // them unconditionally silently produces the wrong number (-123 + 0.456 = -122.544
            // instead of -123.456). Subtract instead of add when the value is negative.
            // This will throw an overflow exception if the combined value is out of decimal's range.
            return integerPart.Sign < 0 ? integerDecimal - fractionalDecimal : integerDecimal + fractionalDecimal;
        }

        /// <summary>
        /// Converts to a double. TrinoBigDecimal implements neither IConvertible nor any
        /// conversion operator, so Convert.ToDouble()/(double) casts fail; this covers the
        /// common case of using a Trino DECIMAL/aggregation result (e.g. SUM(qty * price)) as a
        /// plain floating-point number, at the cost of double's usual floating-point precision.
        /// Unlike ToDecimal(), this has no ~28-significant-digit ceiling, since it goes through
        /// ToString() rather than combining the integer and fractional parts as separate decimal
        /// values (which mishandles the sign for a negative value with a nonzero integer part).
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
        public int GetSign() => integerPart.Sign;
        public BigInteger GetIntegerPart() => integerPart;
        public BigInteger GetFractionalPart() => fractionalPart;

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
