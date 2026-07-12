using System.Collections.Generic;
using SqliteHost.Tests.Adapter;
using SqliteHost.Tests.TestSupport;
using Xunit;

namespace SqliteHost.Tests
{
    public class FloatMappingTests
    {
        private const string InsertFloatType =
            "INSERT INTO call_float_type (call_id, input_f32, input_f64, input_opt_f32, input_opt_f64)"
            + " VALUES (:callId, :f32, :f64, :optF32, :optF64)";

        private static SqliteHostRuntime<IFloatTypeHandlers> CreateRuntime(
            EchoFloatTypeHandlers handlers,
            TestWorkspaceFactory factory)
        {
            // Every test here runs the workspace on the real engine, so all
            // would fail via the version gate below the (default) floor the
            // FloatTypeHost definition carries (see FloorGateTests).
            SampleHostFloor.SkipBelowFloor();
            return new SqliteHostRuntime<IFloatTypeHandlers>(
                connectionFactory: factory,
                hostDefinition: FloatTypeHost.Build(),
                handlers: handlers,
                options: null);
        }

        private static SqliteHostStatement FloatTypeStatement(bool withOptionals)
        {
            return Scripts.Statement(
                InsertFloatType,
                ("callId", SqliteHostBindingValue.Text("c-1")),
                ("f32", SqliteHostBindingValue.Float32(0.75f)),
                ("f64", SqliteHostBindingValue.Float64(98.5)),
                ("optF32", withOptionals ? SqliteHostBindingValue.Float32(-12.25f) : SqliteHostBindingValue.Null()),
                ("optF64", withOptionals ? SqliteHostBindingValue.Float64(0.0) : SqliteHostBindingValue.Null()));
        }

        [SkippableFact]
        public void FloatBindings_RoundTripIntoTheInputDto()
        {
            var handlers = new EchoFloatTypeHandlers();
            using var factory = new TestWorkspaceFactory(retainWorkspace: true);
            var runtime = CreateRuntime(handlers, factory);
            var script = Scripts.New(Scripts.Step("only", FloatTypeStatement(withOptionals: true)));

            SqliteHostRunResult result = runtime.Run(script);

            Assert.Equal(SqliteHostRunStatus.Completed, result.Status);
            FloatTypeInput input = handlers.LastInput;
            Assert.Equal(0.75f, input.F32);           // dyadic-exact
            Assert.Equal(98.5, input.F64);
            Assert.Equal(-12.25f, input.OptF32);      // negative
            Assert.Equal(0.0, input.OptF64);          // zero
            Assert.Empty(input.Pairs);
        }

        [SkippableFact]
        public void OptionalFloatNullInputs_MapToNullDtoValues()
        {
            var handlers = new EchoFloatTypeHandlers();
            using var factory = new TestWorkspaceFactory(retainWorkspace: true);
            var runtime = CreateRuntime(handlers, factory);
            var script = Scripts.New(Scripts.Step("only", FloatTypeStatement(withOptionals: false)));

            SqliteHostRunResult result = runtime.Run(script);

            Assert.Equal(SqliteHostRunStatus.Completed, result.Status);
            Assert.Null(handlers.LastInput.OptF32);
            Assert.Null(handlers.LastInput.OptF64);
        }

        [SkippableFact]
        public void ResultFloats_AreStoredAsRealValues()
        {
            var handlers = new EchoFloatTypeHandlers();
            using var factory = new TestWorkspaceFactory(retainWorkspace: true);
            var runtime = CreateRuntime(handlers, factory);
            var script = Scripts.New(Scripts.Step("only", FloatTypeStatement(withOptionals: true)));

            Assert.Equal(SqliteHostRunStatus.Completed, runtime.Run(script).Status);

            var rows = factory.LastWorkspace.Query(
                "SELECT status, result_f32, result_f64, result_opt_f32, result_opt_f64,"
                + " typeof(result_f32), typeof(result_f64) FROM result_float_type WHERE call_id = 'c-1'",
                null,
                row => new
                {
                    Status = row.GetText(0),
                    F32 = row.GetFloat32(1),
                    F64 = row.GetFloat64(2),
                    OptF32 = row.IsNull(3) ? (float?)null : row.GetFloat32(3),
                    OptF64 = row.IsNull(4) ? (double?)null : row.GetFloat64(4),
                    F32Type = row.GetText(5),
                    F64Type = row.GetText(6)
                });

            var row0 = Assert.Single(rows);
            Assert.Equal("done", row0.Status);
            Assert.Equal(0.75f, row0.F32);
            Assert.Equal(98.5, row0.F64);
            Assert.Equal(-12.25f, row0.OptF32);
            Assert.Equal(0.0, row0.OptF64);
            Assert.Equal("real", row0.F32Type);       // floats stored as REAL
            Assert.Equal("real", row0.F64Type);
        }

        [SkippableFact]
        public void ResultOptionalFloatNulls_AreWrittenAsNullColumns()
        {
            var handlers = new EchoFloatTypeHandlers();
            using var factory = new TestWorkspaceFactory(retainWorkspace: true);
            var runtime = CreateRuntime(handlers, factory);
            var script = Scripts.New(Scripts.Step("only", FloatTypeStatement(withOptionals: false)));

            Assert.Equal(SqliteHostRunStatus.Completed, runtime.Run(script).Status);

            var nullFlags = factory.LastWorkspace.Query(
                "SELECT result_opt_f32, result_opt_f64 FROM result_float_type WHERE call_id = 'c-1'",
                null,
                row => new[] { row.IsNull(0), row.IsNull(1) });

            Assert.All(Assert.Single(nullFlags), Assert.True);
        }

        [SkippableFact]
        public void Float32RoundTrip_PreservesExactDyadicValue()
        {
            // 0.75 is exactly representable as an IEEE-754 single, so it must
            // survive dto -> REAL -> dto without any drift.
            var handlers = new EchoFloatTypeHandlers();
            using var factory = new TestWorkspaceFactory(retainWorkspace: true);
            var runtime = CreateRuntime(handlers, factory);
            var script = Scripts.New(Scripts.Step("only", FloatTypeStatement(withOptionals: true)));

            Assert.Equal(SqliteHostRunStatus.Completed, runtime.Run(script).Status);

            Assert.Equal(0.75f, handlers.LastInput.F32);
            var stored = factory.LastWorkspace.Query(
                "SELECT result_f32 FROM result_float_type WHERE call_id = 'c-1'",
                null,
                row => row.GetFloat64(0));
            Assert.Equal(0.75, Assert.Single(stored));   // exact as double too
        }

        [SkippableFact]
        public void ListItemFloats_RoundTripThroughChildTables()
        {
            var handlers = new EchoFloatTypeHandlers();
            using var factory = new TestWorkspaceFactory(retainWorkspace: true);
            var runtime = CreateRuntime(handlers, factory);
            var script = Scripts.New(
                Scripts.Step("only",
                    FloatTypeStatement(withOptionals: true),
                    Scripts.Statement(
                        "INSERT INTO call_float_type__input_pairs"
                        + " (call_id, item_index, input_f32, input_f64, input_opt_f32, input_opt_f64)"
                        + " VALUES (:callId, 0, 0.75, 98.5, -12.25, 42.5), (:callId, 1, 42.5, 0.0, NULL, NULL)",
                        ("callId", SqliteHostBindingValue.Text("c-1")))));

            Assert.Equal(SqliteHostRunStatus.Completed, runtime.Run(script).Status);

            List<FloatPairItem> pairs = handlers.LastInput.Pairs;
            Assert.Equal(2, pairs.Count);
            Assert.Equal(0.75f, pairs[0].F32);
            Assert.Equal(98.5, pairs[0].F64);
            Assert.Equal(-12.25f, pairs[0].OptF32);
            Assert.Equal(42.5, pairs[0].OptF64);
            Assert.Equal(42.5f, pairs[1].F32);
            Assert.Equal(0.0, pairs[1].F64);
            Assert.Null(pairs[1].OptF32);
            Assert.Null(pairs[1].OptF64);

            var childRows = factory.LastWorkspace.Query(
                "SELECT item_index, result_f32, result_f64, result_opt_f32 FROM result_float_type__result_echo_pairs"
                + " WHERE call_id = 'c-1' ORDER BY item_index",
                null,
                row => new
                {
                    Index = row.GetInt64(0),
                    F32 = row.GetFloat32(1),
                    F64 = row.GetFloat64(2),
                    OptF32 = row.IsNull(3) ? (float?)null : row.GetFloat32(3)
                });

            Assert.Equal(2, childRows.Count);
            Assert.Equal(0.75f, childRows[0].F32);
            Assert.Equal(98.5, childRows[0].F64);
            Assert.Equal(-12.25f, childRows[0].OptF32);
            Assert.Equal(42.5f, childRows[1].F32);
            Assert.Equal(0.0, childRows[1].F64);
            Assert.Null(childRows[1].OptF32);
        }

        [SkippableFact]
        public void RuntimeFloatInputs_StoreIntoRealValue_NullStoresAllValueColumnsNull()
        {
            var handlers = new EchoFloatTypeHandlers();
            using var factory = new TestWorkspaceFactory(retainWorkspace: true);
            var runtime = CreateRuntime(handlers, factory);
            var script = Scripts.New(Scripts.Step("only", FloatTypeStatement(withOptionals: true)));
            script.Inputs = new List<SqliteHostRuntimeInput>
            {
                new SqliteHostRuntimeInput { Name = "ratio", Value = SqliteHostBindingValue.Float64(98.5) },
                new SqliteHostRuntimeInput { Name = "weight", Value = SqliteHostBindingValue.Float32(0.75f) },
                new SqliteHostRuntimeInput { Name = "empty", Value = SqliteHostBindingValue.Null() }
            };

            Assert.Equal(SqliteHostRunStatus.Completed, runtime.Run(script).Status);

            var rows = factory.LastWorkspace.Query(
                "SELECT name, value_type, int_value IS NULL, real_value, text_value IS NULL, blob_value IS NULL"
                + " FROM script_inputs ORDER BY name",
                null,
                row => new
                {
                    Name = row.GetText(0),
                    ValueType = row.GetText(1),
                    IntIsNull = row.GetBool(2),
                    RealValue = row.IsNull(3) ? (double?)null : row.GetFloat64(3),
                    TextIsNull = row.GetBool(4),
                    BlobIsNull = row.GetBool(5)
                });

            Assert.Equal(3, rows.Count);
            Assert.Equal("empty", rows[0].Name);
            Assert.Equal("null", rows[0].ValueType);
            Assert.True(rows[0].IntIsNull);
            Assert.Null(rows[0].RealValue);
            Assert.True(rows[0].TextIsNull);
            Assert.True(rows[0].BlobIsNull);
            Assert.Equal("ratio", rows[1].Name);
            Assert.Equal("float64", rows[1].ValueType);
            Assert.True(rows[1].IntIsNull);
            Assert.Equal(98.5, rows[1].RealValue);
            Assert.Equal("weight", rows[2].Name);
            Assert.Equal("float32", rows[2].ValueType);
            Assert.True(rows[2].IntIsNull);
            Assert.Equal(0.75, rows[2].RealValue);
        }
    }
}
