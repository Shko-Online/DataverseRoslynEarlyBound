namespace ShkoOnline.EarlyBound.Model;

/// <summary>
/// The generated types a project already gets from the assemblies it references, typically a
/// test project referencing the plug-in project. Such types are not generated a second time.
/// </summary>
public sealed class ReferencedTypes : IEquatable<ReferencedTypes>
{
    public static readonly ReferencedTypes None = new(
        new Dictionary<string, ReferencedEntity>(StringComparer.Ordinal),
        new HashSet<string>(StringComparer.Ordinal));

    private readonly IReadOnlyDictionary<string, ReferencedEntity> _entities;
    private readonly ISet<string> _typeNames;

    /// <param name="entities">The table classes that references provide, by table logical name.</param>
    /// <param name="typeNames">Names of the other generated types they provide: enums, helpers, the service context.</param>
    public ReferencedTypes(IReadOnlyDictionary<string, ReferencedEntity> entities, ISet<string> typeNames)
    {
        _entities = entities;
        _typeNames = typeNames;
    }

    public bool IsEmpty => _entities.Count == 0 && _typeNames.Count == 0;

    public IEnumerable<ReferencedEntity> Entities => _entities.Values;

    public bool TryGetEntity(string logicalName, out ReferencedEntity entity) => _entities.TryGetValue(logicalName, out entity!);

    /// <summary>True when a reference provides a type of this name in the model's namespace.</summary>
    public bool Provides(string typeName) => _typeNames.Contains(typeName.TrimStart('@'));

    public bool Equals(ReferencedTypes? other)
    {
        if (ReferenceEquals(this, other))
        {
            return true;
        }

        if (other == null || _entities.Count != other._entities.Count || !_typeNames.SetEquals(other._typeNames))
        {
            return false;
        }

        foreach (var pair in _entities)
        {
            if (!other._entities.TryGetValue(pair.Key, out var entity) || !pair.Value.Equals(entity))
            {
                return false;
            }
        }

        return true;
    }

    public override bool Equals(object? obj) => Equals(obj as ReferencedTypes);

    public override int GetHashCode() => unchecked((_entities.Count * 397) ^ _typeNames.Count);
}

/// <summary>A table class provided by a referenced assembly, and what it has.</summary>
public sealed class ReferencedEntity : IEquatable<ReferencedEntity>
{
    public ReferencedEntity(
        string logicalName,
        string assemblyName,
        bool isPublic,
        ISet<string> memberNames,
        bool hasFieldsClass,
        ISet<string> fieldNames,
        bool hasIdConstructor,
        bool hasKeyConstructor,
        bool hasKeyCollectionConstructor,
        bool hasAnonymousTypeConstructor)
    {
        LogicalName = logicalName;
        AssemblyName = assemblyName;
        IsPublic = isPublic;
        MemberNames = memberNames;
        HasFieldsClass = hasFieldsClass;
        FieldNames = fieldNames;
        HasIdConstructor = hasIdConstructor;
        HasKeyConstructor = hasKeyConstructor;
        HasKeyCollectionConstructor = hasKeyCollectionConstructor;
        HasAnonymousTypeConstructor = hasAnonymousTypeConstructor;
    }

    public string LogicalName { get; }

    /// <summary>Name of the assembly the class comes from, for messages and documentation comments.</summary>
    public string AssemblyName { get; }

    public bool IsPublic { get; }

    /// <summary>Names of the members the class declares.</summary>
    public ISet<string> MemberNames { get; }

    /// <summary>True when the class has the nested class with the column name constants.</summary>
    public bool HasFieldsClass { get; }

    /// <summary>Names of the constants in that nested class.</summary>
    public ISet<string> FieldNames { get; }

    /// <summary>The constructors a derived class can pass its arguments on to.</summary>
    public bool HasIdConstructor { get; }

    public bool HasKeyConstructor { get; }

    public bool HasKeyCollectionConstructor { get; }

    public bool HasAnonymousTypeConstructor { get; }

    public bool Equals(ReferencedEntity? other) =>
        ReferenceEquals(this, other)
        || (other != null
            && LogicalName == other.LogicalName
            && AssemblyName == other.AssemblyName
            && IsPublic == other.IsPublic
            && HasFieldsClass == other.HasFieldsClass
            && HasIdConstructor == other.HasIdConstructor
            && HasKeyConstructor == other.HasKeyConstructor
            && HasKeyCollectionConstructor == other.HasKeyCollectionConstructor
            && HasAnonymousTypeConstructor == other.HasAnonymousTypeConstructor
            && MemberNames.SetEquals(other.MemberNames)
            && FieldNames.SetEquals(other.FieldNames));

    public override bool Equals(object? obj) => Equals(obj as ReferencedEntity);

    public override int GetHashCode() => unchecked((LogicalName.GetHashCode() * 397) ^ MemberNames.Count);
}
