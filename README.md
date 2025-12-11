# Laser Control Software

A comprehensive Windows Forms application designed for controlling laser cutters, built with .NET 9.0. This tool provides a rich interface for designing, importing, and managing laser cutting jobs with advanced features like layer management, G-code generation, and hardware control.

## ✨ Key Features

- **Advanced Object Management**: Support for grouping, ungrouping, moving, and resizing objects.
- **Layer System**: Organizes objects into layers for easier management and processing order.
- **Robust Undo/Redo**: Full history support for all modification actions (Move, Resize, Group, Add).
- **Array Cloning**: Create grid layouts (Rows × Cols) with precise gap control.
- **Laser Framing**: Trace the job's bounding box with low-power laser to verify positioning.
- **SVG & Image Support**: Import and process SVG vectors and images with advanced rasterization options (Bicubic handling, segment optimization).
- **Hardware Control**: Direct serial communication interface for laser machines.
- **Save/Load**: Persist projects using JSON serialization.
- **Interactive Workbench**: Custom scalable and pan-able workspace (`WorkbenchControl`).

## 📁 Project Structure

The solution is organized into logical components:

### `Data/`
Core data models and logic:
- **Models**: `LaserObject`, `LaserGroup`, `Layer`, `ProjectState`.
- **Commands**: Implementation of the Command Pattern (`ICommand`, `CommandManager`) handling undo/redo operations.
- **IO**: `ProjectSerializer`, `SvgImporter`.
- **Hardware**: `SerialInterface`.

### `Controls/`
Custom User Interface controls:
- **WorkbenchControl**: The primary drawing surface handling user input, rendering, and tool interactions.

### `Forms/`
Secondary windows and dialogs:
- **OptionsForm**: Application settings and configuration.
- **MainForm**: The primary application window (located in root).

### `Tools/`
- **ToolManager**: Manages active tools (Select, Move, Draw, etc.) and their interactions with the workbench.

## 🚀 Getting Started

### Prerequisites
- **.NET 9.0 SDK**: Ensure you have the latest .NET 9.0 SDK installed.
- **Windows OS**: Required for Windows Forms compatibility.

### Building
Run the following command in the project root to build the solution:
```bash
dotnet build
```

### Running
Start the application with:
```bash
dotnet run
```

## 🎮 Usage

1.  **Workbench**: The main area where you design your job. Right-click to pan, scroll to zoom.
2.  **Importing**: Use `File -> Import` to load SVG vector graphics or images.
3.  **Manipulation**:
    -   **Select**: Click to select objects. Hold Shift to multi-select.
    -   **Move/Resize**: Drag handles to resize, drag body to move.
    -   **Group**: Select multiple objects and use the Group command to treat them as a single unit.
    -   **Array**: Create multiple copies of selected objects in a grid pattern.
4.  **Framing**: Use the side panel to set Framing Speed/Power and trace the work area bounds.
5.  **Layers**: Manage object visibility and processing order using the layer panel.
6.  **Serial Control**: Connect to your laser cutter via the Options/Settings menu to stream G-code.

## 🛠 Architecture

-   **Command Pattern**: All state-modifying actions are encapsulated as commands (`MoveCommand`, `GroupCommand`, etc.), allowing for a robust Undo/Redo stack managed by `CommandManager`.
-   **Composite Pattern**: `LaserGroup` and `LaserObject` allow for treating individual objects and groups of objects uniformly.
