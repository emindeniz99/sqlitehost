using System;
using System.Collections.Generic;
using CompactGen = Example.Game.Generated.Compact;
using UltraGen = Example.Game.Generated.Ultra;

namespace SqliteHost.Tests.TestSupport
{
    /// <summary>
    /// Compact-profile twin of <see cref="FakeGameHandlers"/>: same
    /// dictionary-backed semantics against the compact sample's generated
    /// interface, so profile-equivalence tests can compare runs
    /// side-by-side.
    /// </summary>
    public sealed class CompactFakeGameHandlers : CompactGen.IGeneratedHostHandlers
    {
        public Dictionary<string, long> Storage { get; } = new Dictionary<string, long>(StringComparer.Ordinal);
        public Dictionary<string, byte[]> Blobs { get; } = new Dictionary<string, byte[]>(StringComparer.Ordinal);
        public Dictionary<string, List<double>> Scores { get; } = new Dictionary<string, List<double>>(StringComparer.Ordinal);
        public List<string> Log { get; } = new List<string>();

        public CompactGen.GetValueResult GetValue(CompactGen.GetValueInput input)
        {
            Log.Add("getValue:" + input.Key);
            return new CompactGen.GetValueResult
            {
                Value = Storage.TryGetValue(input.Key, out long value) ? value : 0
            };
        }

        public CompactGen.SetValueResult SetValue(CompactGen.SetValueInput input)
        {
            Log.Add("setValue:" + input.Key + ":" + input.Value);
            Storage[input.Key] = input.Value;
            return new CompactGen.SetValueResult { Success = true };
        }

        public CompactGen.GetValuesResult GetValues(CompactGen.GetValuesInput input)
        {
            Log.Add("getValues:" + input.Keys.Count);
            var result = new CompactGen.GetValuesResult();
            foreach (CompactGen.KeyQueryItem item in input.Keys)
            {
                bool found = Storage.TryGetValue(item.Key, out long value);
                result.Entries.Add(new CompactGen.ValueEntryItem
                {
                    Key = item.Key,
                    Value = found ? value : (input.DefaultValue ?? 0),
                    Found = found
                });
            }
            return result;
        }

        public CompactGen.PutBlobResult PutBlob(CompactGen.PutBlobInput input)
        {
            Log.Add("putBlob:" + input.Key);
            Blobs[input.Key] = input.Data;
            return new CompactGen.PutBlobResult { Stored = true };
        }

        public CompactGen.RecordScoreResult RecordScore(CompactGen.RecordScoreInput input)
        {
            Log.Add("recordScore:" + input.Key);
            if (!Scores.TryGetValue(input.Key, out List<double> scores))
            {
                scores = new List<double>();
                Scores[input.Key] = scores;
            }
            scores.Add(input.Score);
            double sum = 0;
            foreach (double score in scores)
            {
                sum += score;
            }
            return new CompactGen.RecordScoreResult { Average = sum / scores.Count };
        }
    }

    /// <summary>
    /// Ultra-profile twin of <see cref="FakeGameHandlers"/>: same semantics
    /// through the DTO-less call/result surface.
    /// </summary>
    public sealed class UltraFakeGameHandlers : UltraGen.IGeneratedHostHandlers
    {
        public Dictionary<string, long> Storage { get; } = new Dictionary<string, long>(StringComparer.Ordinal);
        public Dictionary<string, byte[]> Blobs { get; } = new Dictionary<string, byte[]>(StringComparer.Ordinal);
        public Dictionary<string, List<double>> Scores { get; } = new Dictionary<string, List<double>>(StringComparer.Ordinal);
        public List<string> Log { get; } = new List<string>();

        public SqliteHostUltraResult GetValue(SqliteHostUltraCall call)
        {
            string key = call.GetText("key");
            Log.Add("getValue:" + key);
            return new SqliteHostUltraResult()
                .SetInt64("value", Storage.TryGetValue(key, out long value) ? value : 0);
        }

        public SqliteHostUltraResult SetValue(SqliteHostUltraCall call)
        {
            string key = call.GetText("key");
            long value = call.GetInt64("value");
            Log.Add("setValue:" + key + ":" + value);
            Storage[key] = value;
            return new SqliteHostUltraResult().SetBool("success", true);
        }

        public SqliteHostUltraResult GetValues(SqliteHostUltraCall call)
        {
            IReadOnlyList<SqliteHostUltraRow> keys = call.GetList("keys");
            Log.Add("getValues:" + keys.Count);
            long? defaultValue = call.IsNull("default_value") ? (long?)null : call.GetInt64("default_value");
            var result = new SqliteHostUltraResult();
            foreach (SqliteHostUltraRow item in keys)
            {
                string key = item.GetText("key");
                bool found = Storage.TryGetValue(key, out long value);
                result.AddRow("entries")
                    .SetText("key", key)
                    .SetInt64("value", found ? value : (defaultValue ?? 0))
                    .SetBool("found", found);
            }
            return result;
        }

        public SqliteHostUltraResult PutBlob(SqliteHostUltraCall call)
        {
            string key = call.GetText("key");
            Log.Add("putBlob:" + key);
            Blobs[key] = call.GetBlob("data");
            return new SqliteHostUltraResult().SetBool("stored", true);
        }

        public SqliteHostUltraResult RecordScore(SqliteHostUltraCall call)
        {
            string key = call.GetText("key");
            Log.Add("recordScore:" + key);
            if (!Scores.TryGetValue(key, out List<double> scores))
            {
                scores = new List<double>();
                Scores[key] = scores;
            }
            scores.Add(call.GetFloat64("score"));
            double sum = 0;
            foreach (double score in scores)
            {
                sum += score;
            }
            return new SqliteHostUltraResult().SetFloat64("average", sum / scores.Count);
        }
    }
}
