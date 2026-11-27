using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using ShkoOnline.EarlyBound.Model;

namespace ShkoOnline.EarlyBound.Completion;

/// <summary>Where a member of a generated class can be written.</summary>
internal enum MemberContextKind
{
    /// <summary><c>account.…</c>: any property of the row.</summary>
    Instance,

    /// <summary><c>new Account { … }</c>: the properties that can be set.</summary>
    Initializer,

    /// <summary><c>Account.Fields.…</c>: the constants with the column names.</summary>
    Fields,
}

/// <summary>A class that can be derived from a referenced table class, but is not generated yet.</summary>
internal sealed class TestEntityCandidate
{
    public TestEntityCandidate(EntityModel entity, string className, string insertion, string baseAssembly)
    {
        Entity = entity;
        ClassName = className;
        Insertion = insertion;
        BaseAssembly = baseAssembly;
    }

    public EntityModel Entity { get; }

    public string ClassName { get; }

    /// <summary>The name to write: qualified when the namespace is not imported.</summary>
    public string Insertion { get; }

    /// <summary>The assembly the table's own class comes from.</summary>
    public string BaseAssembly { get; }
}

/// <summary>A spot where members of one generated class belong, and which of them are not generated yet.</summary>
internal sealed class MemberRequest
{
    public MemberRequest(MemberContextKind kind, EntityModel entity, List<AttributeModel> attributes, List<RelationshipModel> relationships)
    {
        Kind = kind;
        Entity = entity;
        Attributes = attributes;
        Relationships = relationships;
    }

    public MemberContextKind Kind { get; }

    public EntityModel Entity { get; }

    /// <summary>Columns of the table that have no generated member in this context yet.</summary>
    public List<AttributeModel> Attributes { get; }

    /// <summary>Relationships of the table that have no generated property yet.</summary>
    public List<RelationshipModel> Relationships { get; }
}

/// <summary>
/// When only used members are generated, the compiler's own completion can only show the ones
/// already in use. This finds the places where the rest should be offered: after <c>account.</c>,
/// in an object initializer, and under <c>Account.Fields.</c>.
/// </summary>
internal static class MemberContextAnalyzer
{
    /// <summary>
    /// A check on syntax alone. Most completions are nowhere near a generated class, and this
    /// spares them the semantic model.
    /// </summary>
    public static bool MayBeMemberSlot(SyntaxNode root, int position) => TryFindSlot(root, position, out _, out _);

    public static MemberRequest? Analyze(SyntaxNode root, int position, SemanticModel model, ProjectMetadata metadata, CancellationToken cancellationToken)
    {
        if (!TryFindSlot(root, position, out var receiver, out var initializer))
        {
            return null;
        }

        if (initializer != null)
        {
            return ForInitializer(initializer, model, metadata, cancellationToken);
        }

        // "Account.Fields." names a type; "account." an instance.
        var symbol = model.GetSymbolInfo(receiver!, cancellationToken).Symbol;
        if (symbol is INamedTypeSymbol namedType)
        {
            return ForFields(namedType, model, metadata);
        }

        if (symbol is INamespaceSymbol)
        {
            return null;
        }

        var type = model.GetTypeInfo(receiver!, cancellationToken).Type;
        var entity = FindGeneratedClass(type, model.Compilation, metadata, out var generatedClass);
        if (entity == null)
        {
            return null;
        }

        return new MemberRequest(
            MemberContextKind.Instance,
            entity,
            entity.Attributes.Where(a => !HasMember(generatedClass!, a.PropertyName)).ToList(),
            entity.Relationships.Where(r => !HasMember(generatedClass!, r.PropertyName)).ToList());
    }

    /// <summary>The expression before the dot, or the initializer the caret is in.</summary>
    private static bool TryFindSlot(SyntaxNode root, int position, out ExpressionSyntax? receiver, out InitializerExpressionSyntax? initializer)
    {
        receiver = null;
        initializer = null;
        var token = root.FindToken(position);
        if (token.SpanStart >= position)
        {
            token = token.GetPreviousToken();
        }

        switch (token.Kind())
        {
            case SyntaxKind.DotToken:
                receiver = ReceiverOfDot(token.Parent);
                return receiver != null;

            case SyntaxKind.IdentifierToken when token.Span.End >= position && token.Parent is IdentifierNameSyntax name:
                // A member name that has been started: account.Acc|, new Account { Acc| }
                switch (name.Parent)
                {
                    case MemberAccessExpressionSyntax access when access.Name == name:
                    case MemberBindingExpressionSyntax binding when binding.Name == name:
                        receiver = ReceiverOfDot(name.Parent);
                        return receiver != null;

                    case AssignmentExpressionSyntax assignment when assignment.Left == name:
                        initializer = ObjectInitializer(assignment.Parent);
                        return initializer != null;

                    default:
                        // Without "=" yet, the parser takes "{ Acc }" for a collection initializer.
                        initializer = ObjectInitializer(name.Parent);
                        return initializer != null;
                }

            case SyntaxKind.OpenBraceToken:
            case SyntaxKind.CommaToken:
                initializer = ObjectInitializer(token.Parent);
                return initializer != null;

            default:
                return false;
        }
    }

    private static ExpressionSyntax? ReceiverOfDot(SyntaxNode? node) => node switch
    {
        MemberAccessExpressionSyntax access when access.IsKind(SyntaxKind.SimpleMemberAccessExpression) => access.Expression,
        MemberBindingExpressionSyntax binding => EntityInference.Receiver(binding),
        _ => null,
    };

    private static InitializerExpressionSyntax? ObjectInitializer(SyntaxNode? node) =>
        node is InitializerExpressionSyntax initializer
        && (initializer.IsKind(SyntaxKind.ObjectInitializerExpression) || initializer.IsKind(SyntaxKind.CollectionInitializerExpression))
        && initializer.Parent is BaseObjectCreationExpressionSyntax
            ? initializer
            : null;

    private static MemberRequest? ForInitializer(InitializerExpressionSyntax initializer, SemanticModel model, ProjectMetadata metadata, CancellationToken cancellationToken)
    {
        var type = model.GetTypeInfo((ExpressionSyntax)initializer.Parent!, cancellationToken).Type;
        var entity = FindGeneratedClass(type, model.Compilation, metadata, out var generatedClass);
        if (entity == null)
        {
            return null;
        }

        // Only what can be assigned, and not what the initializer assigns already.
        var assigned = new HashSet<string>(StringComparer.Ordinal);
        foreach (var expression in initializer.Expressions)
        {
            if (expression is AssignmentExpressionSyntax { Left: IdentifierNameSyntax left })
            {
                assigned.Add(left.Identifier.ValueText);
            }
        }

        bool IsOpen(string name, bool hasSetter) =>
            hasSetter && !assigned.Contains(name.TrimStart('@')) && !HasMember(generatedClass!, name);

        return new MemberRequest(
            MemberContextKind.Initializer,
            entity,
            entity.Attributes.Where(a => IsOpen(a.PropertyName, a.HasSetter)).ToList(),
            entity.Relationships.Where(r => IsOpen(r.PropertyName, r.HasSetter)).ToList());
    }

    private static MemberRequest? ForFields(INamedTypeSymbol type, SemanticModel model, ProjectMetadata metadata)
    {
        var settings = metadata.Model.Settings;
        if (!settings.GenerateAttributeNameConsts
            || type.Name != settings.AttributeConstsClassName
            || type.ContainingType == null)
        {
            return null;
        }

        var entity = FindGeneratedClass(type.ContainingType, model.Compilation, metadata, out _);
        if (entity == null)
        {
            return null;
        }

        return new MemberRequest(
            MemberContextKind.Fields,
            entity,
            entity.Attributes.Where(a => !HasMember(type, a.PropertyName)).ToList(),
            new List<RelationshipModel>());
    }

    /// <summary>
    /// The table a type is a generated class of, and the class members get added to.
    /// </summary>
    /// <remarks>
    /// That class is one this project's generator writes: the table's class when the project
    /// generates it itself, or the class derived from it (<c>AccountTestEntity</c>) when the table's
    /// class comes from a reference. A class from another assembly does not grow members when
    /// they are used, so nothing is offered on it.
    /// </remarks>
    private static EntityModel? FindGeneratedClass(ITypeSymbol? type, Compilation compilation, ProjectMetadata metadata, out INamedTypeSymbol? generatedClass)
    {
        generatedClass = null;
        var model = metadata.Model;
        for (var current = type as INamedTypeSymbol; current != null; current = current.BaseType)
        {
            if (!SymbolEqualityComparer.Default.Equals(current.ContainingAssembly, compilation.Assembly)
                || current.ContainingNamespace?.ToDisplayString() != model.Namespace)
            {
                continue;
            }

            var logicalName = LogicalNameOf(current);
            var entity = logicalName == null ? null : model.FindEntity(logicalName);
            if (entity == null)
            {
                continue;
            }

            var className = entity.ClassName.TrimStart('@');
            if (current.Name == className || current.Name == GenerationPlan.TestEntityName(entity))
            {
                generatedClass = current;
                return entity;
            }
        }

        return null;
    }

    /// <summary>
    /// For <c>new …</c>: the classes that can be derived from a referenced table class but have
    /// not been generated, because the code has not named them yet.
    /// </summary>
    public static List<TestEntityCandidate>? AnalyzeObjectCreation(SyntaxNode root, int position, SemanticModel model, ProjectMetadata metadata, CancellationToken cancellationToken)
    {
        if (!IsAfterNew(root, position))
        {
            return null;
        }

        var compilation = model.Compilation;
        var @namespace = metadata.Model.Namespace;
        List<TestEntityCandidate>? result = null;
        foreach (var entity in metadata.Model.Entities)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var className = entity.ClassName.TrimStart('@');
            var testName = GenerationPlan.TestEntityName(entity);

            // The table's class has to come from a reference, and its derived class not exist yet.
            var tableClass = compilation.GetTypeByMetadataName(@namespace + "." + className);
            if (tableClass == null
                || SymbolEqualityComparer.Default.Equals(tableClass.ContainingAssembly, compilation.Assembly)
                || tableClass.IsSealed
                || LogicalNameOf(tableClass) != entity.Definition.LogicalName
                || !compilation.IsSymbolAccessibleWithin(tableClass, compilation.Assembly)
                || compilation.Assembly.GetTypeByMetadataName(@namespace + "." + testName) != null)
            {
                continue;
            }

            var inScope = model.LookupNamespacesAndTypes(position, name: tableClass.Name)
                .Any(symbol => SymbolEqualityComparer.Default.Equals(symbol, tableClass));
            (result ??= new List<TestEntityCandidate>()).Add(new TestEntityCandidate(
                entity,
                testName,
                inScope ? testName : @namespace + "." + testName,
                tableClass.ContainingAssembly?.Name ?? string.Empty));
        }

        return result;
    }

    /// <summary>True right after <c>new</c>, or in the type name that follows it.</summary>
    public static bool IsAfterNew(SyntaxNode root, int position)
    {
        var token = root.FindToken(position);
        if (token.SpanStart >= position)
        {
            token = token.GetPreviousToken();
        }

        if (token.IsKind(SyntaxKind.NewKeyword))
        {
            return token.Parent is BaseObjectCreationExpressionSyntax or ArrayCreationExpressionSyntax or AnonymousObjectCreationExpressionSyntax;
        }

        return token.IsKind(SyntaxKind.IdentifierToken)
            && token.Span.End >= position
            && token.Parent is IdentifierNameSyntax { Parent: ObjectCreationExpressionSyntax creation } name
            && creation.Type == name;
    }

    private static string? LogicalNameOf(INamedTypeSymbol type)
    {
        foreach (var attribute in type.GetAttributes())
        {
            if (attribute.AttributeClass?.Name == "EntityLogicalNameAttribute"
                && attribute.ConstructorArguments.Length == 1
                && attribute.ConstructorArguments[0].Value is string logicalName)
            {
                return logicalName;
            }
        }

        return null;
    }

    /// <summary>True when the class, or a class it derives from, declares a member of that name.</summary>
    private static bool HasMember(INamedTypeSymbol type, string name)
    {
        name = name.TrimStart('@');
        for (var current = type; current != null; current = current.BaseType)
        {
            if (!current.GetMembers(name).IsEmpty)
            {
                return true;
            }
        }

        return false;
    }
}
