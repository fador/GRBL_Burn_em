# GRBL Burn'Em Plugin API

GRBL Burn'Em provides a plugin system to extend the user interface, add custom G-Code generators, and manipulate laser objects programmatically.

## Getting Started

Create a new C# Class Library project targeting `.NET 9.0 Windows`.

### Project Configuration (`.csproj`)

Your project must reference the main `grbl_burn_em.dll` to access the interfaces.

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net9.0-windows10.0.19041.0</TargetFramework>
    <UseWindowsForms>true</UseWindowsForms>
    <Nullable>enable</Nullable>
  </PropertyGroup>

  <ItemGroup>
    <Reference Include="grbl_burn_em">
      <HintPath>path/to/grbl_burn_em.dll</HintPath>
      <Private>false</Private>
    </Reference>
  </ItemGroup>
</Project>
```

> [!IMPORTANT]
> Set `<Private>false</Private>` for the reference to avoid copying `grbl_burn_em.dll` into your plugin folder.

## Core Interfaces

### `IPlugin`

All plugins must implement the `IPlugin` interface.

```csharp
public interface IPlugin
{
    string Name { get; }
    string Version { get; }
    string Author { get; }
    void Initialize(IPluginHost host);
}
```

### `IPluginHost`

The `IPluginHost` is provided during initialization and provides access to application features.

- `void RegisterMenuItem(string menuPath, string menuItemName, Action action)`: Adds an item to the main menu. Use forward slashes for nesting (e.g., `Insert/Shapes`).
- `void RegisterContextMenuAction(string name, Action<LaserObject> action)`: Adds an action to the right-click menu of objects.
- `void RegisterGCodeGenerator(IGCodeGenerator generator)`: Registers a custom G-Code generator.
- `void AddObject(LaserObject obj)`: Adds a new `LaserObject` (e.g., `LaserPath`, `LaserRectangle`) to the project.
- `IEnumerable<LaserObject> GetSelectedObjects()`: Returns currently selected objects.
- `void RefreshUI()`: Forces a repaint of the workbench.

## Custom G-Code Generators

Implement `IGCodeGenerator` to add new ways to translate laser objects into G-Code.

```csharp
public interface IGCodeGenerator
{
    string Name { get; }
    IEnumerable<string> Generate(IEnumerable<LaserObject> objects);
}
```

Registered generators appear automatically in the **File > Options > Machine** tab under the **Generator** dropdown.

## Working with Objects

Plugins can create and manipulate various object types found in the `grbl_burn_em.Data` namespace:

- `LaserPath`: A collection of points representing lines/polygons.
- `LaserRectangle` / `LaserCircle`: Geometric primitives.
- `LaserText`: Vector text.
- `LaserImage`: Raster images for engraving.

### Example: Creating a Star

```csharp
private void CreateStar(IPluginHost host)
{
    var path = new LaserPath { Name = "Star" };
    // ... add points to path.Points ...
    path.UpdateBounds();
    host.AddObject(path);
}
```

## Deployment

1. Build your plugin project.
2. Copy the resulting `.dll` to the `Plugins` folder in the GRBL Burn'Em application directory.
3. Restart the application.

Refer to [StarPlugin.cs](StarPlugin/StarPlugin.cs) in the source tree for an example implementation.
