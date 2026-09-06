using System.Collections.Generic;
using Example.Game.Generated;
using SqliteHost.Tests.Adapter;
using SqliteHost.Tests.TestSupport;
using Xunit;

namespace SqliteHost.Tests
{
    /// <summary>
    /// Storage-class fidelity on the drain's read path. SQLite column
    /// affinity is a hint, not a constraint: a TEXT or REAL value survives
    /// in an INTEGER-affinity column whenever the conversion would lose
    /// information, and an unconditional typed getter then hands the
    /// handler a coerced value nothing reports (docs/adapter-contract.md,
    /// "Value fidelity"). The runtime asks for the column's storage class
    /// first and fails the call with input-type-mismatch instead.
    /// </summary>
    public class RuntimeStorageClassTests
    {
        private const string InsertEveryType =
            "INSERT INTO call_every_type"
            + " (call_id, input_i32, input_i64, input_flag, input_name, input_payload)"
            + " VALUES ('c-1', {I32}, {I64}, {FLAG}, {NAME}, {PAYLOAD})";

        private static string EveryTypeSql(
            string i32 = "1", string i64 = "2", string flag = "1",
            string name = "'n'", string payload = "x'00'")
        {
            return InsertEveryType
                .Replace("{I32}", i32).Replace("{I64}", i64).Replace("{FLAG}", flag)
                .Replace("{NAME}", name).Replace("{PAYLOAD}", payload);
        }

        private static SqliteHostRunResult RunEveryType(string sql, EchoEveryTypeHandlers handlers)
        {
            SampleHostFloor.SkipBelowFloor();
            var runtime = new SqliteHostRuntime<IEveryTypeHandlers>(
                connectionFactory: new TestWorkspaceFactory(),
                hostDefinition: EveryTypeHost.Build(),
                handlers: handlers,
                options: null);
            return runtime.Run(Scripts.New(Scripts.Step("only", Scripts.Statement(sql))));
        }

        /// <summary>
        /// The realistic shape, and the one no validator can see: the CALLER
        /// supplies a runtime input and the script routes it out of
        /// script_inputs into a typed call column with INSERT..SELECT. There
        /// is no parameter feeding the column, so binding-type-mismatch
        /// (docs/validation.md) has nothing to compare, and prepare-only
        /// validation compiles it clean. "1,000" is not losslessly
        /// convertible, so INTEGER affinity leaves it TEXT and the handler
        /// used to receive 1.
        /// </summary>
        [SkippableFact]
        public void CallerTextInput_ThatIsNotANumber_FailsInputTypeMismatch()
        {
            SampleHostFloor.SkipBelowFloor();
            var handlers = new FakeGameHandlers();
            var runtime = new SqliteHostRuntime<IGeneratedHostHandlers>(
                connectionFactory: new TestWorkspaceFactory(),
                hostDefinition: GeneratedHostDefinition.Build(),
                handlers: handlers,
                options: null);
            var script = Scripts.New(
                Scripts.Step("only",
                    Scripts.Statement(
                        "INSERT INTO call_set_value (call_id, input_key, input_value)"
                        + " SELECT 's-1', 'k', text_value FROM script_inputs WHERE name = 'amount'")));
            script.Inputs = new List<SqliteHostRuntimeInput>
            {
                new SqliteHostRuntimeInput
                {
                    Name = "amount",
                    Value = SqliteHostBindingValue.Text("1,000")
                }
            };

            SqliteHostRunResult result = runtime.Run(script);

            Assert.Equal(SqliteHostRunStatus.FailedSql, result.Status);
            Assert.Equal("input-type-mismatch", result.ErrorCode);
            Assert.Equal("setValue", result.Method);
            Assert.Contains("value", result.ErrorMessage);
            Assert.Contains("int64", result.ErrorMessage);
            Assert.Contains("text", result.ErrorMessage);
            Assert.Empty(handlers.Log);
        }

        /// <summary>
        /// The benign control that makes the bug easy to miss: "1000" IS
        /// losslessly convertible, so INTEGER affinity stores an integer and
        /// the call is a normal one. The guard must not reject it.
        /// </summary>
        [SkippableFact]
        public void CallerTextInput_ThatIsANumber_StillCompletes()
        {
            SampleHostFloor.SkipBelowFloor();
            var handlers = new FakeGameHandlers();
            var runtime = new SqliteHostRuntime<IGeneratedHostHandlers>(
                connectionFactory: new TestWorkspaceFactory(),
                hostDefinition: GeneratedHostDefinition.Build(),
                handlers: handlers,
                options: null);
            var script = Scripts.New(
                Scripts.Step("only",
                    Scripts.Statement(
                        "INSERT INTO call_set_value (call_id, input_key, input_value)"
                        + " SELECT 's-1', 'k', text_value FROM script_inputs WHERE name = 'amount'")));
            script.Inputs = new List<SqliteHostRuntimeInput>
            {
                new SqliteHostRuntimeInput
                {
                    Name = "amount",
                    Value = SqliteHostBindingValue.Text("1000")
                }
            };

            SqliteHostRunResult result = runtime.Run(script);

            Assert.Equal(SqliteHostRunStatus.Completed, result.Status);
            Assert.Equal(new[] { "setValue:k:1000" }, handlers.Log);
        }

        [SkippableTheory]
        // Stored literal, declared field, and the storage class the value
        // actually keeps. An INTEGER literal in a TEXT column is absent on
        // purpose: TEXT affinity converts a number to text losslessly at
        // insert, so what is stored is already text and there is nothing to
        // coerce on the way out. A BLOB is the one class TEXT affinity
        // leaves alone, which is why the blob row below is the real case.
        [InlineData("i32", "'abc'", "int32", "text")]
        [InlineData("i64", "'abc'", "int64", "text")]
        [InlineData("i64", "1.5", "int64", "real")]
        [InlineData("flag", "'true'", "bool", "text")]
        [InlineData("name", "x'414243'", "text", "blob")]
        [InlineData("payload", "'hi'", "blob", "text")]
        public void WrongStorageClassInACallColumn_FailsInputTypeMismatch(
            string field, string literal, string declared, string actual)
        {
            var handlers = new EchoEveryTypeHandlers();
            string sql = field == "i32" ? EveryTypeSql(i32: literal)
                : field == "i64" ? EveryTypeSql(i64: literal)
                : field == "flag" ? EveryTypeSql(flag: literal)
                : field == "name" ? EveryTypeSql(name: literal)
                : EveryTypeSql(payload: literal);

            SqliteHostRunResult result = RunEveryType(sql, handlers);

            Assert.Equal(SqliteHostRunStatus.FailedSql, result.Status);
            Assert.Equal("input-type-mismatch", result.ErrorCode);
            Assert.Equal("everyType", result.Method);
            Assert.Contains(field, result.ErrorMessage);
            Assert.Contains(declared, result.ErrorMessage);
            Assert.Contains(actual, result.ErrorMessage);
            Assert.Null(handlers.LastInput);
        }

        [SkippableFact]
        public void MatchingStorageClasses_StillReachTheHandler()
        {
            var handlers = new EchoEveryTypeHandlers();

            SqliteHostRunResult result = RunEveryType(EveryTypeSql(), handlers);

            Assert.Equal(SqliteHostRunStatus.Completed, result.Status);
            Assert.NotNull(handlers.LastInput);
            Assert.Equal(1, handlers.LastInput.I32);
            Assert.Equal(2L, handlers.LastInput.I64);
        }

        /// <summary>
        /// The storage class is right and the value is still wrong: an
        /// INTEGER column holds any int64, so an int32 field can be handed
        /// one that does not fit. sqlite3_column_int returns the low 32
        /// bits, so 2^32+7 arrived as 7 — a substituted value nothing can
        /// tell apart from a stored 7, which is exactly what the NULL rule
        /// in docs/adapter-contract.md forbids for the same reason. The
        /// runtime reads int64 and range-checks instead.
        /// </summary>
        [SkippableTheory]
        [InlineData("4294967303")]
        [InlineData("2147483648")]
        [InlineData("-2147483649")]
        public void Int64OutsideInt32Range_InAnInt32Field_FailsInputTypeMismatch(string literal)
        {
            var handlers = new EchoEveryTypeHandlers();

            SqliteHostRunResult result = RunEveryType(EveryTypeSql(i32: literal), handlers);

            Assert.Equal(SqliteHostRunStatus.FailedSql, result.Status);
            Assert.Equal("input-type-mismatch", result.ErrorCode);
            Assert.Equal("everyType", result.Method);
            Assert.Contains("i32", result.ErrorMessage);
            Assert.Contains(literal, result.ErrorMessage);
            Assert.Null(handlers.LastInput);
        }

        [SkippableTheory]
        [InlineData("2147483647")]
        [InlineData("-2147483648")]
        public void Int32BoundaryValues_StillReachTheHandler(string literal)
        {
            var handlers = new EchoEveryTypeHandlers();

            SqliteHostRunResult result = RunEveryType(EveryTypeSql(i32: literal), handlers);

            Assert.Equal(SqliteHostRunStatus.Completed, result.Status);
            Assert.Equal(int.Parse(literal), handlers.LastInput.I32);
        }
    }
}
