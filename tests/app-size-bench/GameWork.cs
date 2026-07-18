using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

// Dummy "real game" workload. Exercises the BCL surface any shipping game
// already pays for under AOT: generic collections (several instantiations),
// interface/virtual dispatch, delegates, StringBuilder + invariant
// formatting/parsing, UTF8 + base64, byte[] handling, sorting/comparers,
// HashSet, custom exceptions, enums/switch. Deterministic (seeded), all
// results feed a checksum that gets printed so nothing is trimmed away.
namespace DummyGame {

public enum UnitKind { Soldier, Archer, Mage }

public interface IUnit {
    string Name { get; }
    UnitKind Kind { get; }
    int Tick(int t);
}

public sealed class Soldier : IUnit {
    public string Name { get { return "soldier"; } }
    public UnitKind Kind { get { return UnitKind.Soldier; } }
    public int Tick(int t) { return t * 3 + 1; }
}
public sealed class Archer : IUnit {
    public string Name { get { return "archer"; } }
    public UnitKind Kind { get { return UnitKind.Archer; } }
    public int Tick(int t) { return t * 5 - 2; }
}
public sealed class Mage : IUnit {
    public string Name { get { return "mage"; } }
    public UnitKind Kind { get { return UnitKind.Mage; } }
    public int Tick(int t) { return (t * t) % 97; }
}

public struct Vec2 {
    public float X; public float Y;
    public Vec2(float x, float y) { X = x; Y = y; }
    public float Dot(Vec2 o) { return X * o.X + Y * o.Y; }
}

public sealed class ItemStack {
    public string Id;
    public int Count;
    public double Weight;
    public byte[] Payload;
}

public sealed class SaveGame {
    public string PlayerName;
    public long Gold;
    public double Elo;
    public List<ItemStack> Inventory = new List<ItemStack>();
    public Dictionary<string, long> Currencies = new Dictionary<string, long>(StringComparer.Ordinal);
}

public sealed class GameDataException : Exception {
    public GameDataException(string field, int line)
        : base(string.Format(CultureInfo.InvariantCulture, "bad save field '{0}' at line {1}", field, line)) { }
}

public static class GameWork {

    static int Sum<T>(List<T> xs, Func<T, int> f) {
        int s = 0;
        for (int i = 0; i < xs.Count; i++) s += f(xs[i]);
        return s;
    }

    static List<TOut> MapList<TIn, TOut>(List<TIn> xs, Func<TIn, TOut> f) {
        var r = new List<TOut>(xs.Count);
        for (int i = 0; i < xs.Count; i++) r.Add(f(xs[i]));
        return r;
    }

    static string Serialize(SaveGame g) {
        var sb = new StringBuilder(256);
        sb.Append("name=").Append(g.PlayerName).Append('\n');
        sb.Append("gold=").Append(g.Gold.ToString(CultureInfo.InvariantCulture)).Append('\n');
        sb.Append("elo=").Append(g.Elo.ToString("0.###", CultureInfo.InvariantCulture)).Append('\n');
        foreach (var it in g.Inventory) {
            sb.Append("item=").Append(it.Id).Append(',')
              .Append(it.Count.ToString(CultureInfo.InvariantCulture)).Append(',')
              .Append(it.Weight.ToString("0.###", CultureInfo.InvariantCulture)).Append(',')
              .Append(Convert.ToBase64String(it.Payload)).Append('\n');
        }
        foreach (var kv in g.Currencies) {
            sb.Append("cur=").Append(kv.Key).Append(':')
              .Append(kv.Value.ToString(CultureInfo.InvariantCulture)).Append('\n');
        }
        return sb.ToString();
    }

    static SaveGame Parse(string text) {
        var g = new SaveGame();
        var lines = text.Split('\n');
        for (int i = 0; i < lines.Length; i++) {
            var line = lines[i];
            if (line.Length == 0) continue;
            int eq = line.IndexOf('=');
            if (eq < 0) throw new GameDataException(line, i);
            string key = line.Substring(0, eq);
            string val = line.Substring(eq + 1);
            switch (key) {
                case "name": g.PlayerName = val; break;
                case "gold": g.Gold = long.Parse(val, CultureInfo.InvariantCulture); break;
                case "elo": g.Elo = double.Parse(val, CultureInfo.InvariantCulture); break;
                case "item": {
                    var parts = val.Split(',');
                    if (parts.Length != 4) throw new GameDataException(key, i);
                    g.Inventory.Add(new ItemStack {
                        Id = parts[0],
                        Count = int.Parse(parts[1], CultureInfo.InvariantCulture),
                        Weight = double.Parse(parts[2], CultureInfo.InvariantCulture),
                        Payload = Convert.FromBase64String(parts[3]),
                    });
                    break;
                }
                case "cur": {
                    int c = val.IndexOf(':');
                    g.Currencies[val.Substring(0, c)] = long.Parse(val.Substring(c + 1), CultureInfo.InvariantCulture);
                    break;
                }
                default: throw new GameDataException(key, i);
            }
        }
        return g;
    }

    public static int RunAll(int seed) {
        var rng = new Random(seed);
        int checksum = seed;

        // Squad simulation: interface dispatch + generic sum + delegate table.
        var squad = new List<IUnit> { new Soldier(), new Archer(), new Mage(), new Soldier() };
        for (int t = 0; t < 16; t++) checksum += Sum(squad, u => u.Tick(t));
        var abilities = new Dictionary<string, Func<int, int>>(StringComparer.Ordinal) {
            ["fireball"] = x => x * 7 + 3,
            ["heal"] = x => x / 2 + 11,
            ["dash"] = x => x ^ 0x5f,
        };
        foreach (var kv in abilities) checksum += kv.Value(rng.Next(100)) + kv.Key.Length;

        // Movement math on structs + float lists.
        var path = new List<Vec2>();
        for (int i = 0; i < 32; i++) path.Add(new Vec2(i * 0.5f, (i % 7) - 3.0f));
        float acc = 0;
        for (int i = 1; i < path.Count; i++) acc += path[i].Dot(path[i - 1]);
        checksum += (int)acc;
        var speeds = new List<double>();
        for (int i = 0; i < 24; i++) speeds.Add(rng.NextDouble() * 9.5);
        speeds.Sort();
        checksum += (int)(speeds[speeds.Count - 1] * 1000);

        // Save/load roundtrip: DTOs, StringBuilder, invariant parse/format,
        // base64 + UTF8 + byte[].
        var save = new SaveGame { PlayerName = "oyuncu_bir", Gold = 123456789012L, Elo = 1723.456 };
        for (int i = 0; i < 12; i++) {
            var payload = new byte[8 + (i % 5)];
            rng.NextBytes(payload);
            save.Inventory.Add(new ItemStack {
                Id = "item_" + i.ToString(CultureInfo.InvariantCulture),
                Count = rng.Next(1, 99),
                Weight = i * 0.75,
                Payload = payload,
            });
        }
        save.Currencies["gem"] = 4200;
        save.Currencies["token"] = 17;
        string blob = Serialize(save);
        var roundtrip = Parse(blob);
        checksum += (int)(roundtrip.Gold % 100000) + roundtrip.Inventory.Count + blob.Length;
        byte[] utf8 = Encoding.UTF8.GetBytes(blob);
        string b64 = Convert.ToBase64String(utf8);
        checksum += Encoding.UTF8.GetString(Convert.FromBase64String(b64)).Length;

        // Inventory sort with Comparison<T> + map to summary strings + Join/Split.
        roundtrip.Inventory.Sort((a, b) => {
            int c = b.Count.CompareTo(a.Count);
            return c != 0 ? c : string.CompareOrdinal(a.Id, b.Id);
        });
        var summaries = MapList(roundtrip.Inventory, it =>
            string.Format(CultureInfo.InvariantCulture, "{0}x{1}", it.Id, it.Count));
        string joined = string.Join(";", summaries);
        checksum += joined.Split(';').Length + joined.Replace("item_", "#").Length;
        if (joined.IndexOf("item_3", StringComparison.Ordinal) >= 0) checksum += 5;
        checksum += joined.ToUpperInvariant().Length;

        // Achievements: HashSet + enum/switch state machine.
        var unlocked = new HashSet<string>(StringComparer.Ordinal);
        foreach (var u in squad) {
            switch (u.Kind) {
                case UnitKind.Soldier: unlocked.Add("first_blood"); break;
                case UnitKind.Archer: unlocked.Add("eagle_eye"); break;
                case UnitKind.Mage: unlocked.Add("arcane"); break;
            }
        }
        checksum += unlocked.Count * 13;

        // Error paths every game has: catch parse garbage + domain exception.
        try { Parse("gold=not_a_number\n"); }
        catch (FormatException e) { checksum += e.Message.Length > 0 ? 3 : 0; }
        try { Parse("mystery=1\n"); }
        catch (GameDataException e) { checksum += e.Message.Length; }

        return checksum;
    }
}
}
