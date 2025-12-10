# Laser Control Software

A Windows Forms application for laser cutter control, built with .NET 9.0.

## Features
- **Project Structure**: Layer-based object management.
- **Docked Interface**: Customizable layout with Tools, Layers, Object List, and Controls.
- **Tools**: Select, Move, Draw Box, Draw Line.
- **Image Support**: Import and display images for rasterization setup.
- **IO**: Save and Load projects (JSON format).

## Getting Started

### Prerequisites
- .NET 9.0 SDK
- Windows OS (for Windows Forms support)

### Building
Run the following command in the project root:
```bash
dotnet build
```

### Running
```bash
dotnet run
```

## Structure
- **Data/**: Core data models (`LaserObject`, `Layer`, `ProjectState`).
- **Controls/**: Custom UI controls (`WorkbenchControl`).
- **Tools/**: Interaction logic (`ToolManager`).
- **Forms**: Main application window.

## Usage
1.  **Select Tool**: Use top-left tools to draw or select.
2.  **Layers**: Switch active layer using the bottom color panel.
3.  **Pan/Zoom**: Right-click to pan, Scroll wheel to zoom.
4.  **Import**: File -> Import Image.
