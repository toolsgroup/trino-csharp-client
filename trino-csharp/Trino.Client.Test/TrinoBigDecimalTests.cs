using System.Globalization;
using System.Numerics;
using Trino.Client.Types;

namespace Trino.Client.Test
{
    [TestClass]
    public class TrinoBigDecimalTests
    {
        // AISOP-455 / trinodb/trino-csharp-client#29: values strictly between -1 and 0 used to lose
        // their sign at construction ("-0" parses to BigInteger 0).
        [TestMethod]
        [DataRow("-0.5")]
        [DataRow("-0.0001")]
        [DataRow("-0.1032")]
        [DataRow("0")]
        [DataRow("0.5")]
        [DataRow("-1.5")]
        [DataRow("1.5")]
        [DataRow("-82.8506")]
        [DataRow("-123.456")]
        [DataRow("123.00456")]
        [DataRow("-42")]
        public void StringValue_RoundTripsWithCorrectSign(string input)
        {
            var value = new TrinoBigDecimal(input);
            var expected = decimal.Parse(input, CultureInfo.InvariantCulture);

            Assert.AreEqual(input, value.ToString());
            Assert.AreEqual(expected, value.ToDecimal());
            Assert.AreEqual(double.Parse(input, CultureInfo.InvariantCulture), value.ToDouble());
            Assert.AreEqual(Math.Sign(expected), value.GetSign());
        }

        [TestMethod]
        public void NegativeFraction_IsNotEqualToPositive()
        {
            Assert.AreNotEqual(new TrinoBigDecimal("0.5"), new TrinoBigDecimal("-0.5"));
            Assert.AreEqual(new TrinoBigDecimal("-0.5"), new TrinoBigDecimal("-0.5"));
        }

        [TestMethod]
        public void NegativeZero_IsNormalizedToZero()
        {
            var negativeZero = new TrinoBigDecimal("-0");
            Assert.AreEqual(new TrinoBigDecimal("0"), negativeZero);
            Assert.AreEqual(default(TrinoBigDecimal), negativeZero);
            Assert.AreEqual(new TrinoBigDecimal("0").GetHashCode(), negativeZero.GetHashCode());
            Assert.AreEqual("0", negativeZero.ToString());
            Assert.AreEqual(0, negativeZero.GetSign());

            var negativeZeroWithScale = new TrinoBigDecimal("-0.00");
            Assert.AreEqual("0.00", negativeZeroWithScale.ToString());
            Assert.AreEqual(0, negativeZeroWithScale.GetSign());
            Assert.AreEqual(0m, negativeZeroWithScale.ToDecimal());
        }

        [TestMethod]
        public void Default_IsPositiveZero()
        {
            var value = default(TrinoBigDecimal);
            Assert.AreEqual("0", value.ToString());
            Assert.AreEqual(0, value.GetSign());
            Assert.AreEqual(0m, value.ToDecimal());
            Assert.AreEqual(new TrinoBigDecimal("0"), value);
        }

        [TestMethod]
        public void ExplicitPlusSign_IsAccepted()
        {
            Assert.AreEqual(new TrinoBigDecimal("1.5"), new TrinoBigDecimal("+1.5"));
            Assert.AreEqual("1.5", new TrinoBigDecimal("+1.5").ToString());
        }

        [TestMethod]
        public void MissingIntegerPart_IsZero()
        {
            var value = new TrinoBigDecimal("-.5");
            Assert.AreEqual("-0.5", value.ToString());
            Assert.AreEqual(-0.5m, value.ToDecimal());
        }

        [TestMethod]
        [DataRow("--1")]
        [DataRow("+-1")]
        [DataRow("1-")]
        [DataRow("1.-2")]
        [DataRow("1.2.3")]
        [DataRow("-")]
        [DataRow(".")]
        [DataRow("1.")]
        [DataRow("1 2")]
        [DataRow("abc")]
        public void MalformedString_Throws(string input)
        {
            Assert.ThrowsException<FormatException>(() => new TrinoBigDecimal(input));
        }

        [TestMethod]
        [DataRow(null)]
        [DataRow("")]
        [DataRow("   ")]
        public void EmptyString_Throws(string? input)
        {
            Assert.ThrowsException<ArgumentException>(() => new TrinoBigDecimal(input!));
        }

        [TestMethod]
        public void SurroundingWhitespace_IsTrimmed()
        {
            Assert.AreEqual("-0.25", new TrinoBigDecimal("  -0.25 ").ToString());
        }

        [TestMethod]
        public void BigIntegerConstructor_TakesSignFromIntegerPart()
        {
            var value = new TrinoBigDecimal(new BigInteger(-123), new BigInteger(456), 3);
            Assert.AreEqual("-123.456", value.ToString());
            Assert.AreEqual(-123.456m, value.ToDecimal());
            Assert.AreEqual(new BigInteger(-123), value.GetIntegerPart());
            Assert.AreEqual(new BigInteger(456), value.GetFractionalPart());
            Assert.AreEqual(-1, value.GetSign());
        }

        [TestMethod]
        public void BigIntegerConstructor_RejectsNegativeFractionalPart()
        {
            Assert.ThrowsException<ArgumentException>(() => new TrinoBigDecimal(BigInteger.One, BigInteger.MinusOne, 1));
        }

        [TestMethod]
        public void GetIntegerPart_IsSigned_AndZeroForFractionsBetweenMinusOneAndZero()
        {
            Assert.AreEqual(new BigInteger(-82), new TrinoBigDecimal("-82.8506").GetIntegerPart());
            Assert.AreEqual(BigInteger.Zero, new TrinoBigDecimal("-0.6").GetIntegerPart());
            Assert.AreEqual(-1, new TrinoBigDecimal("-0.6").GetSign());
        }

        [TestMethod]
        public void GetPrecision_IsCorrectForNegativeValues()
        {
            Assert.AreEqual(6, new TrinoBigDecimal("-123.456").GetPrecision());
            Assert.AreEqual(new TrinoBigDecimal("123.456").GetPrecision(), new TrinoBigDecimal("-123.456").GetPrecision());
        }

        [TestMethod]
        [DataRow("123456789000000000.123400500099999999")]
        [DataRow("-123456789000000000.123400500099999999")]
        [DataRow("-0.00000000000000000000000000001")]
        public void ToDecimal_TooManyDigits_ThrowsOverflow(string input)
        {
            Assert.ThrowsException<OverflowException>(() => new TrinoBigDecimal(input).ToDecimal());
        }

        [TestMethod]
        public void ToDouble_HandlesValuesBeyondDecimalPrecision()
        {
            var value = new TrinoBigDecimal("-123456789000000000.123400500099999999");
            Assert.AreEqual(-123456789000000000.123400500099999999, value.ToDouble());
        }
    }
}
