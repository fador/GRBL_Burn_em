# Camera Calibration

GRBL Burn'Em supports two camera setups: **stationary** (fixed above the work area) and **head-mounted** (attached to the laser head). Camera calibration uses a printed [ChArUco board](https://docs.opencv.org/4.x/df/d4a/tutorial_charuco_detection.html) and the [Emgu.CV](https://www.emgu.com/) library (OpenCV for .NET).

## Quick Start (with Emulator)

1. Build and run the **emulator**: `dotnet run --project Emulator` (or run `Emulator\bin\...\grbl_burn_em_emulator.exe`)
2. In the emulator: check **"Draw ChArUco board on bed"**, set **Board Y = 0**, **Board size = 120** mm
3. In the emulator: set **Cam Offset X = 50**, **Cam Offset Y = 0**, **FOV Width = 120**, **FOV Height = 90**
4. In the main app: open **Tool > Camera Settings**
5. Click **Connect Emulator** (yellow button)
6. **Start Camera**, select "Network Camera (Emulator)"
7. Follow the calibration steps below

The emulator's **Board X/Y** is the **board origin** (outer top-left corner of the
board): the board extends from (Board X, Board Y) toward +X/+Y, matching the
registration convention. The board is drawn with its axes aligned to the machine axes.

## Workflow Overview

```
┌──────────────────┐
│ 1. Board Setup   │  Configure ChArUco board parameters (once)
└────────┬─────────┘
         ▼
┌──────────────────┐
│ 2. Lens Calib.   │  Compute camera intrinsics + distortion
└────────┬─────────┘
         ▼
     ┌───┴───┐
     │       │
     ▼       ▼
┌─────────┐ ┌──────────────┐
│Stationary│ │Head-Mounted  │
│Registrat.│ │Offset Calib. │
└────┬────┘ └──────┬───────┘
     │             │
     ▼             ▼
┌─────────┐ ┌──────────────┐
│  Live   │ │ Workspace    │
│ Overlay │ │   Scan       │
└─────────┘ └──────────────┘
```

## 1. ChArUco Board Setup

**Menu:** Camera Settings > Calibrate Alignment > **ChArUco Board Setup...**

| Parameter | Default | Description |
|-----------|---------|-------------|
| Dictionary | DICT_4X4_50 | ArUco marker dictionary |
| Squares X/Y | 5 / 7 | Chessboard squares |
| Square Length | 20 mm | Physical chess square side |
| Marker Length | 15 mm | Physical ArUco marker side |

- Click **Preview** to see the board pattern
- Click **Save as PNG** to export a printable board image (300 DPI)
- Print the board at 100% scale (check dimensions with a ruler)
- Mount on a flat, rigid surface

The board parameters are saved to `calibration.json`.

## 2. Lens Calibration (Intrinsics)

**Menu:** Camera Settings > Calibrate Alignment > **Lens Calibration (ChArUco)...**

Computes intrinsic camera parameters (focal length, principal point, distortion).

### Manual Capture
1. Hold the ChArUco board at different angles/distances in view of the camera
2. Click **Capture View** for each position
3. Aim for 5-15 views with the board clearly visible

### Auto Capture
1. Place the board on the work area
2. Position the machine so the board is visible in the camera
3. Configure grid: **Rows**, **Columns**, **Step (mm)**
4. Click **Auto Capture** — the machine moves through grid positions, captures frames where the board is detected

Grid indicators: ✓ (board detected), ✗ (skipped)

### Calibration
- Click **Calibrate** (requires 6+ captured views)
- Review results: RMSE (reprojection error), fx/fy/cx/cy, k1/k2
- Click **Save**

Output stored in `calibration.json`:
```json
"Intrinsics": {
  "CameraMatrix": [fx, 0, cx, 0, fy, cy, 0, 0, 1],
  "DistCoeffs": [k1, k2, p1, p2, k3],
  "ReprojectionError": 0.5,
  "UsedViewCount": 7,
  "CalibratedImageWidth": 1280,
  "CalibratedImageHeight": 960
}
```

## 3a. Stationary Camera Registration

**Menu:** Camera Settings > Calibrate Alignment > **Stationary Registration...**

For a camera fixed above the work area. Computes an image-to-world homography.

1. Place the ChArUco board at a known position on the work area
2. Enter the board's **world position**: X (mm), Y (mm), Rotation (degrees)
3. Click **Compute Registration**
4. Verifies board detection, computes homography and camera pose
5. Click **Save**

**Convention:** the board position refers to the **board origin** — the outer
top-left corner of the board (the corner of the top-left square) — and the board's
axes must point along the machine's +X and +Y directions. For example, a 100×140 mm
board placed with origin at (100, 50) covers the rectangle X∈[100,200], Y∈[50,190].

After saving, enable the camera overlay — a stationary camera with registration is
automatically warped and aligned to the work area using the homography (the manual
Overlay X/Y/W/H settings are ignored while registration is active).

Output:
```json
"Registration": {
  "Homography": [9 doubles, 3x3 image-to-world],
  "Rvec": [3 doubles, camera rotation],
  "Tvec": [3 doubles, camera translation],
  "ReprojectionError": 0.3
}
```

## 3b. Head-Mounted Offset Calibration

**Menu:** Camera Settings > Calibrate Alignment > **Head-Mounted Offset...**

For a camera attached to the laser head. Computes the offset between laser focal point and camera center.

### Auto (ChArUco)
1. Complete lens calibration first
2. Place the ChArUco board on the work area at a known position
3. Enter the board's **X, Y and Rotation** (board origin, see Stationary Registration)
4. Move the machine head over the board
5. Click **Auto (ChArUco Board)**
6. Computes camera-to-laser offset from board detection + machine position

### Manual (Burn Mark)
1. Place material on the work area
2. Click **Manual (Burn Mark)** 
3. Follow prompts: pulse laser → jog to align camera crosshair with burn mark

Output:
```json
"Offset": {
  "OffsetX": 50.0,
  "OffsetY": 0.0,
  "OffsetZ": 100.0
}
```
- `OffsetZ` is the camera height above the work surface (mm), used for FOV computation

## 4. Workspace Scan (Head-Mounted)

**Menu:** Camera Settings > **Scan Workspace**

Moves the machine through a grid covering the work area, captures camera frames at each position, and composites them into a full work area image.

- Requires: head-mounted camera with offset calibrated
- Configure **Overlap** (default 20%)
- Click **Start Scan** — the machine moves, pauses, captures
- Progress is shown; **Cancel** stops the scan
- **Clear Scan** removes the composite

Frame world coordinates are computed from camera intrinsics + height (OffsetZ):
```
FOV_width_mm = height_mm * image_width_px / fx
FOV_height_mm = height_mm * image_height_px / fy
```

## 5. Emulator for Testing

The emulator simulates a complete system without hardware:

### Starting the Emulator
```
dotnet run --project Emulator
```
Or run `Emulator\bin\Debug\net9.0-windows10.0.19041.0\grbl_burn_em_emulator.exe`

### Connecting from Main App
1. **Serial (GRBL):** Connect to `TCP:127.0.0.1:2345` — use the "Connect Emulator" button in Camera Settings
2. **Camera:** Select "Network Camera (Emulator)" from the device list, click Start Camera

### Emulator Controls

| Section | Control | Description |
|---------|---------|-------------|
| Camera | FOV W/H | Camera field of view in mm |
| Camera | Offset X/Y/Z | Camera position relative to laser head |
| Camera | Resolution | Output frame dimensions |
| Distortion | k1, k2 | Simulate radial lens distortion |
| Distortion | Noise | Add random noise to frames |
| ChArUco Board | Dictionary | Select ArUco marker dictionary |
| ChArUco Board | Squares | Number of chessboard squares |
| ChArUco Board | Board size | Physical board size in mm |
| ChArUco Board | Board X/Y | Board origin (outer top-left corner) on virtual bed |
| Jog Controls | D-pad | Move the virtual machine |
| Jog Controls | Step/Feed | Movement parameters |
| Toolbar | Clear Bed | Reset virtual bed to beige |
| Toolbar | Home (0,0) | Reset machine position |
| Toolbar | Reset Pan | Reset view panning |

### Panning
- **Ctrl + Left-click drag** or **Middle-click drag** to pan the bed view

### Testing the Calibration Pipeline
1. Emulator: check "Draw ChArUco board", set Y = 0, size = 120mm
2. Emulator: set FOV = 120x90mm, Cam Offset X = 50
3. Main app: connect to emulator (serial + camera)
4. Board Setup: DICT_4X4_50, 5x5 squares, 24mm square, 17mm marker
5. Lens Calibration: Auto Capture → Calibrate → Save
6. The emulated camera will detect real ArUco patterns rendered on the virtual bed
7. Stationary Registration: enter the board origin position (emulator Board X/Y),
   compute and save — the live overlay is then warped onto the work area
8. Head-Mounted Offset: enter board origin + rotation, run Auto — the result should
   match the emulator's configured Cam Offset

## Configuration File

All calibration data is stored in `calibration.json` in the application directory:

```json
{
  "Intrinsics": { ... },
  "Registration": { ... },
  "Offset": {
    "OffsetX": 0.0,
    "OffsetY": 0.0,
    "OffsetZ": 0.0
  },
  "BoardConfig": {
    "DictionaryName": "DICT_4X4_50",
    "SquaresX": 5,
    "SquaresY": 7,
    "SquareLengthMm": 20.0,
    "MarkerLengthMm": 15.0
  }
}
```

## Troubleshooting

| Problem | Solution |
|---------|----------|
| Board not detected | Ensure board is in camera FOV. Increase lighting. Check dictionary matches. |
| Calibration fails (RMSE > 2.0) | Remove blurry/outlier views. Capture 7+ sharp views at different angles. |
| Auto capture captures nothing | Board not in view. Adjust machine position or increase grid step/rows. |
| "Camera not running" | Start camera in Camera Settings first. |
| Emulator not moving | Click "Connect Emulator" (yellow button) to connect serial. |
| Dialogs appear behind | Fixed — all calibration dialogs now open on top of parent. |
| Board stays after moving | Fixed — only previous board rectangle is cleared when repositioned. |
| Emulator detection fails | Increase FOV/grid. Ensure dict matches Board Setup. Place board Y=0. |
