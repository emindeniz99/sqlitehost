// No-GVM control: same logic, non-generic interface method (object-erased).
using System;
using System.Collections.Generic;

interface IFace
{
    IReadOnlyList<object> Get(Func<int, object> f);
}

sealed class Impl : IFace
{
    public IReadOnlyList<object> Get(Func<int, object> f)
    {
        var r = new List<object>();
        for (int i = 0; i < 3; i++) r.Add(f(i));
        return r;
    }
}

static class Program
{
    static void Main()
    {
        IFace face = new Impl();
        int n = face.Get(i => (object)(i * 2)).Count + face.Get(i => (object)i.ToString()).Count
              + ((long)face.Get(i => (object)(long)i)[2]).GetHashCode() % 7;
        Console.WriteLine(n);
    }
}
