using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.RegularExpressions;
using FluentAssertions;
using Xunit;

namespace SysBot.Tests;

public partial class McpControlContractTests
{
    private static readonly string[] ExpectedOperationIds =
    [
        "cancel_trade_operation",
        "create_trade_plan",
        "enqueue_trade_plan",
        "get_trade_operation",
        "get_trade_plan",
        "list_bot_instances",
        "list_trade_events",
        "pause_trade_operation",
        "resolve_trade_attention",
        "resume_trade_operation",
        "validate_trade_plan",
    ];

    [Fact]
    public void OpenApiContract_HasStableUniqueMcpToolNames()
    {
        using var contract = LoadContract();
        var operationIds = GetOperations(contract.RootElement)
            .Select(operation => operation.GetProperty("operationId").GetString())
            .OfType<string>()
            .ToArray();

        operationIds.Should().OnlyHaveUniqueItems();
        operationIds.Order().Should().Equal(ExpectedOperationIds);
        operationIds.Should().OnlyContain(name => McpToolName().IsMatch(name));
    }

    [Fact]
    public void DestructiveOperations_RequireExplicitConfirmation()
    {
        using var contract = LoadContract();

        AssertConfirmationSchema(GetRequestSchema(
            GetOperation(contract.RootElement, "cancel_trade_operation")));
        AssertConfirmationSchema(GetRequestSchema(
            GetOperation(contract.RootElement, "resolve_trade_attention")));
    }

    [Fact]
    public void ToolInputs_DoNotExposeRawPathsUrlsOrSecrets()
    {
        using var contract = LoadContract();
        var schemaPropertyNames = GetSchemaPropertyNames(
            contract.RootElement.GetProperty("components").GetProperty("schemas"));

        schemaPropertyNames.Should().NotContain(
            ["token", "secret", "file_path", "memory_address", "url"]);
    }

    [Fact]
    public void Contract_CoversAllSupportedSwitchModesAndAttentionState()
    {
        using var contract = LoadContract();
        var schemas = contract.RootElement
            .GetProperty("components")
            .GetProperty("schemas");

        GetEnumValues(schemas.GetProperty("ProgramMode"))
            .Should().Equal("swsh", "bdsp", "la", "sv", "lgpe", "lza");
        GetEnumValues(schemas.GetProperty("PlanState")).Should().Contain("needs_attention");
        GetEnumValues(schemas.GetProperty("ItemState")).Should().Contain("needs_attention");
    }

    [Fact]
    public void OwnerIdentity_IsDerivedOutsideToolInputs()
    {
        using var contract = LoadContract();
        var root = contract.RootElement;
        var tradePlanInputProperties = root
            .GetProperty("components")
            .GetProperty("schemas")
            .GetProperty("TradePlanInput")
            .GetProperty("properties");
        var validateProperties = GetRequestSchema(
                GetOperation(root, "validate_trade_plan"))
            .GetProperty("properties");

        tradePlanInputProperties.TryGetProperty("owner_id", out _).Should().BeFalse();
        validateProperties.TryGetProperty("owner_id", out _).Should().BeFalse();
        root.GetProperty("components")
            .GetProperty("schemas")
            .GetProperty("TradePlan")
            .GetProperty("properties")
            .TryGetProperty("owner_id", out _)
            .Should().BeTrue("persisted plans still retain their trusted owner");
    }

    private static JsonDocument LoadContract() =>
        JsonDocument.Parse(File.ReadAllText(GetContractPath()));

    private static string GetContractPath()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            var candidate = Path.Combine(directory.FullName, "contracts", "pokebot-control-v1.openapi.json");
            if (File.Exists(candidate))
                return candidate;
            directory = directory.Parent;
        }

        throw new FileNotFoundException("Could not locate contracts/pokebot-control-v1.openapi.json.");
    }

    private static IEnumerable<JsonElement> GetOperations(JsonElement root)
    {
        foreach (var path in root.GetProperty("paths").EnumerateObject())
        {
            foreach (var method in path.Value.EnumerateObject())
            {
                if (method.Value.ValueKind == JsonValueKind.Object &&
                    method.Value.TryGetProperty("operationId", out _))
                {
                    yield return method.Value;
                }
            }
        }
    }

    private static JsonElement GetOperation(JsonElement root, string operationId) =>
        GetOperations(root).Single(operation =>
            operation.GetProperty("operationId").GetString() == operationId);

    private static JsonElement GetRequestSchema(JsonElement operation) =>
        operation
            .GetProperty("requestBody")
            .GetProperty("content")
            .GetProperty("application/json")
            .GetProperty("schema");

    private static string[] GetEnumValues(JsonElement schema) =>
        schema.GetProperty("enum")
            .EnumerateArray()
            .Select(value => value.GetString())
            .OfType<string>()
            .ToArray();

    private static string[] GetSchemaPropertyNames(JsonElement element)
    {
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        AddSchemaPropertyNames(element, names);
        return [.. names];
    }

    private static void AddSchemaPropertyNames(JsonElement element, HashSet<string> names)
    {
        if (element.ValueKind == JsonValueKind.Object)
        {
            foreach (var property in element.EnumerateObject())
            {
                if (property.NameEquals("properties") && property.Value.ValueKind == JsonValueKind.Object)
                {
                    foreach (var schemaProperty in property.Value.EnumerateObject())
                        names.Add(schemaProperty.Name);
                }

                AddSchemaPropertyNames(property.Value, names);
            }
        }
        else if (element.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in element.EnumerateArray())
                AddSchemaPropertyNames(item, names);
        }
    }

    private static void AssertConfirmationSchema(JsonElement schema)
    {
        schema.GetProperty("required")
            .EnumerateArray()
            .Select(value => value.GetString())
            .Should().Contain("confirm");

        var confirm = schema.GetProperty("properties").GetProperty("confirm");
        confirm.GetProperty("type").GetString().Should().Be("boolean");
        confirm.GetProperty("const").GetBoolean().Should().BeTrue();
    }

    [GeneratedRegex("^[a-z0-9_]{3,64}$")]
    private static partial Regex McpToolName();
}
