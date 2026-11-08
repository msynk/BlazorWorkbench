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
| Types with fixtures | Select of the matching fixtures. |
| Types with a custom editor | Your component. |
| Everything else | Shown as unsupported, with the reason and the scenario value. |

Every row shows the **source** of its value: *Scenario*, *Default* (component default), *Override*, *Fixture* or *Logged*. The *null* and *unset* buttons keep three cases apart: an explicit `null`, a parameter that is not supplied (component default), and a value. Invalid input is rejected with a message and is never applied. Unsetting a parameter remounts the component, because Blazor does not reset parameters that are removed.

Edits are transient. They live in the URL (`?args=`), so you can share a link, until you export them from **Code → Save as scenario**.

Workbench does not serialize arbitrary live .NET objects. Complex values come from fixtures or factories.

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
