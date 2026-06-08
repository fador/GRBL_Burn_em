using grbl_burn_em.Data;
using grbl_burn_em.Data.Commands;
using grbl_burn_em.Data.Pdf;
using grbl_burn_em.Tools;
using System.Text.Json;

namespace grbl_burn_em;

public partial class MainForm
{
    private void ImportFile()
    {
        using var ofd = new OpenFileDialog();
        ofd.Filter = "Supported Files|*.bmp;*.jpg;*.jpeg;*.png;*.svg;*.pdf|Images|*.bmp;*.jpg;*.jpeg;*.png|Scalable Vector Graphics|*.svg|PDF Documents|*.pdf|All Files|*.*";
        if (ofd.ShowDialog() == DialogResult.OK)
        {
            string ext = Path.GetExtension(ofd.FileName).ToLower();
            
            if (ext == ".svg")
            {
                try 
                {
                    var objects = SvgImporter.Import(ofd.FileName);
                    var cmd = new AddObjectCommand(objects);
                    
                    foreach(var obj in objects)
                    {
                        if (ProjectState.Instance.ActiveLayer != null)
                             obj.LayerId = ProjectState.Instance.ActiveLayer.Id;
                    }
                    CommandManager.Instance.Execute(cmd);
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"Failed to import SVG: {ex.Message}", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
                }
            }
            else if (ext == ".pdf")
            {
                try 
                {
                    var result = PdfImporter.Import(ofd.FileName);
                    
                    if (result.Objects.Count == 0)
                    {
                        if (result.Warnings.Count > 0)
                        {
                            string msg = "Import failed / no objects found. Warnings:\n\n" + string.Join("\n", result.Warnings.Take(10));
                            if (result.Warnings.Count > 10) msg += $"\n...and {result.Warnings.Count - 10} more.";
                            MessageBox.Show(msg, "PDF Import Failed", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                        }
                        else
                        {
                            MessageBox.Show("No supported objects found in PDF.", "Warning", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                        }
                    }
                    else
                    {
                        var cmd = new AddObjectCommand(result.Objects);
                        
                        foreach(var obj in result.Objects)
                        {
                            if (ProjectState.Instance.ActiveLayer != null)
                                 obj.LayerId = ProjectState.Instance.ActiveLayer.Id;
                        }
                        CommandManager.Instance.Execute(cmd);
                    }
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"Failed to import PDF: {ex.Message}", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
                }
            }
            else
            {
                // Assume Image
                try
                {
                    // Load into LaserImage
                    var lImg = new LaserImage();
                    lImg.Name = Path.GetFileNameWithoutExtension(ofd.FileName);
                    lImg.ImagePath = ofd.FileName;
                    
                    using var stream = new FileStream(ofd.FileName, FileMode.Open, FileAccess.Read);
                    var lbmp = new Bitmap(stream);
                    lImg.Image = new Bitmap(lbmp); 
                    
                    lImg.Position = new PointF(0, 0);
                    
                    float dpiX = lImg.Image.HorizontalResolution;
                    float dpiY = lImg.Image.VerticalResolution;
                    if (dpiX <= 0) dpiX = 96;
                    if (dpiY <= 0) dpiY = 96;
                    
                    float width = lImg.Image.Width * (96.0f / dpiX);
                    float height = lImg.Image.Height * (96.0f / dpiY);
                    
                    lImg.Size = new SizeF(width, height);

                    if (ProjectState.Instance.ActiveLayer != null)
                        lImg.LayerId = ProjectState.Instance.ActiveLayer.Id;

                    // Command
                    var cmd = new AddObjectCommand(lImg);
                    CommandManager.Instance.Execute(cmd);
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"Failed to load image: {ex.Message}", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
                }
            }
        }
    }


    protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
    {
        if (keyData == (Keys.Control | Keys.Z))
        {
            CommandManager.Instance.Undo();
            return true;
        }
        if (keyData == (Keys.Control | Keys.Y))
        {
            CommandManager.Instance.Redo();
            return true;
        }
        if (keyData == (Keys.Control | Keys.C))
        {
             CopySelection();
             return true;
        }
        if (keyData == (Keys.Control | Keys.V))
        {
             PasteSelection();
             return true;
        }
        if (keyData == Keys.Delete)
        {
             DeleteSelection();
             return true;
        }
        if (keyData == (Keys.Control | Keys.G))
        {
            var sel = ProjectState.Instance.SelectedObjects;
            if (sel.Count > 1)
            {
                var cmd = new GroupCommand(sel);
                CommandManager.Instance.Execute(cmd);
            }
            return true;
        }
        if (keyData == (Keys.Control | Keys.U))
        {
            // Ungroup ALL selected groups
            var sel = ProjectState.Instance.SelectedObjects;
            if (sel.Any(o => o is LaserGroup))
            {
                var cmd = new UngroupCommand(sel);
                CommandManager.Instance.Execute(cmd);
            }
            return true;
        }
        return base.ProcessCmdKey(ref msg, keyData);
    }

    public void CopySelection()
    {
        var sel = ProjectState.Instance.SelectedObjects;
        if (sel.Count > 0)
        {
            var dtos = sel.Select(ProjectSerializer.ToDto).ToList();
            var options = new JsonSerializerOptions { WriteIndented = true };
            options.Converters.Add(new ColorJsonConverter());
            var json = JsonSerializer.Serialize(dtos, options);
            Clipboard.SetText(json);
        }
    }

    public void PasteSelection()
    {
        if (Clipboard.ContainsText())
        {
            try
            {
                var json = Clipboard.GetText();
                if (json.TrimStart().StartsWith("[")) // Basic check
                {
                    var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
                    options.Converters.Add(new ColorJsonConverter());
                    var dtos = JsonSerializer.Deserialize<List<LaserObjectDto>>(json, options);
                    
                    if (dtos != null && dtos.Count > 0)
                    {
                        var newObjects = new List<LaserObject>();
                        foreach (var dto in dtos)
                        {
                            var obj = ProjectSerializer.FromDto(dto);
                            if (obj != null)
                            {
                                obj.Id = Guid.NewGuid();
                                obj.Position = new PointF(obj.Position.X + 10, obj.Position.Y + 10);
                                if (obj.Name != null) obj.Name += " (Copy)";
                                newObjects.Add(obj);
                            }
                        }
                        
                        if (newObjects.Count > 0)
                        {
                            var cmd = new AddObjectCommand(newObjects);
                            CommandManager.Instance.Execute(cmd);
                            
                            ProjectState.Instance.SelectedObjects = newObjects;
                            _workbench.Invalidate();
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Paste failed: {ex.Message}");
            }
        }
    }

    public void DeleteSelection()
    {
        var sel = ProjectState.Instance.SelectedObjects;
        if (sel.Count > 0)
        {
            var cmd = new RemoveObjectCommand(sel);
            CommandManager.Instance.Execute(cmd);
            
            ProjectState.Instance.SelectedObjects = new List<LaserObject>();
            _workbench.Invalidate();
        }
    }

    public void GroupSelection()
    {
        var sel = ProjectState.Instance.SelectedObjects;
        if (sel.Count > 1) CommandManager.Instance.Execute(new GroupCommand(sel));
    }

    public void UngroupSelection()
    {
        var sel = ProjectState.Instance.SelectedObjects;
        if (sel.Any(o => o is LaserGroup)) CommandManager.Instance.Execute(new UngroupCommand(sel));
    }

    public void MaskSelectedImage()
    {
        var sel = ProjectState.Instance.SelectedObjects;
        if (sel.Count != 2) 
        {
            MessageBox.Show("Please select exactly one Image and one Shape (Circle/Rectangle) to create a mask.", "Invalid Selection", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }
        
        var obj1 = sel[0];
        var obj2 = sel[1];
        
        LaserImage? img = obj1 as LaserImage ?? obj2 as LaserImage;
        LaserObject? shape = (obj1 is LaserCircle || obj1 is LaserRectangle) ? obj1 :
                             (obj2 is LaserCircle || obj2 is LaserRectangle) ? obj2 : null;
                             
        if (img != null && shape != null && img != shape)
        {
             if (img.MaskId == shape.Id)
             {
                 img.MaskId = Guid.Empty;
             }
             else
             {
                 img.MaskId = shape.Id;
             }
             _workbench.Invalidate();
        }
        else
        {
            MessageBox.Show("Selection must include one Image and one Shape.", "Invalid Selection", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
    }

    public void UnmaskSelectedImage()
    {
        var sel = ProjectState.Instance.SelectedObjects;
        var images = sel.OfType<LaserImage>().Where(i => i.MaskId != Guid.Empty).ToList();
        if (images.Count > 0)
        {
            foreach (var img in images)
            {
                img.MaskId = Guid.Empty;
            }
            _workbench.Invalidate();
        }
    }

    public void AttachSelectedTextToPath()
    {
        var sel = ProjectState.Instance.SelectedObjects;
        if (sel.Count == 2)
        {
            var txt = sel.OfType<LaserText>().FirstOrDefault();
            var path = sel.FirstOrDefault(o => o is LaserPath || o is LaserBezier || o is LaserCircle || o is LaserRectangle);
            if (txt != null && path != null && txt != path)
            {
                txt.PathId = path.Id;
                // Auto-calculate offset based on text position
                txt.PathOffset = PathWarp.GetClosestOffset(path, txt.Position);
                _workbench.Invalidate();
            }
        }
    }

    public void DetachSelectedTextFromPath()
    {
        var sel = ProjectState.Instance.SelectedObjects;
        foreach (var txt in sel.OfType<LaserText>())
        {
            txt.PathId = Guid.Empty;
        }
        _workbench.Invalidate();
    }


    public bool UpdateSelectedObjects(bool updateListSelection = true)
    {
        var sel = ProjectState.Instance.SelectedObjects;
        
        _isUpdatingUI = true;
        if (sel.Count == 1)
        {
            var obj = sel[0];
            _nudPosX.Enabled = true;
            _nudPosY.Enabled = true;
            _nudSizeW.Enabled = true;
            _nudSizeH.Enabled = true;
            _nudRotation.Enabled = true; 

            _nudPosX.Value = (decimal)obj.Position.X;
            _nudPosY.Value = (decimal)obj.Position.Y;
            _nudSizeW.Value = (decimal)obj.Size.Width;
            _nudSizeH.Value = (decimal)obj.Size.Height;
            _nudRotation.Value = (decimal)obj.Rotation; 
            
            // Text Toolbar
            if (obj is LaserText txt)
            {
                _txtContent.Enabled = true;
                _cmbFont.Enabled = true;
                _nudFontSize.Enabled = true;
                
                _txtContent.Text = txt.Text;
                if (_cmbFont.Items.Contains(txt.FontName))
                    _cmbFont.SelectedItem = txt.FontName;
                else if (_cmbFont.Items.Count > 0)
                     _cmbFont.SelectedIndex = 0; 
                    
                _nudFontSize.Value = (decimal)txt.FontSize;
                _btnBold.Enabled = true;
                _btnItalic.Enabled = true;
                _btnBold.Checked = txt.FontStyle.HasFlag(FontStyle.Bold);
                _btnItalic.Checked = txt.FontStyle.HasFlag(FontStyle.Italic);

                // Path Controls 
                _nudVerticalOffset.Enabled = true;
                _chkReversePath.Enabled = true;
                _chkUpsideDown.Enabled = true;
                _nudVerticalOffset.Value = (decimal)txt.VerticalOffset;
                _chkReversePath.Checked = txt.ReversePath;
                _chkUpsideDown.Checked = txt.UpsideDown;
                _cmbWarpMethod.Enabled = true;
                _cmbWarpMethod.SelectedIndex = (int)txt.WarpMethod;

                if (txt.PathId != Guid.Empty)
                {
                    _trkPathOffset.Enabled = true;
                    var pathObj = ProjectState.Instance.Objects.FirstOrDefault(o => o.Id == txt.PathId);
                    if (pathObj != null)
                    {
                        var backbone = PathWarp.FlattenPath(pathObj);
                        float totalLen = 0;
                        for (int i = 0; i < backbone.Count - 1; i++)
                        {
                            float dx = backbone[i+1].X - backbone[i].X;
                            float dy = backbone[i+1].Y - backbone[i].Y;
                            totalLen += (float)Math.Sqrt(dx*dx + dy*dy);
                        }
                        
                        _trkPathOffset.Maximum = (int)(totalLen * 10); // 0.1mm precision
                        int val = (int)(txt.PathOffset * 10);
                        if (val < 0) val = 0;
                        if (val > _trkPathOffset.Maximum) val = _trkPathOffset.Maximum;
                        _trkPathOffset.Value = val;
                    }
                }
                else
                {
                    _trkPathOffset.Enabled = false;
                    _trkPathOffset.Value = 0;
                }
            }
            else
            {
                _txtContent.Enabled = false;
                _cmbFont.Enabled = false;
                _nudFontSize.Enabled = false;
                _btnBold.Enabled = false;
                _btnItalic.Enabled = false;
                _btnBold.Checked = false;
                _btnItalic.Checked = false;
                _trkPathOffset.Enabled = false;
                _nudVerticalOffset.Enabled = false;
                _chkReversePath.Enabled = false;
                _chkUpsideDown.Enabled = false;
                _cmbWarpMethod.Enabled = false;
                _cmbWarpMethod.SelectedIndex = -1;
                _txtContent.Text = "";
            }

            // Update Layer Info Label
            var layer = ProjectState.Instance.Layers.FirstOrDefault(l => l.Id == obj.LayerId);
            if (layer != null)
            {
                _lblLayerInfo.Text = $"{layer.Name} (S: {layer.Speed})";
            }
            else
            {
                _lblLayerInfo.Text = "No Layer";
            }
        }
        else
        {
            _nudPosX.Enabled = false;
            _nudPosY.Enabled = false;
            _nudSizeW.Enabled = false;
            _nudSizeH.Enabled = false;
            _nudRotation.Enabled = false; 
            
            _txtContent.Enabled = false;
            _cmbFont.Enabled = false;
            _nudFontSize.Enabled = false;
            _btnBold.Enabled = false;
            _btnItalic.Enabled = false;
            _btnBold.Checked = false;
            _btnItalic.Checked = false;
            
            _nudPosX.Value = 0;
            _nudPosY.Value = 0;
            _nudSizeW.Value = 0;
            _nudSizeH.Value = 0;
            _txtContent.Text = "";

            _lblLayerInfo.Text = "-";
        }
        _isUpdatingUI = false;
        
        _isUpdatingSelection = true;
        
        if (updateListSelection)
        {
            var currentSet = new HashSet<LaserObject>(ProjectState.Instance.SelectedObjects);
            
            // Update Object List Selection
            foreach (DataGridViewRow row in _objectList.Rows)
            {
                if (row.DataBoundItem is LaserObject obj)
                {
                    bool shouldSelect = currentSet.Contains(obj);
                    if (row.Selected != shouldSelect)
                    {
                        row.Selected = shouldSelect;
                    }
                }
            }
        }
        _isUpdatingSelection = false;

        return true;
    }

    private void ExportSvg()
    {
        var selectedCount = ProjectState.Instance.SelectedObjects.Count;
        List<LaserObject> objects;

        if (selectedCount > 0)
        {
            var result = MessageBox.Show(
                $"Export {selectedCount} selected object(s) to SVG?\n\nClick Yes to export selection only.\nClick No to export all objects.",
                "Export SVG",
                MessageBoxButtons.YesNoCancel,
                MessageBoxIcon.Question);

            if (result == DialogResult.Cancel)
                return;

            objects = result == DialogResult.Yes
                ? new List<LaserObject>(ProjectState.Instance.SelectedObjects)
                : ProjectState.Instance.Objects.ToList();
        }
        else
        {
            objects = ProjectState.Instance.Objects.ToList();
        }

        if (objects.Count == 0)
        {
            MessageBox.Show("No objects to export.", "Export SVG",
                MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        using var sfd = new SaveFileDialog
        {
            Filter = "SVG File|*.svg",
            DefaultExt = ".svg",
            Title = $"Export {objects.Count} object(s) to SVG"
        };

        if (sfd.ShowDialog() == DialogResult.OK)
        {
            try
            {
                SvgExporter.Export(objects, sfd.FileName);
                MessageBox.Show($"Exported {objects.Count} object(s) successfully.",
                    "Export SVG", MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Export failed: {ex.Message}", "Error",
                    MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }
    }
}
