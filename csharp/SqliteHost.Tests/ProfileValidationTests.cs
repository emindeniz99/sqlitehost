using System;
using System.Collections.Generic;
using SqliteHost.Tests.Adapter;
using SqliteHost.Tests.TestSupport;
using Xunit;

namespace SqliteHost.Tests
{
    /// <summary>
    /// Fail-loud behaviors specific to the compact and ultra registration
    /// surfaces (plus the boxed-DTO guard on the classic surface): builder
    /// preconditions, inline shape-rule parity, and the ultra result-shape
    /// validation that replaces compile-time DTO typing.
    /// </summary>
    public class ProfileValidationTests
    {
        private interface ITestHandlers
        {
        }

        private sealed class TestHandlers : ITestHandlers
        {
        }

        // --- classic guard: boxed DTOs must be classes ---

        private struct StructInput
        {
        }

        private sealed class DummyResult
        {
            public bool Ok { get; set; }
        }

        [Fact]
        public void Classic_ValueTypeInputDto_IsRejectedAtRegistration()
        {
            var ex = Assert.Throws<ArgumentException>(
                () => HostMethod.For<ITestHandlers, StructInput, DummyResult>("m"));
            Assert.Contains("must be classes", ex.Message);
        }

        [Fact]
        public void Classic_ValueTypeListItemDto_IsRejectedAtRegistration()
        {
            var builder = HostMethod.For<ITestHandlers, DummyInput, DummyResult>("m");
            var ex = Assert.Throws<ArgumentException>(
                () => builder.Inputs(i => i.List<int>("items", (x, v) => { }, item => { })));
            Assert.Contains("must be classes", ex.Message);
        }

        private sealed class DummyInput
        {
        }

        // --- compact builder preconditions ---

        [Fact]
        public void Compact_MissingHandler_FailsLoud()
        {
            var ex = Assert.Throws<InvalidOperationException>(() => CompactHostMethod
                .For<ITestHandlers>("m")
                .CreateInput(CreateDummyInput)
                .Build());
            Assert.Contains("has no handler", ex.Message);
        }

        [Fact]
        public void Compact_MissingCreateInput_FailsLoud()
        {
            var ex = Assert.Throws<InvalidOperationException>(() => CompactHostMethod
                .For<ITestHandlers>("m")
                .Handler(InvokeDummy)
                .Build());
            Assert.Contains("has no input factory", ex.Message);
        }

        [Fact]
        public void Compact_InlineShapeRules_MatchClassicMessages()
        {
            var ex = Assert.Throws<InvalidOperationException>(() => CompactHostMethod
                .For<ITestHandlers>("m")
                .CreateInput(CreateDummyInput)
                .ResultBool("ok", ReadDummyOk)
                .ResultBool("second", ReadDummyOk)
                .Inline("fn_m")
                .Handler(InvokeDummy)
                .Build());
            Assert.Contains("exactly one scalar field (found 2)", ex.Message);
        }

        private static object CreateDummyInput()
        {
            return new DummyInput();
        }

        private static bool ReadDummyOk(object result)
        {
            return ((DummyResult)result).Ok;
        }

        private static object InvokeDummy(object handlers, object input)
        {
            return new DummyResult { Ok = true };
        }

        // --- ultra builder preconditions ---

        [Fact]
        public void Ultra_MissingHandler_FailsLoud()
        {
            var ex = Assert.Throws<InvalidOperationException>(() => UltraHostMethod
                .For<ITestHandlers>("m")
                .InputText("key")
                .ResultBool("ok")
                .Build());
            Assert.Contains("has no handler", ex.Message);
        }

        [Fact]
        public void Ultra_InlineShapeRules_MatchClassicMessages()
        {
            var ex = Assert.Throws<InvalidOperationException>(() => UltraHostMethod
                .For<ITestHandlers>("m")
                .InputList("items", item => item.Text("key"))
                .ResultBool("ok")
                .Inline("fn_m")
                .Handler(delegate { return new SqliteHostUltraResult(); })
                .Build());
            Assert.Contains("scalar fields only (no lists)", ex.Message);
        }

        // --- ultra result-shape enforcement at runtime ---

        private SqliteHostRunResult RunUltraMethod(
            Func<SqliteHostUltraCall, SqliteHostUltraResult> body,
            Action<IUltraHostMethodBuilder<ITestHandlers>> declareResults = null)
        {
            var builder = UltraHostMethod
                .For<ITestHandlers>("probe")
                .InputText("key");
            if (declareResults != null)
            {
                declareResults(builder);
            }
            else
            {
                builder.ResultLong("value");
            }
            IHostMethodSpec<ITestHandlers> spec = builder
                .Handler(delegate(object handlers, SqliteHostUltraCall call) { return body(call); })
                .Build();
            SqliteHostDefinition<ITestHandlers> definition = SqliteHostDefinition
                .ForHandlers<ITestHandlers>()
                .Methods(new List<IHostMethodSpec<ITestHandlers>> { spec });
            using var factory = new TestWorkspaceFactory();
            var runtime = new SqliteHostRuntime<ITestHandlers>(
                factory, definition, new TestHandlers(), null);
            return runtime.Run(Scripts.New(Scripts.Step(
                "s",
                Scripts.Statement(
                    "INSERT INTO call_probe (call_id, input_key) VALUES (:c, 'k')",
                    ("c", SqliteHostBindingValue.Text("call-1"))))));
        }

        [SkippableFact]
        public void Ultra_UnsetRequiredResultField_IsAHandlerError()
        {
            SampleHostFloor.SkipBelowFloor();
            SqliteHostRunResult result = RunUltraMethod(call => new SqliteHostUltraResult());
            Assert.Equal(SqliteHostRunStatus.FailedHandler, result.Status);
            Assert.Equal("handler-error", result.ErrorCode);
        }

        [SkippableFact]
        public void Ultra_UndeclaredResultField_IsAHandlerError()
        {
            SampleHostFloor.SkipBelowFloor();
            SqliteHostRunResult result = RunUltraMethod(
                call => new SqliteHostUltraResult().SetInt64("value", 1).SetInt64("extra", 2));
            Assert.Equal(SqliteHostRunStatus.FailedHandler, result.Status);
        }

        [SkippableFact]
        public void Ultra_MistypedResultField_IsAHandlerError()
        {
            SampleHostFloor.SkipBelowFloor();
            SqliteHostRunResult result = RunUltraMethod(
                call => new SqliteHostUltraResult().SetText("value", "not-a-long"));
            Assert.Equal(SqliteHostRunStatus.FailedHandler, result.Status);
        }

        [SkippableFact]
        public void Ultra_NullForRequiredNumericResultField_IsAHandlerError()
        {
            SampleHostFloor.SkipBelowFloor();
            SqliteHostRunResult result = RunUltraMethod(
                call => new SqliteHostUltraResult().SetNull("value"));
            Assert.Equal(SqliteHostRunStatus.FailedHandler, result.Status);
        }

        [SkippableFact]
        public void Ultra_NullHandlerResult_IsAHandlerError()
        {
            SampleHostFloor.SkipBelowFloor();
            SqliteHostRunResult result = RunUltraMethod(call => null);
            Assert.Equal(SqliteHostRunStatus.FailedHandler, result.Status);
        }

        [SkippableFact]
        public void Ultra_ReadingUndeclaredInputField_IsAHandlerError()
        {
            SampleHostFloor.SkipBelowFloor();
            SqliteHostRunResult result = RunUltraMethod(
                call => new SqliteHostUltraResult().SetInt64("value", call.GetInt64("missing")));
            Assert.Equal(SqliteHostRunStatus.FailedHandler, result.Status);
        }

        [SkippableFact]
        public void Ultra_UnsetOptionalResultField_WritesNullAndCompletes()
        {
            SampleHostFloor.SkipBelowFloor();
            SqliteHostRunResult result = RunUltraMethod(
                call => new SqliteHostUltraResult(),
                declareResults: b => b.ResultOptionalLong("value"));
            Assert.Equal(SqliteHostRunStatus.Completed, result.Status);
            Assert.Equal(1, result.ExecutedCallCount);
        }

        [SkippableFact]
        public void Ultra_ExplicitNullForRequiredTextResultField_SurfacesAsSqlErrorLikeClassic()
        {
            // Classic parity: a null string on a required text field passes
            // the shape layer in both profiles (classic getters may return
            // null) and the NOT NULL result column rejects it at the SQL
            // layer.
            SampleHostFloor.SkipBelowFloor();
            SqliteHostRunResult result = RunUltraMethod(
                call => new SqliteHostUltraResult().SetText("value", null),
                declareResults: b => b.ResultText("value"));
            Assert.Equal(SqliteHostRunStatus.FailedSql, result.Status);
        }

        [SkippableFact]
        public void Ultra_ResultListRows_AreShapeChecked()
        {
            SampleHostFloor.SkipBelowFloor();
            SqliteHostRunResult result = RunUltraMethod(
                call =>
                {
                    var r = new SqliteHostUltraResult().SetInt64("value", 1);
                    r.AddRow("rows").SetText("wrong_field", "x");
                    return r;
                },
                declareResults: b => b.ResultLong("value").ResultList("rows", item => item.Text("name")));
            Assert.Equal(SqliteHostRunStatus.FailedHandler, result.Status);
        }

        // --- ultra input surface ---

        [SkippableFact]
        public void Ultra_DeclaredOptionalInput_AnswersIsNullWhenAbsent()
        {
            SampleHostFloor.SkipBelowFloor();
            bool sawNull = false;
            var builder = UltraHostMethod
                .For<ITestHandlers>("probe")
                .InputText("key")
                .InputOptionalLong("bonus");
            IHostMethodSpec<ITestHandlers> spec = builder
                .ResultLong("value")
                .Handler(delegate(object handlers, SqliteHostUltraCall call)
                {
                    sawNull = call.IsNull("bonus");
                    return new SqliteHostUltraResult().SetInt64("value", 1);
                })
                .Build();
            SqliteHostDefinition<ITestHandlers> definition = SqliteHostDefinition
                .ForHandlers<ITestHandlers>()
                .Methods(new List<IHostMethodSpec<ITestHandlers>> { spec });
            using var factory = new TestWorkspaceFactory();
            var runtime = new SqliteHostRuntime<ITestHandlers>(
                factory, definition, new TestHandlers(), null);
            SqliteHostRunResult result = runtime.Run(Scripts.New(Scripts.Step(
                "s",
                Scripts.Statement(
                    "INSERT INTO call_probe (call_id, input_key) VALUES (:c, 'k')",
                    ("c", SqliteHostBindingValue.Text("call-1"))))));

            Assert.Equal(SqliteHostRunStatus.Completed, result.Status);
            Assert.True(sawNull);
        }
    }
}
