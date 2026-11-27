using System.Collections.Immutable;
using System.Composition;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Completion;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Options;
using Microsoft.CodeAnalysis.Text;
using ShkoOnline.EarlyBound.Metadata;

namespace ShkoOnline.EarlyBound.Completion;

/// <summary>
/// IntelliSense for late-bound Dataverse code, driven by the same YAML metadata as the generator:
/// table, column and relationship names in SDK calls, names and operators inside FetchXML strings,
/// and choice values where a number is expected.
/// </summary>
/// <remarks>
/// Ships inside the NuGet package and is loaded by Visual Studio, the VS Code C# extension and
/// Rider from the project's analyzer references; there is nothing to install in the IDE.
/// </remarks>
[ExportCompletionProvider(nameof(EarlyBoundCompletionProvider), LanguageNames.CSharp)]
[Shared]
public sealed class EarlyBoundCompletionProvider : CompletionProvider
{
    private static readonly string[] ChoiceHints = { "OptionSetValue", "Condition", ".Value", "AddAttributeValue" };

    public override bool ShouldTriggerCompletion(SourceText text, int caretPosition, CompletionTrigger trigger, OptionSet options)
    {
        if (trigger.Kind != CompletionTriggerKind.Insertion || caretPosition <= 0 || caretPosition > text.Length)
        {
            return false;
        }

        var typed = trigger.Character;
        switch (typed)
        {
            case '"':
            case '\'':
                // The opening quote of a name: entity[" or <entity name='
                return true;

            case ' ':
                // A member slot in an initializer, or a value slot.
                return IsAtStartOfInitializerMember(text, caretPosition - 1) || LineContainsAny(text, caretPosition, ChoiceHints);

            case '(':
            case ',':
            case '=':
                // A value slot, but only on lines that can hold a choice value.
                return LineContainsAny(text, caretPosition, ChoiceHints);

            default:
                if ((char.IsLetterOrDigit(typed) || typed == '_') && caretPosition >= 2)
                {
                    // The first character of a name, right after its opening quote.
                    var before = text[caretPosition - 2];
                    if (before == '"' || before == '\'')
                    {
                        return true;
                    }

                    // The first character of a member in an initializer: new Account { N
                    return !IsIdentifierCharacter(before) && IsAtStartOfInitializerMember(text, caretPosition - 1);
                }

                return false;
        }
    }

    public override async Task ProvideCompletionsAsync(CompletionContext context)
    {
        try
        {
            var document = context.Document;
            var cancellationToken = context.CancellationToken;
            if (document.Project.Language != LanguageNames.CSharp)
            {
                return;
            }

            var metadata = ProjectMetadata.Get(document.Project, cancellationToken);
            if (metadata == null)
            {
                return;
            }

            var root = await document.GetSyntaxRootAsync(cancellationToken).ConfigureAwait(false);
            if (root == null)
            {
                return;
            }

            var text = await document.GetTextAsync(cancellationToken).ConfigureAwait(false);
            var position = context.Position;
            var literal = StringLiteralContext.TryCreate(root, text, position);
            if (literal != null)
            {
                await CompleteInStringAsync(context, metadata, literal, text, position).ConfigureAwait(false);
            }
            else if (!IsInCommentOrString(root, position)
                && !await CompleteMembersAsync(context, metadata, root, position).ConfigureAwait(false)
                && !await CompleteTestEntityNamesAsync(context, metadata, root, position).ConfigureAwait(false))
            {
                await CompleteChoiceValueAsync(context, metadata, root, position).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception)
        {
            // A completion provider must never take IntelliSense down with it.
        }
    }

    private static async Task CompleteInStringAsync(CompletionContext context, ProjectMetadata metadata, StringLiteralContext literal, SourceText text, int position)
    {
        var cancellationToken = context.CancellationToken;
        var set = metadata.Set;

        if (FetchXmlAnalyzer.LooksLikeFetchXml(literal.TextBeforeCaret, literal.TextAfterCaret))
        {
            var fetch = FetchXmlAnalyzer.Analyze(literal.TextBeforeCaret, literal.TextAfterCaret);
            IEnumerable<CompletionItem>? items = null;
            switch (fetch.Kind)
            {
                case FetchXmlRequestKind.Entity:
                    items = CompletionItems.Entities(set);
                    break;
                case FetchXmlRequestKind.Attribute:
                    items = AttributeItems(set, fetch.EntityName);
                    break;
                case FetchXmlRequestKind.OptionValue:
                    var optionSet = FindOptionSet(set, fetch.EntityName, fetch.AttributeName);
                    if (optionSet != null)
                    {
                        items = CompletionItems.Options(optionSet, fetch.AttributeName!, insertion: null, shareList: false);
                    }

                    break;
                case FetchXmlRequestKind.Operator:
                    items = CompletionItems.Keywords(FetchXmlAnalyzer.Operators, "operator");
                    break;
                case FetchXmlRequestKind.LinkType:
                    items = CompletionItems.Keywords(FetchXmlAnalyzer.LinkTypes, "link type");
                    break;
                case FetchXmlRequestKind.FilterType:
                    items = CompletionItems.Keywords(FetchXmlAnalyzer.FilterTypes, "filter type");
                    break;
            }

            if (items != null)
            {
                context.CompletionListSpan = WordSpan(text, position, literal.Content, allowHyphenAndSpace: fetch.Kind >= FetchXmlRequestKind.Operator);
                context.AddItems(items);
            }

            // A FetchXML string is never also an SDK name.
            return;
        }

        if (literal.Expression is not LiteralExpressionSyntax)
        {
            return;
        }

        var model = await context.Document.GetSemanticModelAsync(cancellationToken).ConfigureAwait(false);
        if (model == null || !NameContextAnalyzer.TryAnalyze(literal.Expression, model, cancellationToken, out var request))
        {
            return;
        }

        IEnumerable<CompletionItem> names;
        switch (request.Role)
        {
            case NameRole.Entity:
                names = CompletionItems.Entities(set);
                break;
            case NameRole.Attribute:
                names = AttributeItems(set, request.EntityLogicalName);
                break;
            case NameRole.Relationship:
                names = CompletionItems.Relationships(set);
                break;
            default:
                return;
        }

        context.CompletionListSpan = WordSpan(text, position, literal.Content, allowHyphenAndSpace: false);
        context.AddItems(names);
    }

    /// <summary>
    /// With only used members generated, offers the others where a member is being written, so the
    /// whole table stays discoverable. Picking one puts its name in the code, and the generator adds it.
    /// </summary>
    private static async Task<bool> CompleteMembersAsync(CompletionContext context, ProjectMetadata metadata, SyntaxNode root, int position)
    {
        if (!metadata.GeneratesOnlyUsed || !MemberContextAnalyzer.MayBeMemberSlot(root, position))
        {
            return false;
        }

        var cancellationToken = context.CancellationToken;
        var model = await context.Document.GetSemanticModelAsync(cancellationToken).ConfigureAwait(false);
        var request = model == null ? null : MemberContextAnalyzer.Analyze(root, position, model, metadata, cancellationToken);
        if (request == null)
        {
            return false;
        }

        // The compiler's completion for an object initializer shows members and nothing else, and
        // says so by being exclusive. Entries that are to appear next to its own have to say the same.
        if (request.Kind == MemberContextKind.Initializer && (request.Attributes.Count > 0 || request.Relationships.Count > 0))
        {
            context.IsExclusive = true;
        }

        // The list's span is left alone: these entries join the compiler's own members.
        context.AddItems(CompletionItems.Members(request));
        return true;
    }

    /// <summary>
    /// After <c>new</c>, in a project whose table classes come from a reference: offers the classes
    /// derived from them (<c>AccountTestEntity</c>) that the code has not named yet.
    /// </summary>
    private static async Task<bool> CompleteTestEntityNamesAsync(CompletionContext context, ProjectMetadata metadata, SyntaxNode root, int position)
    {
        if (!metadata.GeneratesOnlyUsed || !MemberContextAnalyzer.IsAfterNew(root, position))
        {
            return false;
        }

        var cancellationToken = context.CancellationToken;
        var model = await context.Document.GetSemanticModelAsync(cancellationToken).ConfigureAwait(false);
        var candidates = model == null ? null : MemberContextAnalyzer.AnalyzeObjectCreation(root, position, model, metadata, cancellationToken);
        if (candidates == null)
        {
            return false;
        }

        context.AddItems(CompletionItems.TestEntities(candidates));
        return true;
    }

    private static async Task CompleteChoiceValueAsync(CompletionContext context, ProjectMetadata metadata, SyntaxNode root, int position)
    {
        var cancellationToken = context.CancellationToken;
        var model = await context.Document.GetSemanticModelAsync(cancellationToken).ConfigureAwait(false);
        if (model == null || !ChoiceContextAnalyzer.TryAnalyze(root, position, model, cancellationToken, out var request))
        {
            return;
        }

        var optionSet = FindOptionSet(metadata.Set, request.EntityLogicalName, request.AttributeLogicalName, out var owner);
        if (optionSet == null)
        {
            return;
        }

        // Prefer the generated enum over a magic number. When only used members are generated the
        // enum may not exist yet; writing its name is what brings it into being, so the class of
        // its table is enough to know the generator is at work here.
        Func<OptionDefinition, string?>? insertion = null;
        var ownerModel = owner == null ? null : metadata.Model.FindEntity(owner.LogicalName);
        var generated = ownerModel?.FindAttribute(request.AttributeLogicalName)?.OptionSet;
        if (generated != null)
        {
            var @namespace = metadata.Model.Namespace;
            var enumType = model.Compilation.GetTypeByMetadataName(@namespace + "." + generated.EnumName.TrimStart('@'));
            var anchor = enumType;
            if (anchor == null && metadata.GeneratesOnlyUsed)
            {
                anchor = model.Compilation.GetTypeByMetadataName(@namespace + "." + ownerModel!.ClassName.TrimStart('@'));
            }

            if (anchor != null && (enumType == null || enumType.TypeKind == TypeKind.Enum))
            {
                var inScope = model.LookupNamespacesAndTypes(position, name: anchor.Name)
                    .Any(symbol => SymbolEqualityComparer.Default.Equals(symbol, anchor));
                var typeName = inScope ? generated.EnumName : @namespace + "." + generated.EnumName;
                insertion = option =>
                {
                    var member = generated.FindOption(option.Value)?.MemberName;
                    return member != null && (enumType == null || enumType.GetMembers(member.TrimStart('@')).Length > 0)
                        ? "(int)" + typeName + "." + member
                        : null;
                };
            }
        }

        context.CompletionListSpan = request.ReplacementSpan;
        context.AddItems(CompletionItems.Options(optionSet, request.AttributeLogicalName, insertion, shareList: true));
    }

    private static IEnumerable<CompletionItem> AttributeItems(MetadataSet set, string? entityLogicalName)
    {
        var entity = entityLogicalName == null ? null : set.FindEntity(entityLogicalName);
        return entity != null ? CompletionItems.Attributes(entity) : CompletionItems.AllAttributes(set);
    }

    private static OptionSetDefinition? FindOptionSet(MetadataSet set, string? entityLogicalName, string? attributeLogicalName) =>
        FindOptionSet(set, entityLogicalName, attributeLogicalName, out _);

    private static OptionSetDefinition? FindOptionSet(MetadataSet set, string? entityLogicalName, string? attributeLogicalName, out EntityDefinition? owner)
    {
        owner = null;
        if (string.IsNullOrEmpty(attributeLogicalName))
        {
            return null;
        }

        if (entityLogicalName != null)
        {
            owner = set.FindEntity(entityLogicalName);
            var attribute = owner?.FindAttribute(attributeLogicalName!);
            return attribute == null ? null : set.ResolveOptionSet(attribute);
        }

        // The table is unknown: usable only when the column means the same choice wherever it exists.
        OptionSetDefinition? found = null;
        foreach (var entity in set.Entities)
        {
            var attribute = entity.FindAttribute(attributeLogicalName!);
            var optionSet = attribute == null ? null : set.ResolveOptionSet(attribute);
            if (optionSet == null)
            {
                continue;
            }

            if (found != null && !ReferenceEquals(found, optionSet))
            {
                owner = null;
                return null;
            }

            found = optionSet;
            owner = entity;
        }

        return found;
    }

    public override Task<CompletionDescription?> GetDescriptionAsync(Document document, CompletionItem item, CancellationToken cancellationToken)
    {
        if (!item.Properties.TryGetValue(CompletionItems.DescriptionKey, out var description))
        {
            return Task.FromResult<CompletionDescription?>(CompletionDescription.Empty);
        }

        var parts = ImmutableArray.CreateBuilder<TaggedText>();
        var first = true;
        foreach (var line in description.Split('\n'))
        {
            if (!first)
            {
                parts.Add(new TaggedText(TextTags.LineBreak, "\n"));
            }

            parts.Add(new TaggedText(TextTags.Text, line));
            first = false;
        }

        return Task.FromResult<CompletionDescription?>(CompletionDescription.Create(parts.ToImmutable()));
    }

    public override Task<CompletionChange> GetChangeAsync(Document document, CompletionItem item, char? commitKey, CancellationToken cancellationToken)
    {
        var insertion = item.Properties.TryGetValue(CompletionItems.InsertionKey, out var custom) ? custom : item.DisplayText;
        return Task.FromResult(CompletionChange.Create(new TextChange(item.Span, insertion)));
    }

    /// <summary>
    /// True when what precedes <paramref name="position"/>, whitespace aside, is the <c>{</c> or
    /// <c>,</c> that a member of an initializer follows. A look at the text only: whether it really
    /// is the initializer of a generated class is decided when completions are computed.
    /// </summary>
    /// <remarks>
    /// The compiler's completion for object initializers is exclusive, and only providers that
    /// report being triggered get to add to an exclusive list.
    /// </remarks>
    private static bool IsAtStartOfInitializerMember(SourceText text, int position)
    {
        var i = position - 1;
        while (i >= 0 && char.IsWhiteSpace(text[i]))
        {
            i--;
        }

        return i >= 0 && (text[i] == '{' || text[i] == ',');
    }

    private static bool IsIdentifierCharacter(char c) => char.IsLetterOrDigit(c) || c == '_';

    /// <summary>The run of name characters around the caret, limited to the literal's content.</summary>
    private static TextSpan WordSpan(SourceText text, int position, TextSpan content, bool allowHyphenAndSpace)
    {
        bool IsNameCharacter(char c) =>
            char.IsLetterOrDigit(c) || c == '_' || (allowHyphenAndSpace && (c == '-' || c == ' '));

        var start = position;
        while (start > content.Start && IsNameCharacter(text[start - 1]))
        {
            start--;
        }

        var end = position;
        while (end < content.End && IsNameCharacter(text[end]))
        {
            end++;
        }

        return TextSpan.FromBounds(start, end);
    }

    private static bool IsInCommentOrString(SyntaxNode root, int position)
    {
        var trivia = root.FindTrivia(position);
        if (trivia.IsKind(SyntaxKind.SingleLineCommentTrivia)
            || trivia.IsKind(SyntaxKind.MultiLineCommentTrivia)
            || trivia.IsKind(SyntaxKind.SingleLineDocumentationCommentTrivia)
            || trivia.IsKind(SyntaxKind.MultiLineDocumentationCommentTrivia))
        {
            return true;
        }

        var token = root.FindToken(position);
        return token.Span.Contains(position)
            && position > token.SpanStart
            && (token.IsKind(SyntaxKind.StringLiteralToken)
                || token.IsKind(SyntaxKind.CharacterLiteralToken)
                || token.IsKind(SyntaxKind.InterpolatedStringTextToken)
                || token.IsKind(SyntaxKind.SingleLineRawStringLiteralToken)
                || token.IsKind(SyntaxKind.MultiLineRawStringLiteralToken));
    }

    private static bool LineContainsAny(SourceText text, int caretPosition, string[] hints)
    {
        var line = text.Lines.GetLineFromPosition(Math.Min(caretPosition, text.Length));
        var before = text.ToString(TextSpan.FromBounds(line.Start, Math.Min(caretPosition, line.End)));
        foreach (var hint in hints)
        {
            if (before.IndexOf(hint, StringComparison.Ordinal) >= 0)
            {
                return true;
            }
        }

        return false;
    }
}
