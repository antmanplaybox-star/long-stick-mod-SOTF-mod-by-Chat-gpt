namespace LongStickSpear.Core;

public static class PrefabGraph
{
    public static Dictionary<TKey, T> CloneAndRewrite<T, TKey>(IEnumerable<T> sources,
        Func<T, TKey> identity, Func<T, T> clone, Action<T, Func<T, T>> rewrite,
        Func<T, T> externalReference) where TKey : notnull
    {
        var copies = new Dictionary<TKey, T>();
        foreach (var source in sources) copies.Add(identity(source), clone(source));
        T Resolve(T reference) => copies.TryGetValue(identity(reference), out var copy) ? copy : externalReference(reference);
        foreach (var copy in copies.Values) rewrite(copy, Resolve);
        return copies;
    }

    public static List<T> Gather<T, TKey>(T root, Func<T, IEnumerable<T>> references,
        Func<T, bool> canVisit, Func<T, TKey> identity, int limit = 128) where TKey : notnull
    {
        if (limit < 1) throw new ArgumentOutOfRangeException(nameof(limit));
        var result = new List<T>();
        var seen = new HashSet<TKey>();
        var pending = new Queue<T>(); pending.Enqueue(root);
        while (pending.Count > 0 && result.Count < limit)
        {
            var node = pending.Dequeue();
            if (!canVisit(node) || !seen.Add(identity(node))) continue;
            result.Add(node);
            foreach (var next in references(node)) pending.Enqueue(next);
        }
        return result;
    }
}
