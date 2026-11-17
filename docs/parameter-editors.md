# Parameter inspector and custom editors

The **Controls** panel generates an editor for each component parameter.

| Parameter type | Editor |
| --- | --- |
| `bool` | Checkbox (a null/true/false select for `bool?`). |
| `string`, `char`, `Uri` | Text input. |
| Integer types | Text input with numeric validation (invariant culture, range checked). |
| `float`, `double`, `decimal` | Text input with decimal validation. |
| `enum` | Select. `[Flags]` enums get a checkbox list. |
| `DateTime`, `DateTimeOffset`, `DateOnly`, `TimeOnly` | Native date and time inputs. |
| `TimeSpan`, `Guid` | Text input with format validation. |
| `RenderFragment` (incl. `ChildContent`) | Multi-line text, rendered as text content. |
| `EventCallback` | Read-only: *Logged to Actions (n calls)*. |
| Records, classes and structs of plain data; arrays, lists, sets and string-keyed dictionaries of them | A JSON text area, checked against the type. |
| Types with fixtures | Select of the matching fixtures (plus **Edit as JSON** for types that can be edited as JSON). |
| Types with a custom editor | Your component. |
| Everything else | Shown as unsupported, with the reason and the scenario value. |

### Shaping the editors with standard attributes

The standard .NET attributes on a parameter change its editor, so no Workbench-specific code is needed:

| Attribute | Effect in the Controls panel |
| --- | --- |
| `[Range(0, 100)]` | A slider next to the number box (values outside the range can still be typed, to preview them) |
| `[Display(Name = "Volume level")]`, `[DisplayName]` | The label (the parameter name is shown next to it) |
| `[Display(GroupName = "Sound")]`, `[Category("Sound")]` | A section; sections appear in the order of their first parameter |
| `[Display(Order = 2)]` | The position within the panel or section |
| `[Display(Description = "...")]`, `[Description]` | The help text, when the parameter has no XML documentation |
| `[DataType(DataType.MultilineText)]` | A text area for a `string` |
| `[DataType("Color")]` | A color picker next to the text box for a `string` |
| `[StringLength(140)]`, `[MaxLength(140)]` | A length limit on the text box |
| `[EditorBrowsable(EditorBrowsableState.Never)]`, `[Browsable(false)]` | Hidden behind *Show hidden parameters*, unless the scenario or the URL sets it |

For `DateTimeOffset` parameters the date and time picker keeps the value's UTC offset, shown next to it, so editing never
moves the value to the server's time zone.

Every row shows the **source** of its value: *Scenario*, *Default* (component default), *Override*, *Fixture* or *Logged*. The *null* and *unset* buttons keep three cases apart: an explicit `null`, a parameter that is not supplied (component default), and a value. Invalid input is rejected with a message and is never applied. Unsetting a parameter remounts the component, because Blazor does not reset parameters that are removed.

Edits are transient. They live in the URL (`?args=`), so you can share a link, until you export them from **Code → Save as scenario**.

Complex values are edited as JSON when every public property is itself editable (a few levels deep): records such as
`Customer(string Name, Address Home, IReadOnlyList<string> Tags)`, settings classes, `List<Person>` or
`Dictionary<string, int>`. Only the parameter's declared type is created: System.Text.Json does not read type names from
the JSON, so input cannot choose what gets instantiated. Delegates, components, interfaces other than collections, abstract
types, `object`, framework types, disposable types and recursive types are not editable; they come from fixtures or
factories. Values up to 64 KB are accepted. **Code → Save as scenario** exports a JSON-edited value as
`JsonSerializer.Deserialize<T>("""...""", JsonSerializerOptions.Web)!`.

## Custom editors

A custom editor is a component that derives from `ParameterEditorBase<T>`:

```razor
@inherits ParameterEditorBase<HexColor?>

<input id="@($"wb-param-{Parameter.Name}")" type="color" value="@(Value?.Value ?? "#000000")" @onchange="OnChange" />

@code {
    private Task OnChange(ChangeEventArgs e) => SetValueAsync(new HexColor(e.Value?.ToString() ?? "#000000"));
}
```

Register it in the host:

```csharp
builder.AddBlazorWorkbench(o => o.AddParameterEditor<HexColor, HexColorEditor>());
```

- A custom editor takes precedence over the built-in editor for its type.
- Values must survive a JSON round trip with System.Text.Json, because they travel to the preview document. Workbench deserializes them with the same options.
- Editors are not discovered as previewable components.
- Workbench Core needs no change. The editor lives in your host project.

The demo's [HexColorEditor](../demo/BlazorWorkbench.Demo/Editors/HexColorEditor.razor) is a complete example.
