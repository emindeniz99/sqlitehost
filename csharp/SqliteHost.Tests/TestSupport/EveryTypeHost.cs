using System.Collections.Generic;

namespace SqliteHost.Tests.TestSupport
{
    /// <summary>
    /// Hand-built test host exercising every field builder: all five scalar
    /// types, required and optional, plus a list with an optional item field
    /// — in both directions.
    /// </summary>
    public sealed class PairItem
    {
        public string K { get; set; }
        public long? OptV { get; set; }
    }

    public sealed class EveryTypeInput
    {
        public int I32 { get; set; }
        public long I64 { get; set; }
        public bool Flag { get; set; }
        public string Name { get; set; }
        public byte[] Payload { get; set; }
        public int? OptI32 { get; set; }
        public long? OptI64 { get; set; }
        public bool? OptFlag { get; set; }
        public string OptName { get; set; }
        public byte[] OptPayload { get; set; }
        public List<PairItem> Pairs { get; set; } = new List<PairItem>();
    }

    public sealed class EveryTypeResult
    {
        public int I32 { get; set; }
        public long I64 { get; set; }
        public bool Flag { get; set; }
        public string Name { get; set; }
        public byte[] Payload { get; set; }
        public int? OptI32 { get; set; }
        public long? OptI64 { get; set; }
        public bool? OptFlag { get; set; }
        public string OptName { get; set; }
        public byte[] OptPayload { get; set; }
        public List<PairItem> EchoPairs { get; set; } = new List<PairItem>();
    }

    public interface IEveryTypeHandlers
    {
        EveryTypeResult EveryType(EveryTypeInput input);
    }

    /// <summary>Echoes the input back as the result.</summary>
    public sealed class EchoEveryTypeHandlers : IEveryTypeHandlers
    {
        public EveryTypeInput LastInput { get; private set; }

        public EveryTypeResult EveryType(EveryTypeInput input)
        {
            LastInput = input;
            return new EveryTypeResult
            {
                I32 = input.I32,
                I64 = input.I64,
                Flag = input.Flag,
                Name = input.Name,
                Payload = input.Payload,
                OptI32 = input.OptI32,
                OptI64 = input.OptI64,
                OptFlag = input.OptFlag,
                OptName = input.OptName,
                OptPayload = input.OptPayload,
                EchoPairs = input.Pairs
            };
        }
    }

    public static class EveryTypeHost
    {
        public static SqliteHostDefinition<IEveryTypeHandlers> Build()
        {
            return SqliteHostDefinition
                .ForHandlers<IEveryTypeHandlers>()
                .ApiLevel(1)
                .Methods(new IHostMethodSpec<IEveryTypeHandlers>[]
                {
                    HostMethod
                        .For<IEveryTypeHandlers, EveryTypeInput, EveryTypeResult>("everyType")
                        .ApiLevel(1)
                        .Inputs(i => i
                            .Int("i32", (x, v) => x.I32 = v)
                            .Long("i64", (x, v) => x.I64 = v)
                            .Bool("flag", (x, v) => x.Flag = v)
                            .Text("name", (x, v) => x.Name = v)
                            .Blob("payload", (x, v) => x.Payload = v)
                            .OptionalInt("opt_i32", (x, v) => x.OptI32 = v)
                            .OptionalLong("opt_i64", (x, v) => x.OptI64 = v)
                            .OptionalBool("opt_flag", (x, v) => x.OptFlag = v)
                            .OptionalText("opt_name", (x, v) => x.OptName = v)
                            .OptionalBlob("opt_payload", (x, v) => x.OptPayload = v)
                            .List<PairItem>("pairs", (x, v) => x.Pairs = v, item => item
                                .Text("k", (p, v) => p.K = v)
                                .OptionalLong("opt_v", (p, v) => p.OptV = v)))
                        .Results(r => r
                            .Int("i32", x => x.I32)
                            .Long("i64", x => x.I64)
                            .Bool("flag", x => x.Flag)
                            .Text("name", x => x.Name)
                            .Blob("payload", x => x.Payload)
                            .OptionalInt("opt_i32", x => x.OptI32)
                            .OptionalLong("opt_i64", x => x.OptI64)
                            .OptionalBool("opt_flag", x => x.OptFlag)
                            .OptionalText("opt_name", x => x.OptName)
                            .OptionalBlob("opt_payload", x => x.OptPayload)
                            .List<PairItem>("echo_pairs", x => x.EchoPairs, item => item
                                .Text("k", p => p.K)
                                .OptionalLong("opt_v", p => p.OptV)))
                        .Handler((handlers, input) => handlers.EveryType(input))
                        .Build()
                });
        }
    }
}
