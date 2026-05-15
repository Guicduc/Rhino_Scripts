# AGENTS.md

## Grasshopper components folder

Use this folder as the default installation path for compiled Grasshopper components:

```text
C:\Program Files\Rhino 7\Plug-ins\Grasshopper\Components
```

When a `.gha` component is created or rebuilt in this repository, copy it to that folder and restart Rhino/Grasshopper so the component is loaded.

## Custom Grasshopper tab

Do not place new local components inside Grasshopper's default tabs such as `Curve`, `Maths`, `Vector`, or `Surface`.

To make a component appear in a custom tab, set the last two arguments of the `GH_Component` base constructor:

```csharp
public MyComponent()
    : base(
        "Component Name",
        "Nickname",
        "Component description.",
        "Custom",
        "Geometry")
{
}
```

The fourth argument is the Grasshopper tab/category. Use `Custom` for this repository's local tools.
The fifth argument is the panel/subcategory inside that tab, for example `Geometry` or `Analysis`.

Also set component exposure so the component appears directly in the Grasshopper panel:

```csharp
public override GH_Exposure Exposure
{
    get { return GH_Exposure.primary; }
}
```

After changing category or subcategory, rebuild the `.gha`, copy it to:

```text
C:\Program Files\Rhino 7\Plug-ins\Grasshopper\Components
```

Then restart Rhino/Grasshopper and search by component nickname.

## Created scripts and components

- `CenterRectangleComponent.cs` / `CenterRectangle.gha`: Grasshopper component named `Center Rectangle` (`CenterRect`) that creates a rectangle centered on a point from X and Y dimensions. Category: `Custom > Geometry`.
- `VerticalCurveComponent.cs` / `VerticalCurve.gha`: Grasshopper component named `Curve Is Vertical` (`IsVertical`) that identifies whether curves are vertical in World Z using an X/Y drift tolerance. Category: `Custom > Analysis`.
