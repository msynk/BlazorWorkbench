using System.Text.Json;
using BlazorWorkbench.Values;
using Microsoft.AspNetCore.Components;

namespace BlazorWorkbench.Core.Tests;

public class JsonEditingTests
{
    public sealed record Address(string Street, string City);

    public sealed record Customer(string Name, int Age, Address Home, IReadOnlyList<string> Tags);

    public sealed class Settings
    {
        public bool Compact { get; set; }
        public Dictionary<string, int> Limits { get; set; } = [];
    }

    public sealed class WithDelegate
    {
        public Action? OnChange { get; set; }
    }

    public sealed class Node
    {
        public Node? Next { get; set; }
    }

    [Theory]
    [InlineData(typeof(List<int>), true)]
    [InlineData(typeof(string[]), true)]
    [InlineData(typeof(IReadOnlyList<Address>), true)]
    [InlineData(typeof(Dictionary<string, int>), true)]
    [InlineData(typeof(Customer), true)]
    [InlineData(typeof(Settings), true)]
    [InlineData(typeof(Dictionary<int, string>), false)]   // JSON object keys are strings
    [InlineData(typeof(object), false)]
    [InlineData(typeof(IComponent), false)]
    [InlineData(typeof(WithDelegate), false)]
    [InlineData(typeof(Node), false)]                      // recursive types are not edited as JSON
    [InlineData(typeof(HttpClient), false)]
    [InlineData(typeof(RenderFragment), false)]
    public void Only_plain_data_types_are_json_editable(Type type, bool expected) =>
        Assert.Equal(expected, ParameterValueConverter.IsJsonEditable(type));

    [Fact]
    public void Records_round_trip_through_json()
    {
        var customer = new Customer("Ada", 36, new Address("1 Loop", "London"), ["vip"]);
        Assert.True(ParameterValueConverter.TrySerialize(customer, out var json));
        Assert.True(ParameterValueConverter.TryConvert(json, typeof(Customer), out var back, out var error), error);
        Assert.Equal("London", ((Customer)back!).Home.City);

        // The inspector sends typed text as a JSON string; it is parsed as JSON for the declared type.
        var typed = JsonSerializer.SerializeToElement("""{"name":"Bob","age":4,"home":{"street":"x","city":"Paris"},"tags":[]}""");
        Assert.True(ParameterValueConverter.TryConvert(typed, typeof(Customer), out var parsed, out _));
        Assert.Equal("Paris", ((Customer)parsed!).Home.City);
    }

    [Fact]
    public void Invalid_or_oversized_json_is_rejected_with_a_message()
    {
        var invalid = JsonSerializer.SerializeToElement("{ not json");
        Assert.False(ParameterValueConverter.TryConvert(invalid, typeof(Settings), out _, out var error));
        Assert.StartsWith("Not valid JSON for", error, StringComparison.Ordinal);

        var huge = JsonSerializer.SerializeToElement("[" + string.Join(',', Enumerable.Repeat("1", ParameterValueConverter.MaxJsonLength)) + "]");
        Assert.False(ParameterValueConverter.TryConvert(huge, typeof(List<int>), out _, out var tooLong));
        Assert.Contains("longer than", tooLong, StringComparison.Ordinal);
        Assert.False(ParameterValueConverter.TrySerialize(Enumerable.Range(0, ParameterValueConverter.MaxJsonLength).ToList(), out _));
    }

    [Fact]
    public void Json_values_export_as_csharp_that_recreates_them()
    {
        var code = BlazorWorkbench.Documentation.ScenarioExporter.CSharpValue(new Address("1 Loop", "London"), typeof(Address));
        Assert.StartsWith("System.Text.Json.JsonSerializer.Deserialize<", code, StringComparison.Ordinal);
        Assert.Contains("\"\"\"{\"street\":\"1 Loop\",\"city\":\"London\"}\"\"\"", code, StringComparison.Ordinal);
        Assert.EndsWith("System.Text.Json.JsonSerializerOptions.Web)!", code, StringComparison.Ordinal);
    }
}
