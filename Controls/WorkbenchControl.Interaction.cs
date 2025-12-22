/*
 * Copyright (c) 2025 Marko Viitanen (Fador) and the project contributors. All rights reserved.
 * Licensed under the MIT License. See LICENSE file in the project root for full license information.
 *
 * This file is part of the GRBL Burn'Em laser control software
 */
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;
using System.Linq;
using grbl_burn_em.Data;
using grbl_burn_em.Tools;
using grbl_burn_em.Data.Commands;
using grbl_burn_em.Forms;

namespace grbl_burn_em.Controls
{
    public partial class WorkbenchControl
    {
        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            
            // 1. Reset State
            if (e.Button == MouseButtons.Left && !_isPanning) ResetInteractionState();

            PointF worldPos = ScreenToWorld(e.Location);
            _currentMouseWorld = worldPos; // Keep raw for selection hit testing
            
            PointF snappedPos = Snap(worldPos);

            // 2. Panning (Right Click)
            if (e.Button == MouseButtons.Right)
            {
                if (ToolManager.Instance.CurrentTool == ToolType.DrawBezier && _currentBezier != null)
                {
                    FinalizeBezier();
                    return;
                }
                
                _isPanning = true;
                _lastMousePos = e.Location;
                _rightClickDownPos = e.Location;
                Cursor = Cursors.SizeAll;
                return;
            }

            if (e.Button != MouseButtons.Left) return;

        
            // 3. Tool Creation
            if (ToolManager.Instance.CurrentTool == ToolType.DrawLine)
            {
                 var line = new LaserPath { Name = "Line", Position = snappedPos };
                 line.Points.Add(snappedPos);
                 line.Points.Add(snappedPos);
                 ProjectState.Instance.AddObject(line);
                 _interactionObject = line;
                 _isDragging = true;
                 _dragStartPos = snappedPos;
                 return;
            }
            else if (ToolType.DrawCircle == ToolManager.Instance.CurrentTool)
            {
                 var circle = new LaserCircle { Name = "Circle", Position = snappedPos, Size = new SizeF(0, 0) };
                 ProjectState.Instance.AddObject(circle);
                 _interactionObject = circle;
                 _isDragging = true;
                 _dragStartPos = snappedPos;
                 return;
            }
            else if (ToolManager.Instance.CurrentTool == ToolType.DrawBox)
            {
                 var box = new LaserRectangle { Name = "Rectangle", Position = snappedPos, Size = new SizeF(0, 0) };
                 ProjectState.Instance.AddObject(box);
                 _interactionObject = box;
                 _isDragging = true;
                 _dragStartPos = snappedPos;
                 return;
            }
            else if (ToolManager.Instance.CurrentTool == ToolType.DrawBezier)
            {
                 if (_currentBezier == null)
                 {
                      _currentBezier = new LaserBezier { Name = "Bezier" };
                      _currentBezier.Points.Add(snappedPos);
                      ProjectState.Instance.AddObject(_currentBezier);
                 }
                 else
                 {
                      // Add new segment (C1, C2, End)
                      
                      // Snap to Start if close (Closing the loop)
                      if (_currentBezier.Points.Count > 0)
                      {
                          var start = _currentBezier.Points[0];
                          float distSq = (snappedPos.X - start.X) * (snappedPos.X - start.X) + (snappedPos.Y - start.Y) * (snappedPos.Y - start.Y);
                          float snapThresh = 15.0f / _zoom; 
                          if (distSq < snapThresh * snapThresh)
                          {
                              snappedPos = start;
                          }
                      }

                      var last = _currentBezier.Points.Last();
                      float dx = snappedPos.X - last.X;
                      float dy = snappedPos.Y - last.Y;
                      // Auto-calculated smooth control points
                      _currentBezier.Points.Add(new PointF(last.X + dx * 0.33f, last.Y + dy * 0.33f)); // C1
                      _currentBezier.Points.Add(new PointF(last.X + dx * 0.66f, last.Y + dy * 0.66f)); // C2
                      _currentBezier.Points.Add(snappedPos); // End
                      
                      _currentBezier.UpdateBounds();
                      Invalidate();
                 }
                 return;
            }
            else if (ToolManager.Instance.CurrentTool == ToolType.Text)
            {
                string val = "Text";
                string fontName = "Arial";
                float fontSize = 20f;
        
                using (var form = new TextEditorForm(val, fontName, fontSize, FontStyle.Regular))
                {
                    if (form.ShowDialog() == DialogResult.OK)
                    {
                        val = form.TextValue;
                        fontName = form.FontName;
                        fontSize = form.FontSize;
                        
                        if (string.IsNullOrWhiteSpace(val)) return;
        
                        var t = new LaserText();
                        t.Text = val;
                        t.FontName = fontName;
                        t.FontSize = fontSize;
                        t.FontStyle = form.FontStyle;
                        t.Position = snappedPos;
        
                        t.UpdateTextSize();
                        
                        ProjectState.Instance.AddObject(t);
        
                        // Auto-select
                        ProjectState.Instance.SelectedObjects = new List<LaserObject> { t };
        
                        // Switch back to select for convenience
                        ToolManager.Instance.SetTool(ToolType.Select);
        
                        Invalidate();
                    }
                }
                return;
            }
            if (ToolManager.Instance.CurrentTool == ToolType.Ruler)
            {
                 _isMeasuring = true;
                 _measureStart = worldPos;
                 _measureEnd = worldPos;
                 Invalidate();
                 return;
            }

            // 3b. Click To Move
            if (ToolManager.Instance.CurrentTool == ToolType.ClickToMove)
            {
                 // Check if machine is Idle or Jogging
                 if (SerialInterface.Instance.IsConnected)
                 {
                     int feed = 1000; // mm/min
                     string cmd = $"$J=G90 X{snappedPos.X:F3} Y{snappedPos.Y:F3} F{feed}";
                     SerialInterface.Instance.Write(cmd + "\n");
                 }
                 return;
            }

            // 4. Selection Tool
            if (ToolManager.Instance.CurrentTool == ToolType.Select)
            {
                // A. Check Resize Handles
                int handle = HitTestHandles(worldPos);
                if (handle != -1)
                {
                    _dragHandleIndex = handle;
                    _isResizing = true;
                    _dragStartPos = snappedPos; 
                    _initialGroupBounds = GetSelectionBounds();
                    SnapshotSelection();
                    Invalidate();
                    return;
                }

                // B. Hit Test Objects
                float hitTolerance = 8.0f / _zoom; 
                var hitObj = ProjectState.Instance.Objects.Reverse().FirstOrDefault(o => o.HitTest(worldPos, hitTolerance));

                if (hitObj != null)
                {
                    bool isCtrl = (Control.ModifierKeys == Keys.Control);
                    bool isSelected = ProjectState.Instance.SelectedObjects.Contains(hitObj);

                    if (isCtrl)
                    {
                        // Toggle Selection
                        var newSelection = new List<LaserObject>(ProjectState.Instance.SelectedObjects);
                        if (isSelected)
                        {
                            newSelection.Remove(hitObj);
                            ProjectState.Instance.SelectedObjects = newSelection;
                        }
                        else
                        {
                            newSelection.Add(hitObj);
                            ProjectState.Instance.SelectedObjects = newSelection;
                            // Prepare for move
                            _interactionObject = hitObj;
                            _isMoving = true;
                            _dragStartPos = snappedPos;
                            _moveStartPos = snappedPos;
                        }
                    }
                    else
                    {
                    // Normal Click
                        if (isSelected)
                        {
                            // Clicked on already selected object -> Prepare for move (Group move)
                            _interactionObject = hitObj;
                            _isMoving = true;
                            _dragStartPos = snappedPos;
                            _moveStartPos = snappedPos;
                        }
                        else
                        {
                            // Select ONLY this object
                            ProjectState.Instance.SelectedObjects = new List<LaserObject> { hitObj };
                            _interactionObject = hitObj;
                            _isMoving = true;
                            _dragStartPos = snappedPos;
                            _moveStartPos = snappedPos;
                        }
                    }
                }
                else
                {
                    // C. Clicked Empty Space -> Start Selection Box
                    _isSelecting = true;
                    _dragStartPos = worldPos;
                }

                Invalidate();
            }
            else if (ToolManager.Instance.CurrentTool == ToolType.Rotate)
            {
                 if (ProjectState.Instance.SelectedObjects.Count > 0)
                 {
                     var b = GetSelectionBounds();
                     if (b != null)
                     {
                         _rotateCenter = new PointF(b.Value.Left + b.Value.Width/2, b.Value.Top + b.Value.Height/2);
                         _rotateStartAngle = (float)(Math.Atan2(worldPos.Y - _rotateCenter.Y, worldPos.X - _rotateCenter.X) * 180.0 / Math.PI);
                         SnapshotSelection();
                         _isRotating = true;
                         Invalidate();
                     }
                 }
            }
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            PointF worldPos = ScreenToWorld(e.Location);
            _currentMouseWorld = worldPos; // Raw for some interactions
                                           
            PointF effectivePos = Snap(worldPos);

            if (_isPanning)
            {
                float dx = e.X - _lastMousePos.X;
                float dy = e.Y - _lastMousePos.Y;
                _panOffset.X += dx;
                _panOffset.Y += dy;
                _lastMousePos = e.Location;
                Invalidate();
                MousePositionChanged?.Invoke(ScreenToWorld(e.Location));
                return;
            }

            // Fire Mouse Position Event (Throttled)
            long now = DateTime.Now.Ticks;
            if (now - _lastUpdateTicks > 500000) // 50ms
            {
                 MousePositionChanged?.Invoke(worldPos);
            }

            // 2. Resizing
            if (_isResizing)
            {
                UpdateResize(effectivePos); // Snap resize handle
                return;
            }

            if (_isRotating)
            {
                float currentAngle = (float)(Math.Atan2(worldPos.Y - _rotateCenter.Y, worldPos.X - _rotateCenter.X) * 180.0 / Math.PI);
                float deltaAngle = currentAngle - _rotateStartAngle;
                
                foreach(var kvp in _initialStates)
                {
                    var obj = kvp.Key;
                    var init = kvp.Value;
                    
                    // 1. Rotate Orientation
                    obj.Rotation = init.Rotation + deltaAngle;
                    
                    // 2. Orbit Position (Around Center)
                    float oldCenterX = init.Pos.X + init.Size.Width / 2f;
                    float oldCenterY = init.Pos.Y + init.Size.Height / 2f;
                    
                    float rx = oldCenterX - _rotateCenter.X;
                    float ry = oldCenterY - _rotateCenter.Y;
                    
                    float rad = deltaAngle * (float)Math.PI / 180f;
                    float c = (float)Math.Cos(rad);
                    float s = (float)Math.Sin(rad);
                    
                    float nx = rx * c - ry * s;
                    float ny = rx * s + ry * c;
                    
                    float newCenterX = _rotateCenter.X + nx;
                    float newCenterY = _rotateCenter.Y + ny;

                    // Position is top-left
                    PointF newPos = new PointF(newCenterX - init.Size.Width / 2f, newCenterY - init.Size.Height / 2f);
                    float dx = newPos.X - obj.Position.X;
                    float dy = newPos.Y - obj.Position.Y;
                    
                    obj.Position = newPos;

                    // Shift points if absolute (Paths and Beziers)
                    if (obj is LaserPath p && init.Points != null)
                    {
                        for(int i=0; i<p.Points.Count; i++)
                        {
                             p.Points[i] = new PointF(p.Points[i].X + dx, p.Points[i].Y + dy);
                        }
                    }
                    else if (obj is LaserBezier b && init.Points != null)
                    {
                        for(int i=0; i<b.Points.Count; i++)
                        {
                             b.Points[i] = new PointF(b.Points[i].X + dx, b.Points[i].Y + dy);
                        }
                        b.UpdateBounds();
                    }
                }
                Invalidate();
                
                // Fire Mouse Position Event (Throttled)
                long nowMove = DateTime.Now.Ticks;
                if (nowMove - _lastUpdateTicks > 500000) // 50ms
                {
                     _lastUpdateTicks = nowMove;
                }
                return;
            }

            // 3. Moving Objects
            if (_isMoving && _interactionObject != null)
            {
                Cursor = Cursors.SizeAll;
                // Calculate delta based on Effective Positions to ensure we move in steps
                float dx = effectivePos.X - Snap(_dragStartPos).X; 
                float dy = effectivePos.Y - Snap(_dragStartPos).Y;
                
                float incDx = effectivePos.X - _dragStartPos.X; 
                float incDy = effectivePos.Y - _dragStartPos.Y;
                
                if (Math.Abs(incDx) > 0.0001 || Math.Abs(incDy) > 0.0001)
                {
                    foreach(var obj in ProjectState.Instance.SelectedObjects)
                    {
                        MoveObject(obj, incDx, incDy);
                    }
                    _dragStartPos = effectivePos; // Update to the position we moved TO
                }
                
                Invalidate();
                // Fire Mouse Position Event (Throttled)
                long nowMove = DateTime.Now.Ticks;
                if (nowMove - _lastUpdateTicks > 500000) // 50ms
                {
                     MainForm.Instance.UpdateSelectedObjects(false);
                     _lastUpdateTicks = nowMove;
                }

                return;
            }

            // 4. Creating Objects (Dragging)
            if (_isDragging && _interactionObject != null)
            {
                PointF start = Snap(_dragStartPos); 
                
                if (ToolManager.Instance.CurrentTool == ToolType.DrawBox || ToolManager.Instance.CurrentTool == ToolType.DrawCircle)
                {
                    float w = Math.Abs(effectivePos.X - start.X);
                    float h = Math.Abs(effectivePos.Y - start.Y);

                    if (Control.ModifierKeys == Keys.Control)
                    {
                        float max = Math.Max(w, h);
                        w = max;
                        h = max;
                    }

                    // Determine Top-Left based on drag direction
                    float x = (effectivePos.X < start.X) ? start.X - w : start.X;
                    float y = (effectivePos.Y < start.Y) ? start.Y - h : start.Y;

                    _interactionObject.Position = new PointF(x, y);
                    _interactionObject.Size = new SizeF(w, h);
                }
                else if (ToolManager.Instance.CurrentTool == ToolType.DrawLine)
                {
                    if (_interactionObject is LaserPath path && path.Points.Count >= 2)
                    {
                        path.Points[0] = start; // Ensure start point is snapped too
                        path.Points[1] = effectivePos;
                        path.UpdateBounds();
                    }
                }
                
                Invalidate();
                return;
            }
            
            if (_isMeasuring)
            {
                 _measureEnd = worldPos;
                 Invalidate();
                 return;
            }

            // 5. Selection Box
            if (_isSelecting)
            {
                Invalidate();
                return;
            }

            // 6. Cursor Updates
            if (ToolManager.Instance.CurrentTool == ToolType.Select)
            {
                if (HitTestHandles(worldPos) != -1)
                {
                    Cursor = Cursors.SizeAll;
                }
                else
                {
                    float hitTolerance = 8.0f / _zoom;
                    bool hit = false;
                    foreach (var obj in ProjectState.Instance.Objects.Reverse())
                    {
                        if (obj.HitTest(worldPos, hitTolerance))
                        {
                            hit = true;
                            break;
                        }
                    }
                    Cursor = hit ? Cursors.SizeAll : Cursors.Default;
                }
            }
            else
            {
                Cursor = Cursors.Default;
            }
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            base.OnMouseUp(e);
            
            _currentMouseWorld = ScreenToWorld(e.Location);
            
            if (e.Button == MouseButtons.Right)
            {
                if (_isPanning)
                {
                    _isPanning = false;
                    Cursor = Cursors.Default;
                    
                    float dx = e.X - _rightClickDownPos.X;
                    float dy = e.Y - _rightClickDownPos.Y;
                    if (Math.Abs(dx) < 5 && Math.Abs(dy) < 5)
                    {
                        ShowContextMenu(e.Location);
                    }
                    return;
                }
            }

            if (_isRotating)
            {
                _isRotating = false;
                
                 var newStates = new Dictionary<LaserObject, (PointF Pos, SizeF Size, List<PointF>? Points, float FontSize, float Rotation)>();
                 foreach(var obj in ProjectState.Instance.SelectedObjects)
                 {
                    List<PointF>? pts = null;
                    float fSize = 0;
                    if (obj is LaserPath p) pts = new List<PointF>(p.Points);
                    else if (obj is LaserBezier b) pts = new List<PointF>(b.Points);
                    
                    if (obj is LaserText t) fSize = t.FontSize;
                    
                    newStates[obj] = (obj.Position, obj.Size, pts, fSize, obj.Rotation);
                 }
                 
                 CommandManager.Instance.Execute(new ResizeCommand(_initialStates, newStates));
                 
                 MainForm.Instance.UpdateSelectedObjects();
                 _initialStates.Clear();
                 ResetInteractionState();
                 return;
            }

            if (_isSelecting)
            {
                _isSelecting = false;
                // Find objects in rect
                float x = Math.Min(_dragStartPos.X, _currentMouseWorld.X);
                float y = Math.Min(_dragStartPos.Y, _currentMouseWorld.Y);
                float w = Math.Abs(_currentMouseWorld.X - _dragStartPos.X);
                float h = Math.Abs(_currentMouseWorld.Y - _dragStartPos.Y);
                var rect = new RectangleF(x, y, w, h);
                
                var list = new List<LaserObject>();
                foreach(var obj in ProjectState.Instance.Objects)
                {
                    var objRect = obj.GetBounds();
                    if (!objRect.IsEmpty && rect.IntersectsWith(objRect))
                    {
                        list.Add(obj);
                    }
                }
                if (Control.ModifierKeys == Keys.Control)
                {
                     var current = new List<LaserObject>(ProjectState.Instance.SelectedObjects);
                     foreach(var item in list)
                     {
                         if (!current.Contains(item)) current.Add(item);
                     }
                     ProjectState.Instance.SelectedObjects = current;
                }
                else
                {
                    ProjectState.Instance.SelectedObjects = list;
                }

                MainForm.Instance.UpdateSelectedObjects();
                Invalidate();
                return;
            }

            if (_isDragging)
            {
                _isDragging = false;
                if (_interactionObject != null)
                {
                   ProjectState.Instance.RemoveObject(_interactionObject);
                   var cmd = new AddObjectCommand(_interactionObject);
                   CommandManager.Instance.Execute(cmd);
                }
                _interactionObject = null;
            }

            if (_isMeasuring)
            {
                 _isMeasuring = false;
                 Invalidate();
            }

            if (_isMoving)
            {
                _isMoving = false;
                
                // Calculate Total Move Delta
                float dx = _currentMouseWorld.X - _moveStartPos.X;
                float dy = _currentMouseWorld.Y - _moveStartPos.Y;
                bool movedSignificantly = Math.Abs(dx) > 0.001 || Math.Abs(dy) > 0.001;

                if (movedSignificantly)
                {
                    // Revert the interactive move to prevent double application by MoveCommand.Execute()
                    foreach(var obj in ProjectState.Instance.SelectedObjects)
                    {
                         MoveObject(obj, -dx, -dy);
                    }

                    var cmd = new MoveCommand(ProjectState.Instance.SelectedObjects, dx, dy);
                    CommandManager.Instance.Execute(cmd);
                    
                    MainForm.Instance.UpdateSelectedObjects(); 
                }
                else
                {
                    if (ToolManager.Instance.CurrentTool == ToolType.Select && 
                        Control.ModifierKeys != Keys.Control && 
                        _interactionObject != null)
                    {
                        ProjectState.Instance.SelectedObjects = new List<LaserObject> { _interactionObject };
                        Invalidate();
                    }
                }
                _interactionObject = null;
            }
            
            if (_isResizing)
            {
                _isResizing = false;
                _dragHandleIndex = -1;
                _initialGroupBounds = null;
                
                var newStates = new Dictionary<LaserObject, (PointF Pos, SizeF Size, List<PointF>? Points, float FontSize, float Rotation)>();
                foreach (var obj in ProjectState.Instance.SelectedObjects)
                {
                    List<PointF>? pts = null;
                    float fSize = 0;
                    if (obj is LaserPath p) pts = new List<PointF>(p.Points);
                    else if (obj is LaserBezier b) pts = new List<PointF>(b.Points);
                    
                    if (obj is LaserText t) fSize = t.FontSize;
                    
                    newStates[obj] = (obj.Position, obj.Size, pts, fSize, obj.Rotation);
                }
                
                var cmd = new ResizeCommand(_initialStates, newStates);
                CommandManager.Instance.Execute(cmd);
                MainForm.Instance.UpdateSelectedObjects(); // Final update
                
                _initialStates.Clear();
            }
            
            // Final safety reset
            ResetInteractionState();
        }

        protected override void OnMouseDoubleClick(MouseEventArgs e)
        {
            base.OnMouseDoubleClick(e);

            if (ToolManager.Instance.CurrentTool != ToolType.Select) return;

            PointF worldPos = ScreenToWorld(e.Location);
            float hitTolerance = 8.0f / _zoom;

            var hitObj = ProjectState.Instance.Objects.Reverse().FirstOrDefault(o => o.HitTest(worldPos, hitTolerance));

            if (hitObj is LaserText textObj)
            {
                MainForm.Instance.EditText(textObj);
            }
        }
        
        protected override void OnMouseWheel(MouseEventArgs e)
        {
            base.OnMouseWheel(e);
            float zoomFactor = 1.1f;
            if (e.Delta > 0)
                _zoom *= zoomFactor;
            else
                _zoom /= zoomFactor;
            
            // Clamp zoom
            if (_zoom < 0.1f) _zoom = 0.1f;
            if (_zoom > 50.0f) _zoom = 50.0f;

            Invalidate();
        }

        private void ResetInteractionState()
        {
            _isSelecting = false;
            _isDragging = false;
            _isMoving = false;
            _isResizing = false;
            _interactionObject = null;
            _dragHandleIndex = -1;
            _initialGroupBounds = null;
            _initialStates.Clear();
        }
        
        private RectangleF? GetSelectionBounds()
        {
            if (ProjectState.Instance.SelectedObjects.Count == 0) return null;
            
            float minX = float.MaxValue, minY = float.MaxValue;
            float maxX = float.MinValue, maxY = float.MinValue;
            bool hasBounds = false;
            
            foreach (var obj in ProjectState.Instance.SelectedObjects)
            {
                var b = obj.GetBounds();
                if (b.IsEmpty) continue;
                
                if (b.Left < minX) minX = b.Left;
                if (b.Top < minY) minY = b.Top;
                if (b.Right > maxX) maxX = b.Right;
                if (b.Bottom > maxY) maxY = b.Bottom;
                hasBounds = true;
            }
            
            if (!hasBounds) return null;
            return new RectangleF(minX, minY, maxX - minX, maxY - minY);
        }
        
        private void SnapshotSelection()
        {
            _initialStates.Clear();
            foreach (var obj in ProjectState.Instance.SelectedObjects)
            {
                List<PointF>? pts = null;
                float fSize = 0;
                if (obj is LaserPath p) pts = new List<PointF>(p.Points);
                else if (obj is LaserBezier b) pts = new List<PointF>(b.Points);
                
                if (obj is LaserText t) fSize = t.FontSize;
                
                _initialStates[obj] = (obj.Position, obj.Size, pts, fSize, obj.Rotation);
            }
        }

        private void UpdateResize(PointF currentPos)
        {
            // 1. Node Editing
            if (_dragHandleIndex >= 100)
            {
                 if (ProjectState.Instance.SelectedObjects.Count == 1 && ProjectState.Instance.SelectedObjects[0] is LaserBezier bezier)
                 {
                     int idx = _dragHandleIndex - 100;
                     if (idx >= 0 && idx < bezier.Points.Count)
                     {
                         // Snap Ends together
                         if (idx == 0 || idx == bezier.Points.Count - 1)
                         {
                             int otherIdx = (idx == 0) ? bezier.Points.Count - 1 : 0;
                             var other = bezier.Points[otherIdx];
                             float distSq = (currentPos.X - other.X) * (currentPos.X - other.X) + (currentPos.Y - other.Y) * (currentPos.Y - other.Y);
                             float snapThresh = 15.0f / _zoom;
                             if (distSq < snapThresh * snapThresh)
                             {
                                 currentPos = other;
                             }
                         }
                         
                         bezier.Points[idx] = currentPos;
                         bezier.UpdateBounds();
                         Invalidate();
                     }
                 }
                 return;
            }

            if (_initialGroupBounds == null) return;
            var b = _initialGroupBounds.Value;
            float l = b.Left, t = b.Top, r = b.Right, bm = b.Bottom;
            
            // Update bounds based on handle
            float newL = l, newT = t, newR = r, newB = bm;
            
            if (_dragHandleIndex == 0 || _dragHandleIndex == 6 || _dragHandleIndex == 7) newL = currentPos.X;
            if (_dragHandleIndex == 0 || _dragHandleIndex == 1 || _dragHandleIndex == 2) newT = currentPos.Y;
            if (_dragHandleIndex == 2 || _dragHandleIndex == 3 || _dragHandleIndex == 4) newR = currentPos.X;
            if (_dragHandleIndex == 4 || _dragHandleIndex == 5 || _dragHandleIndex == 6) newB = currentPos.Y;
            
            float newW = newR - newL;
            float newH = newB - newT;
            
            // Size Snapping
            if (IsSnappingEnabled)
            {
                float interval = SnapInterval;
                if (Math.Abs(interval) > 0.001f)
                {
                    newW = (float)Math.Round(newW / interval) * interval;
                    newH = (float)Math.Round(newH / interval) * interval;
                    if (newW < interval) newW = interval; 
                    if (newH < interval) newH = interval;
                }
            }

            // Determine Scale Factors
            float scaleX = (b.Width == 0) ? 1 : newW / b.Width;
            float scaleY = (b.Height == 0) ? 1 : newH / b.Height;

            // Aspect Ratio Lock (Ctrl)
            if (Control.ModifierKeys == Keys.Control)
            {
                bool isCorner = (_dragHandleIndex == 0 || _dragHandleIndex == 2 || _dragHandleIndex == 4 || _dragHandleIndex == 6);
                bool isTopBottom = (_dragHandleIndex == 1 || _dragHandleIndex == 5);
                bool isLeftRight = (_dragHandleIndex == 3 || _dragHandleIndex == 7);

                if (isCorner)
                {
                    float mag = Math.Max(Math.Abs(scaleX), Math.Abs(scaleY));
                    scaleX = Math.Sign(scaleX) * mag;
                    scaleY = Math.Sign(scaleY) * mag;
                }
                else if (isTopBottom)
                {
                    scaleX = Math.Abs(scaleY);
                }
                else if (isLeftRight)
                {
                    scaleY = Math.Abs(scaleX);
                }
                
                float finalW = b.Width * scaleX;
                float finalH = b.Height * scaleY;
                
                if (_dragHandleIndex == 0) { newL = b.Right - finalW; newT = b.Bottom - finalH; }
                if (_dragHandleIndex == 1) { newT = b.Bottom - finalH; float cx = (b.Left + b.Right) / 2; newL = cx - finalW / 2; newR = cx + finalW / 2; }
                if (_dragHandleIndex == 2) { newL = b.Left; newT = b.Bottom - finalH; }
                if (_dragHandleIndex == 3) { newL = b.Left; float cy = (b.Top + b.Bottom) / 2; newT = cy - finalH / 2; }
                if (_dragHandleIndex == 4) { newL = b.Left; newT = b.Top; }
                if (_dragHandleIndex == 5) { newT = b.Top; float cx = (b.Left + b.Right) / 2; newL = cx - finalW / 2; }
                if (_dragHandleIndex == 6) { newL = b.Right - finalW; newT = b.Top; }
                if (_dragHandleIndex == 7) { newL = b.Right - finalW; float cy = (b.Top + b.Bottom) / 2; newT = cy - finalH / 2; }
            }
            
            // Apply to objects
            foreach (var kvp in _initialStates)
            {
                var obj = kvp.Key;
                var init = kvp.Value;
                
                float relX = init.Pos.X - b.Left;
                float relY = init.Pos.Y - b.Top;
                
                float objX = newL + relX * scaleX;
                float objY = newT + relY * scaleY;
                float objW = init.Size.Width * scaleX;
                float objH = init.Size.Height * scaleY;

                if (objW < 0) { objX += objW; objW = -objW; }
                if (objH < 0) { objY += objH; objH = -objH; }

                obj.Position = new PointF(objX, objY);
                obj.Size = new SizeF(objW, objH);
                
                if (obj is LaserPath p && init.Points != null)
                {
                    for(int i=0; i<p.Points.Count; i++)
                    {
                        float px = init.Points[i].X - b.Left;
                        float py = init.Points[i].Y - b.Top;
                        p.Points[i] = new PointF(newL + px * scaleX, newT + py * scaleY);
                    }
                }
                else if (obj is LaserBezier bez && init.Points != null)
                {
                    for(int i=0; i<bez.Points.Count && i<init.Points.Count; i++)
                    {
                        float px = init.Points[i].X - b.Left;
                        float py = init.Points[i].Y - b.Top;
                        bez.Points[i] = new PointF(newL + px * scaleX, newT + py * scaleY);
                    }
                    bez.UpdateBounds();
                }
                else if (obj is LaserText txt)
                {
                     float newFs = init.FontSize * Math.Abs(scaleY);
                     if (newFs < 1f) newFs = 1f;
                     txt.FontSize = newFs;
                }
            }
            Invalidate();
            
            long nowResize = DateTime.Now.Ticks;
            if (nowResize - _lastUpdateTicks > 500000)
            {
                MainForm.Instance.UpdateSelectedObjects(false); 
                _lastUpdateTicks = nowResize;
            }
        }
        
        private void MoveObject(LaserObject obj, float dx, float dy)
        {
             if (obj is LaserPath path)
             {
                 for(int i=0; i<path.Points.Count; i++)
                 {
                     path.Points[i] = new PointF(path.Points[i].X + dx, path.Points[i].Y + dy);
                 }
                 path.Position = new PointF(path.Position.X + dx, path.Position.Y + dy);
             }
             else if (obj is LaserBezier bezier)
             {
                 for(int i=0; i<bezier.Points.Count; i++)
                 {
                     bezier.Points[i] = new PointF(bezier.Points[i].X + dx, bezier.Points[i].Y + dy);
                 }
                 bezier.Position = new PointF(bezier.Position.X + dx, bezier.Position.Y + dy);
             }
             else if (obj is LaserGroup group)
             {
                 foreach(var child in group.Children)
                 {
                     MoveObject(child, dx, dy);
                 }
             }
             else
             {
                 obj.Position = new PointF(obj.Position.X + dx, obj.Position.Y + dy);
             }
        }

        private int HitTestHandles(PointF pos)
        {
             // 1. Node Handles (Index >= 100)
             if (ProjectState.Instance.SelectedObjects.Count == 1 && ProjectState.Instance.SelectedObjects[0] is LaserBezier bezier)
             {
                 float size = 8.0f / _zoom;
                 for(int i=0; i<bezier.Points.Count; i++)
                 {
                     var p = bezier.Points[i];
                     if (new RectangleF(p.X - size/2, p.Y - size/2, size, size).Contains(pos))
                     {
                         return 100 + i;
                     }
                 }
             }

             // 2. Resize Handles
             var bounds = GetSelectionBounds();
             if (bounds == null) return -1;
             
             var handles = GetHandlePositions(bounds.Value);
             float handleSize = 8.0f / _zoom;
             
             for(int i=0; i<handles.Length; i++)
             {
                 var r = new RectangleF(handles[i].X - handleSize/2, handles[i].Y - handleSize/2, handleSize, handleSize);
                 if (r.Contains(pos)) return i;
             }
             return -1;
        }
        
        private void FinalizeBezier()
        {
            if (_currentBezier != null)
            {
                if (_currentBezier.Points.Count < 4) // Minimum 1 segment
                {
                    ProjectState.Instance.RemoveObject(_currentBezier);
                }
                _currentBezier = null;
                Invalidate();
            }
        }
    }
}

