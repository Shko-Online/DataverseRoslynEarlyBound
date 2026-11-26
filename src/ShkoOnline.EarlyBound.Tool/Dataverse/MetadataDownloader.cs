using System.Collections.Concurrent;
using System.Text.Json;
using System.Text.RegularExpressions;
using ShkoOnline.EarlyBound.Configuration;
using ShkoOnline.EarlyBound.Metadata;

namespace ShkoOnline.EarlyBound.Tool.Dataverse;

/// <summary>A table as listed by the environment, before its details are downloaded.</summary>
internal sealed record EntitySummary(string LogicalName, string SchemaName, string? DisplayName, Guid MetadataId, bool IsCustom, bool IsIntersect);

internal sealed record SolutionSummary(string UniqueName, string? FriendlyName, string? Version, bool IsManaged);

/// <summary>What a pull produced.</summary>
internal sealed class PullResult
{
    public List<EntityDefinition> Entities { get; } = new();

    public List<OptionSetDefinition> GlobalOptionSets { get; } = new();

    public int LanguageCode { get; set; }

    /// <summary>Things the user asked for that could not be honoured.</summary>
    public List<string> Warnings { get; } = new();

    /// <summary>Things done on the user's behalf that they did not ask for.</summary>
    public List<string> Notes { get; } = new();
}

/// <summary>Downloads table metadata from a Dataverse environment through the Web API.</summary>
internal sealed class MetadataDownloader
{
    private const int MaxConcurrentRequests = 8;

    private const string EntitySelect =
        "LogicalName,SchemaName,DisplayName,DisplayCollectionName,Description,EntitySetName,LogicalCollectionName," +
        "PrimaryIdAttribute,PrimaryNameAttribute,PrimaryImageAttribute,ObjectTypeCode,OwnershipType," +
        "IsCustomEntity,IsActivity,IsIntersect";

    private const string RelationshipExpand =
        ",ManyToOneRelationships($select=SchemaName,ReferencedEntity,ReferencingAttribute)" +
        ",OneToManyRelationships($select=SchemaName,ReferencingEntity,ReferencingAttribute)" +
        ",ManyToManyRelationships($select=SchemaName,Entity1LogicalName,Entity2LogicalName,IntersectEntityName)";

    /// <summary>Column type → the Web API type whose OptionSet has to be expanded separately.</summary>
    private static readonly Dictionary<string, string> ChoiceCasts = new(StringComparer.Ordinal)
    {
        [AttributeTypes.Picklist] = "PicklistAttributeMetadata",
        [AttributeTypes.State] = "StateAttributeMetadata",
        [AttributeTypes.Status] = "StatusAttributeMetadata",
        [AttributeTypes.MultiSelectPicklist] = "MultiSelectPicklistAttributeMetadata",
    };

    private readonly IDataverseApi _api;
    private readonly SemaphoreSlim _throttle = new(MaxConcurrentRequests);

    public MetadataDownloader(IDataverseApi api)
    {
        _api = api;
    }

    /// <summary>The environment's base language, which labels default to.</summary>
    public async Task<int> GetBaseLanguageAsync(CancellationToken cancellationToken)
    {
        using var document = await GetAsync("organizations?$select=languagecode", cancellationToken).ConfigureAwait(false);
        foreach (var organization in MetadataMapper.Array(document.RootElement, "value"))
        {
            if (MetadataMapper.Int(organization, "languagecode") is { } languageCode)
            {
                return languageCode;
            }
        }

        return 1033;
    }

    public async Task<List<EntitySummary>> ListEntitiesAsync(int languageCode, CancellationToken cancellationToken)
    {
        using var document = await GetAsync(
            "EntityDefinitions?$select=LogicalName,SchemaName,DisplayName,MetadataId,IsCustomEntity,IsIntersect", cancellationToken).ConfigureAwait(false);
        var result = new List<EntitySummary>();
        foreach (var entity in MetadataMapper.Array(document.RootElement, "value"))
        {
            var logicalName = MetadataMapper.String(entity, "LogicalName");
            if (string.IsNullOrEmpty(logicalName))
            {
                continue;
            }

            Guid.TryParse(MetadataMapper.String(entity, "MetadataId"), out var metadataId);
            result.Add(new EntitySummary(
                logicalName!,
                MetadataMapper.String(entity, "SchemaName") ?? logicalName!,
                MetadataMapper.Label(entity, "DisplayName", languageCode),
                metadataId,
                MetadataMapper.Bool(entity, "IsCustomEntity") ?? false,
                MetadataMapper.Bool(entity, "IsIntersect") ?? false));
        }

        result.Sort((x, y) => string.CompareOrdinal(x.LogicalName, y.LogicalName));
        return result;
    }

    public async Task<List<SolutionSummary>> ListSolutionsAsync(CancellationToken cancellationToken)
    {
        var result = new List<SolutionSummary>();
        await foreach (var solution in GetAllAsync(
            "solutions?$select=uniquename,friendlyname,version,ismanaged&$filter=isvisible eq true&$orderby=uniquename", cancellationToken).ConfigureAwait(false))
        {
            var uniqueName = MetadataMapper.String(solution, "uniquename");
            if (!string.IsNullOrEmpty(uniqueName))
            {
                result.Add(new SolutionSummary(
                    uniqueName!,
                    MetadataMapper.String(solution, "friendlyname"),
                    MetadataMapper.String(solution, "version"),
                    MetadataMapper.Bool(solution, "ismanaged") ?? false));
            }
        }

        return result;
    }

    /// <summary>Downloads everything <paramref name="settings"/> asks for.</summary>
    /// <param name="onProgress">Called with (done, total, table) as tables finish downloading.</param>
    public async Task<PullResult> PullAsync(PullSettings settings, Action<int, int, string>? onProgress, CancellationToken cancellationToken)
    {
        if (settings.Entities.Count == 0 && settings.Solutions.Count == 0)
        {
            throw new InvalidOperationException(
                "Nothing to pull. List tables under 'pull.entities' or solutions under 'pull.solutions' in earlybound.yaml, or pass --entities / --solutions.");
        }

        var result = new PullResult
        {
            LanguageCode = settings.LanguageCode ?? await GetBaseLanguageAsync(cancellationToken).ConfigureAwait(false),
        };

        var index = await ListEntitiesAsync(result.LanguageCode, cancellationToken).ConfigureAwait(false);
        var fromSolutions = new HashSet<string>(StringComparer.Ordinal);
        foreach (var solution in settings.Solutions)
        {
            var found = await GetSolutionEntitiesAsync(solution, index, cancellationToken).ConfigureAwait(false);
            if (found == null)
            {
                result.Warnings.Add($"Solution '{solution}' does not exist in this environment. Use its unique name, not its display name.");
                continue;
            }

            if (found.Count == 0)
            {
                result.Warnings.Add($"Solution '{solution}' contains no tables.");
            }

            fromSolutions.UnionWith(found);
        }

        var selected = EntitySelector.Select(index.Select(e => e.LogicalName), settings.Entities, fromSolutions, settings.Exclude, out var unmatched);
        foreach (var pattern in unmatched)
        {
            result.Warnings.Add($"No table matches '{pattern}'.");
        }

        var includeRelationships = !string.Equals(settings.Relationships, PullSettings.RelationshipsNone, StringComparison.OrdinalIgnoreCase);
        var downloaded = await DownloadAsync(selected, includeRelationships, result, onProgress, cancellationToken).ConfigureAwait(false);

        // Party lists (To, From, Cc...) are collections of activityparty rows; without that table they stay untyped.
        if (!selected.Contains("activityparty")
            && !settings.Exclude.Any(pattern => EntitySelector.Matcher(pattern)("activityparty"))
            && index.Any(e => e.LogicalName == "activityparty")
            && downloaded.Any(e => e.Attributes.Any(a => a.Type == AttributeTypes.PartyList)))
        {
            selected.Add("activityparty");
            downloaded.AddRange(await DownloadAsync(new[] { "activityparty" }, includeRelationships, result, null, cancellationToken).ConfigureAwait(false));
            result.Notes.Add("Added 'activityparty' because a pulled table has party-list columns. List it under pull.exclude to leave it out.");
        }

        if (string.Equals(settings.Relationships, PullSettings.RelationshipsIncluded, StringComparison.OrdinalIgnoreCase))
        {
            foreach (var entity in downloaded)
            {
                entity.ManyToOne.RemoveAll(r => !selected.Contains(r.Entity));
                entity.OneToMany.RemoveAll(r => !selected.Contains(r.Entity));
                entity.ManyToMany.RemoveAll(r => !selected.Contains(r.Entity));
            }
        }

        result.Entities.AddRange(downloaded.OrderBy(e => e.LogicalName, StringComparer.Ordinal));
        result.GlobalOptionSets.Sort((x, y) => string.CompareOrdinal(x.Name, y.Name));
        return result;
    }

    private async Task<List<EntityDefinition>> DownloadAsync(
        ICollection<string> logicalNames,
        bool includeRelationships,
        PullResult result,
        Action<int, int, string>? onProgress,
        CancellationToken cancellationToken)
    {
        var globalOptionSets = new ConcurrentDictionary<string, OptionSetDefinition>(StringComparer.Ordinal);
        var done = 0;
        var total = logicalNames.Count;

        var tasks = logicalNames.Select(async logicalName =>
        {
            var entity = await DownloadEntityAsync(logicalName, includeRelationships, result.LanguageCode, globalOptionSets, cancellationToken).ConfigureAwait(false);
            onProgress?.Invoke(Interlocked.Increment(ref done), total, logicalName);
            return entity;
        }).ToList();

        var entities = (await Task.WhenAll(tasks).ConfigureAwait(false)).ToList();
        foreach (var optionSet in globalOptionSets.Values)
        {
            if (result.GlobalOptionSets.All(o => o.Name != optionSet.Name))
            {
                result.GlobalOptionSets.Add(optionSet);
            }
        }

        return entities;
    }

    private async Task<EntityDefinition> DownloadEntityAsync(
        string logicalName,
        bool includeRelationships,
        int languageCode,
        ConcurrentDictionary<string, OptionSetDefinition> globalOptionSets,
        CancellationToken cancellationToken)
    {
        var key = $"EntityDefinitions(LogicalName='{EscapeLiteral(logicalName)}')";
        using var entityDocument = await GetAsync(
            $"{key}?$select={EntitySelect}&$expand=Attributes{(includeRelationships ? RelationshipExpand : string.Empty)}", cancellationToken).ConfigureAwait(false);

        // Option sets are navigation properties of the specific column types, so each type present is asked for once.
        var casts = MetadataMapper.Array(entityDocument.RootElement, "Attributes")
            .Select(MetadataMapper.AttributeType)
            .Where(ChoiceCasts.ContainsKey)
            .Distinct(StringComparer.Ordinal)
            .Select(type => ChoiceCasts[type])
            .ToList();

        var castDocuments = await Task.WhenAll(casts.Select(cast =>
            GetAsync($"{key}/Attributes/Microsoft.Dynamics.CRM.{cast}?$select=LogicalName&$expand=OptionSet", cancellationToken))).ConfigureAwait(false);
        try
        {
            var optionSets = new Dictionary<string, JsonElement>(StringComparer.Ordinal);
            foreach (var document in castDocuments)
            {
                foreach (var attribute in MetadataMapper.Array(document.RootElement, "value"))
                {
                    var attributeName = MetadataMapper.String(attribute, "LogicalName");
                    if (attributeName == null
                        || !attribute.TryGetProperty("OptionSet", out var optionSet)
                        || optionSet.ValueKind != JsonValueKind.Object)
                    {
                        continue;
                    }

                    optionSets[attributeName] = optionSet;
                    if (MetadataMapper.Bool(optionSet, "IsGlobal") == true
                        && MetadataMapper.String(optionSet, "Name") is { Length: > 0 } name)
                    {
                        globalOptionSets.GetOrAdd(name, _ => MetadataMapper.MapOptionSet(optionSet, languageCode));
                    }
                }
            }

            return MetadataMapper.MapEntity(entityDocument.RootElement, optionSets, languageCode);
        }
        finally
        {
            foreach (var document in castDocuments)
            {
                document.Dispose();
            }
        }
    }

    /// <summary>Logical names of the tables in a solution, or null when the solution does not exist.</summary>
    private async Task<HashSet<string>?> GetSolutionEntitiesAsync(string uniqueName, List<EntitySummary> index, CancellationToken cancellationToken)
    {
        string? solutionId = null;
        using (var solutions = await GetAsync(
            $"solutions?$select=solutionid&$filter=uniquename eq '{EscapeLiteral(uniqueName)}'", cancellationToken).ConfigureAwait(false))
        {
            foreach (var solution in MetadataMapper.Array(solutions.RootElement, "value"))
            {
                solutionId = MetadataMapper.String(solution, "solutionid");
            }
        }

        if (!Guid.TryParse(solutionId, out var id))
        {
            return null;
        }

        var byMetadataId = index.ToDictionary(e => e.MetadataId, e => e.LogicalName);
        var result = new HashSet<string>(StringComparer.Ordinal);

        // Component type 1 is a table.
        await foreach (var component in GetAllAsync(
            $"solutioncomponents?$select=objectid&$filter=componenttype eq 1 and _solutionid_value eq {id:D}", cancellationToken).ConfigureAwait(false))
        {
            if (Guid.TryParse(MetadataMapper.String(component, "objectid"), out var objectId)
                && byMetadataId.TryGetValue(objectId, out var logicalName))
            {
                result.Add(logicalName);
            }
        }

        return result;
    }

    private async Task<JsonDocument> GetAsync(string url, CancellationToken cancellationToken)
    {
        await _throttle.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            return await _api.GetAsync(url, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _throttle.Release();
        }
    }

    /// <summary>Reads every page of a collection.</summary>
    private async IAsyncEnumerable<JsonElement> GetAllAsync(string url, [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken)
    {
        string? next = url;
        while (next != null)
        {
            using var document = await GetAsync(next, cancellationToken).ConfigureAwait(false);
            foreach (var item in MetadataMapper.Array(document.RootElement, "value"))
            {
                yield return item.Clone();
            }

            next = MetadataMapper.String(document.RootElement, "@odata.nextLink");
        }
    }

    private static string EscapeLiteral(string value) => Uri.EscapeDataString(value.Replace("'", "''"));
}

/// <summary>Applies the include and exclude patterns of a pull to the tables of an environment.</summary>
internal static class EntitySelector
{
    /// <param name="unmatched">Include patterns that matched no table.</param>
    public static SortedSet<string> Select(
        IEnumerable<string> allLogicalNames,
        IEnumerable<string> include,
        IEnumerable<string> fromSolutions,
        IEnumerable<string> exclude,
        out List<string> unmatched)
    {
        var all = allLogicalNames.ToList();
        var selected = new SortedSet<string>(StringComparer.Ordinal);
        unmatched = new List<string>();

        foreach (var pattern in include)
        {
            var matcher = Matcher(pattern);
            var any = false;
            foreach (var name in all)
            {
                if (matcher(name))
                {
                    selected.Add(name);
                    any = true;
                }
            }

            if (!any)
            {
                unmatched.Add(pattern);
            }
        }

        selected.UnionWith(fromSolutions);

        foreach (var pattern in exclude)
        {
            var matcher = Matcher(pattern);
            selected.RemoveWhere(name => matcher(name));
        }

        return selected;
    }

    /// <summary>A case-insensitive match where <c>*</c> stands for any run of characters.</summary>
    internal static Func<string, bool> Matcher(string pattern)
    {
        pattern = pattern.Trim();
        if (pattern.IndexOf('*') < 0)
        {
            return name => string.Equals(name, pattern, StringComparison.OrdinalIgnoreCase);
        }

        var regex = new Regex(
            "^" + Regex.Escape(pattern).Replace("\\*", ".*") + "$",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        return regex.IsMatch;
    }
}
