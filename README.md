# Laser Control Software

A comprehensive Windows Forms application designed for controlling laser cutters, built with .NET 9.0. This tool provides a rich interface for designing, importing, and managing laser cutting jobs with advanced features like layer management, G-code generation, and hardware control.

## Key Features

### Object Management & Design
- **Advanced Transformations**: Move, resize, and rotate objects with precision. Supports robust negative scaling (flipping).
- **Group/Ungroup**: Organize complex designs by grouping multiple objects.
- **Grid Array**: Quickly replicate selected objects in a configurable Row × Column grid.
- **Creation Tools**: Draw Lines and Rectangles directly on the canvas.
- **Ruler Tool**: Measure distances on the workbench for precise alignment.
- **Snap-to-Grid**: Toggleable alignment aid with configurable grid size.

### Import & Processing
- **SVG Import**: High-fidelity vector import with configurable curve smoothness (flatness control) for perfect circles and ellipses.
- **Image Rasterization**: Import bitmaps (PNG, JPG, BMP) with advanced raster settings:
  - Configurable Line Interval (resolution).
  - Minimum Segment Length optimization.
  - Bicubic Resampling for high-quality scaling.
  - 1 bit dithering for lasers without PWM support.
  - **Smart Optimization**: Skips empty areas and handles transparency (laser off) automatically.
- **Layer System**: Color-coded layers to organize parts of your design.

### Machine Control & G-Code
- **G-Code Generation**: Built-in generator compatible with Grbl controllers.
- **Framing**: Trace the bounding box of your design with the laser (low power) to verify positioning before cutting.
- **Serial Connection**: Direct COM port streaming with connection status monitoring.
- **G-Code Preview**: Debug viewer to inspect the generated G-code before sending.

### Quality of Life
- **Robust Undo/Redo**: Full history support for all modification actions.
- **Interactive Workbench**: Smooth Pan (Right-click dragging) and Zoom (Scroll wheel) navigation. **View state (Zoom/Pan) is saved between sessions.**
- **Project Persistence**: Save and Load full project states via JSON.
- **Customizable Options**: Configure workspace dimensions, origin point, connection defaults, and UI preferences (e.g., Skip Splash Screen).

## Project Structure

The solution is organized into logical components:

### `Data/`
Core data models and logic:
- **Models**: `LaserObject`, `LaserGroup`, `Layer`, `ProjectState`.
- **Generators**: `GrblGenerator`, `Rasterizer` (Image to G-code logic).
- **Commands**: Implementation of the Command Pattern (`ICommand`, `CommandManager`) handling undo/redo operations.
- **IO**: `ProjectSerializer`, `SvgImporter`.
- **Hardware**: `SerialInterface`.
- **OpenGL**: Rendering context and resource management.

### `Controls/`
Custom User Interface controls:
- **WorkbenchControl**: The primary drawing surface handling user input, rendering, and tool interactions.

### `Forms/`
- **MainForm**: The primary application window.
- **OptionsForm**: Comprehensive settings dialog for Machine, Connection, Import, and View preferences.
- **DebugCodeForm**: Viewer for generated G-code.
- **GridArrayForm**: Dialog for parameterizing array creation.
- **SplashForm**: Laser simulation loading screen.

### `Tools/`
- **ToolManager**: Manages active tools (Select, DrawLine, DrawBox, Ruler) and their state.

## Getting Started

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

## Usage

1.  **Workbench Navigation**: 
    -   **Pan**: Right-click and drag.
    -   **Zoom**: Mouse scroll wheel.
2.  **Tools Panel** (Left):
    -   **Select**: Click to select, drag box to area select. Drag handles to resize.
    -   **Line/Box**: Draw primitives.
    -   **Ruler**: Measure distances.
3.  **Importing**: 
    -   Use `File -> Import` to load designs.
    -   Configure quality settings in `File -> Options -> Import` (e.g., SVG Curve Flatness).
4.  **Modification**:
    -   Use the **Control Panel** (Right) to precisely set X, Y, Width, and Height.
    -   **Group/Ungroup** and **Array** buttons help manage complex compositions.
5.  **Output**:
    -   **Framing**: Set Power/Speed and click "Frame Bounds" to preview the job area.
    -   **Generate G-Code**: Create and view the text file for the machine.
    -   **Connect**: Establish serial connection to stream the job.

## Known issues

-   **OpenGL**: The application may experience issues with OpenGL on some systems.
-   **G-code**: The G-code generator is custom and only verified on certain GRBL controllers.
-   **Text rendering**: The text rendering is not optimal and may not be correct.

## Architecture

-   **Command Pattern**: All state-modifying actions use `MoveCommand`, `GroupCommand`, `ResizeCommand`, etc., ensuring rock-solid Undo/Redo capability.
-   **Singleton State**: `ProjectState` and `AppConfiguration` provide centralized access to the application data and settings.
