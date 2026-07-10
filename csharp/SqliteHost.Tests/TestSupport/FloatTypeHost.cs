using System.Collections.Generic;

namespace SqliteHost.Tests.TestSupport
{
    /// <summary>
    /// Hand-built test host exercising every float field builder: float32
    /// and float64, required and optional, scalar and list item — in both
    /// directions.
    /// </summary>
    public sealed class FloatPairItem
    {
        public float F32 { get; set; }
        public double F64 { get; set; }
        public float? OptF32 { get; set; }
        public double? OptF64 { get; set; }
    }

    public sealed class FloatTypeInput
    {
        public float F32 { get; set; }
        public double F64 { get; set; }
        public float? OptF32 { get; set; }
        public double? OptF64 { get; set; }
        public List<FloatPairItem> Pairs { get; set; } = new List<FloatPairItem>();
    }

    public sealed class FloatTypeResult
    {
        public float F32 { get; set; }
        public double F64 { get; set; }
        public float? OptF32 { get; set; }
        public double? OptF64 { get; set; }
        public List<FloatPairItem> EchoPairs { get; set; } = new List<FloatPairItem>();
    }

    public interface IFloatTypeHandlers
    {
        FloatTypeResult FloatType(FloatTypeInput input);
    }

    /// <summary>Echoes the input back as the result.</summary>
    public sealed class EchoFloatTypeHandlers : IFloatTypeHandlers
    {
        public FloatTypeInput LastInput { get; private set; }

        public FloatTypeResult FloatType(FloatTypeInput input)
        {
            LastInput = input;
            return new FloatTypeResult
            {
                F32 = input.F32,
                F64 = input.F64,
                OptF32 = input.OptF32,
                OptF64 = input.OptF64,
                EchoPairs = input.Pairs
            };
        }
    }

    public static class FloatTypeHost
    {
        public static SqliteHostDefinition<IFloatTypeHandlers> Build()
        {
            return SqliteHostDefinition
                .ForHandlers<IFloatTypeHandlers>()
                .ApiLevel(1)
                .Methods(new IHostMethodSpec<IFloatTypeHandlers>[]
                {
                    HostMethod
                        .For<IFloatTypeHandlers, FloatTypeInput, FloatTypeResult>("floatType")
                        .ApiLevel(1)
                        .Inputs(i => i
                            .Float("f32", (x, v) => x.F32 = v)
                            .Double("f64", (x, v) => x.F64 = v)
                            .OptionalFloat("opt_f32", (x, v) => x.OptF32 = v)
                            .OptionalDouble("opt_f64", (x, v) => x.OptF64 = v)
                            .List<FloatPairItem>("pairs", (x, v) => x.Pairs = v, item => item
                                .Float("f32", (p, v) => p.F32 = v)
                                .Double("f64", (p, v) => p.F64 = v)
                                .OptionalFloat("opt_f32", (p, v) => p.OptF32 = v)
                                .OptionalDouble("opt_f64", (p, v) => p.OptF64 = v)))
                        .Results(r => r
                            .Float("f32", x => x.F32)
                            .Double("f64", x => x.F64)
                            .OptionalFloat("opt_f32", x => x.OptF32)
                            .OptionalDouble("opt_f64", x => x.OptF64)
                            .List<FloatPairItem>("echo_pairs", x => x.EchoPairs, item => item
                                .Float("f32", p => p.F32)
                                .Double("f64", p => p.F64)
                                .OptionalFloat("opt_f32", p => p.OptF32)
                                .OptionalDouble("opt_f64", p => p.OptF64)))
                        .Handler((handlers, input) => handlers.FloatType(input))
                        .Build()
                });
        }
    }
}
