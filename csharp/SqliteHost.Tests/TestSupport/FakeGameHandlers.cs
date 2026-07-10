using System;
using System.Collections.Generic;
using Example.Game.Generated;

namespace SqliteHost.Tests.TestSupport
{
    /// <summary>Dictionary-backed fake implementation of the generated handler interface.</summary>
    public sealed class FakeGameHandlers : IGeneratedHostHandlers
    {
        public Dictionary<string, long> Storage { get; } = new Dictionary<string, long>(StringComparer.Ordinal);
        public Dictionary<string, byte[]> Blobs { get; } = new Dictionary<string, byte[]>(StringComparer.Ordinal);

        /// <summary>Invocation log: "method:key[:value]" entries in call order.</summary>
        public List<string> Log { get; } = new List<string>();

        public GetValuesInput LastGetValuesInput { get; private set; }
        public PutBlobInput LastPutBlobInput { get; private set; }

        public Func<GetValueInput, GetValueResult> GetValueOverride { get; set; }

        public GetValueResult GetValue(GetValueInput input)
        {
            Log.Add("getValue:" + input.Key);
            if (GetValueOverride != null)
            {
                return GetValueOverride(input);
            }
            return new GetValueResult
            {
                Value = Storage.TryGetValue(input.Key, out long value) ? value : 0
            };
        }

        public SetValueResult SetValue(SetValueInput input)
        {
            Log.Add("setValue:" + input.Key + ":" + input.Value);
            Storage[input.Key] = input.Value;
            return new SetValueResult { Success = true };
        }

        public GetValuesResult GetValues(GetValuesInput input)
        {
            Log.Add("getValues:" + input.Keys.Count);
            LastGetValuesInput = input;
            var result = new GetValuesResult();
            foreach (KeyQueryItem item in input.Keys)
            {
                bool found = Storage.TryGetValue(item.Key, out long value);
                result.Entries.Add(new ValueEntryItem
                {
                    Key = item.Key,
                    Value = found ? value : (input.DefaultValue ?? 0),
                    Found = found
                });
            }
            return result;
        }

        public PutBlobResult PutBlob(PutBlobInput input)
        {
            Log.Add("putBlob:" + input.Key);
            LastPutBlobInput = input;
            Blobs[input.Key] = input.Data;
            return new PutBlobResult { Stored = true };
        }
    }
}
