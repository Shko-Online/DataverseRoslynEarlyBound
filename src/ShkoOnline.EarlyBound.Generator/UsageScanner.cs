using System.Collections;
using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace ShkoOnline.EarlyBound.Generator;

/// <summary>Finds which of the names the generator tracks a C# file mentions.</summary>
internal static class UsageScanner
{
    /// <summary>
    /// The tracked names that occur as identifiers in <paramref name="tree"/>, in ordinal order.
    /// </summary>
    /// <remarks>
    /// Every identifier counts, wherever it stands: <c>account.Name</c>, <c>new Account { Name = … }</c>,
    /// <c>Account.Fields.Name</c>, <c>nameof</c>, a pattern, a bare <c>Name</c> inside a partial class,
    /// a <c>cref</c> in a documentation comment. Looking at tokens instead of bound symbols is what makes
    /// this possible before the members exist, and cheap enough to do on every edit.
    /// </remarks>
    public static EquatableArray<string> Scan(SyntaxTree tree, HashSet<string> trackedNames, CancellationToken cancellationToken)
    {
        if (trackedNames.Count == 0)
        {
            return EquatableArray<string>.Empty;
        }

        SortedSet<string>? found = null;
        var identifier = (int)SyntaxKind.IdentifierToken;
        foreach (var token in tree.GetRoot(cancellationToken).DescendantTokens(descendIntoTrivia: true))
        {
            if (token.RawKind != identifier)
            {
                continue;
            }

            var name = token.ValueText;
            if (trackedNames.Contains(name))
            {
                (found ??= new SortedSet<string>(StringComparer.Ordinal)).Add(name);
            }
        }

        cancellationToken.ThrowIfCancellationRequested();
        return found == null ? EquatableArray<string>.Empty : new EquatableArray<string>(found.ToImmutableArray());
    }

    /// <summary>The names used anywhere in the project.</summary>
    public static EquatableArray<string> Merge(ImmutableArray<EquatableArray<string>> perTree)
    {
        SortedSet<string>? all = null;
        foreach (var names in perTree)
        {
            foreach (var name in names)
            {
                (all ??= new SortedSet<string>(StringComparer.Ordinal)).Add(name);
            }
        }

        return all == null ? EquatableArray<string>.Empty : new EquatableArray<string>(all.ToImmutableArray());
    }
}

/// <summary>
/// An immutable array compared by its contents, so the generator pipeline can tell that a step
/// produced the same answer as last time and skip what follows.
/// </summary>
internal readonly struct EquatableArray<T> : IEquatable<EquatableArray<T>>, IEnumerable<T>
    where T : IEquatable<T>
{
    public static readonly EquatableArray<T> Empty = new(ImmutableArray<T>.Empty);

    private readonly ImmutableArray<T> _items;

    public EquatableArray(ImmutableArray<T> items)
    {
        _items = items;
    }

    private ImmutableArray<T> Items => _items.IsDefault ? ImmutableArray<T>.Empty : _items;

    public int Count => Items.Length;

    public bool Equals(EquatableArray<T> other) => Items.AsSpan().SequenceEqual(other.Items.AsSpan());

    public override bool Equals(object? obj) => obj is EquatableArray<T> other && Equals(other);

    public override int GetHashCode()
    {
        var hash = 17;
        foreach (var item in Items)
        {
            hash = unchecked((hash * 31) + item.GetHashCode());
        }

        return hash;
    }

    public ImmutableArray<T>.Enumerator GetEnumerator() => Items.GetEnumerator();

    IEnumerator<T> IEnumerable<T>.GetEnumerator() => ((IEnumerable<T>)Items).GetEnumerator();

    IEnumerator IEnumerable.GetEnumerator() => ((IEnumerable)Items).GetEnumerator();
}
