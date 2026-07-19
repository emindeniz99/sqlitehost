using System;
using Xunit;

namespace SqliteHost.Tests
{
    /// <summary>
    /// The text/blob binding factories must reject a null payload. The
    /// discriminator must never lie about the payload: a value tagged
    /// text/blob but carrying null would silently degrade to SQL NULL
    /// downstream (native BindValue emits sqlite3_bind_null and scalar-fn
    /// returns emit sqlite3_result_null — NativeSqliteHostConnection.cs:567
    /// and :313), and the runtime input serializer would persist
    /// valueType="text"/"blob" over a NULL payload column, corrupting the
    /// round-trip. Null() is the single, explicit way to express SQL NULL.
    /// This mirrors the Java reference, which already guards with
    /// Objects.requireNonNull(value, "value") (BindingValue.java:91, :97),
    /// and the wire contract where text/blob are real payloads and an
    /// absent value is the reserved NULL path (docs/script-envelope.md:54,
    /// 58, 59).
    /// </summary>
    public class SqliteHostBindingValueTests
    {
        [Fact]
        public void Text_Null_Throws_SoTheDiscriminatorNeverLiesAboutThePayload()
        {
            Assert.Throws<ArgumentNullException>(() => SqliteHostBindingValue.Text(null));
        }

        [Fact]
        public void Blob_Null_Throws_SoTheDiscriminatorNeverLiesAboutThePayload()
        {
            Assert.Throws<ArgumentNullException>(() => SqliteHostBindingValue.Blob(null));
        }

        [Fact]
        public void Null_IsTheSingleRepresentationOfSqlNull()
        {
            Assert.Equal(SqliteHostBindingType.Null, SqliteHostBindingValue.Null().Type);
        }

        [Fact]
        public void EmptyPayloadIsNotNull_AndStaysAValidTextOrBlobBinding()
        {
            Assert.Equal(SqliteHostBindingType.Text, SqliteHostBindingValue.Text("").Type);
            Assert.Equal(SqliteHostBindingType.Blob, SqliteHostBindingValue.Blob(new byte[0]).Type);
        }

        // Non-finite floats have no JSON representation and no portable
        // SQLite REAL; the Java factory already rejects them
        // (BindingValue.java float32/float64: "must be finite"), so the C#
        // factory must too — otherwise a NaN/Infinity binding serializes to
        // a non-contract payload or binds as a silently-degraded REAL.
        [Theory]
        [InlineData(float.NaN)]
        [InlineData(float.PositiveInfinity)]
        [InlineData(float.NegativeInfinity)]
        public void Float32_NonFinite_Throws(float value)
        {
            Assert.Throws<ArgumentException>(() => SqliteHostBindingValue.Float32(value));
        }

        [Theory]
        [InlineData(double.NaN)]
        [InlineData(double.PositiveInfinity)]
        [InlineData(double.NegativeInfinity)]
        public void Float64_NonFinite_Throws(double value)
        {
            Assert.Throws<ArgumentException>(() => SqliteHostBindingValue.Float64(value));
        }

        [Fact]
        public void FiniteFloatsStayValid()
        {
            Assert.Equal(SqliteHostBindingType.Float32, SqliteHostBindingValue.Float32(1.5f).Type);
            Assert.Equal(SqliteHostBindingType.Float64, SqliteHostBindingValue.Float64(-2.5d).Type);
        }
    }
}
