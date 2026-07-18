// GVM probe: identical to nogvm/ except the interface method is GENERIC.
// The pair isolates what one generic virtual method costs under an AOT
// compiler (NativeAOT measured: +283 KB raw / +127 KB gz; IL2CPP: measure!).
using System;
using System.Collections.Generic;

interface IFace
{
    IReadOnlyList<T> Get<T>(Func<int, T> f);
}

sealed class Impl : IFace
{
    public IReadOnlyList<T> Get<T>(Func<int, T> f)
    {
        var r = new List<T>();
        for (int i = 0; i < 3; i++) r.Add(f(i));
        return r;
    }
}

static class Program
{
    static void Main()
    {
        IFace face = new Impl();
        int n = face.Get(i => i * 2).Count + face.Get(i => i.ToString()).Count
              + face.Get(i => (long)i)[2].GetHashCode() % 7;
        Console.WriteLine(n);
    }
}
