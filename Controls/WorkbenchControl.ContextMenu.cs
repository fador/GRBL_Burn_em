using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Linq;
using System.Windows.Forms;
using grbl_burn_em.Data;
using grbl_burn_em.Data.Commands;

namespace grbl_burn_em.Controls
{
    public partial class WorkbenchControl
    {


        private void ShowContextMenu(Point screenPos)
        {
            var menu = new ContextMenuStrip();
            bool hasSelection = ProjectState.Instance.SelectedObjects.Count > 0;
            var selection = ProjectState.Instance.SelectedObjects;

            // 1. Edit Operations
            if (hasSelection)
            {
                menu.Items.Add("Copy", null, (s, e) => ExecuteCopy());
            }
            // Paste available if clipboard has text (JSON)
            if (Clipboard.ContainsText())
            {
                menu.Items.Add("Paste", null, (s, e) => ExecutePaste());
            }

            // 2. Modifiers
            if (hasSelection)
            {
                menu.Items.Add(new ToolStripSeparator());
                menu.Items.Add("Array Modifier...", null, (s, e) => ExecuteArrayModifier());
            }

            // 3. Grouping
            if (hasSelection)
            {
                if (selection.Count > 1)
                {
                    menu.Items.Add("Group", null, (s, e) => ExecuteGroup());
                }
                
                if (selection.Any(o => o is LaserGroup))
                {
                    menu.Items.Add("Ungroup", null, (s, e) => ExecuteUngroup());
                }
            }

            // 4. Text Operations
            if (hasSelection)
            {
                var texts = selection.OfType<LaserText>().ToList();
                if (texts.Count > 0)
                {
                    menu.Items.Add(new ToolStripSeparator());
                    
                    // Attach Logic: 1 Text + 1 Shape
                    if (selection.Count == 2)
                    {
                        var shape = selection.FirstOrDefault(o => o != texts[0] && (o is LaserPath || o is LaserBezier || o is LaserCircle || o is LaserRectangle));
                        if (shape != null)
                        {
                            menu.Items.Add("Attach to Path", null, (s, e) => ExecuteAttachToPath());
                        }
                    }

                    if (texts.Any(t => t.PathId != Guid.Empty))
                    {
                        menu.Items.Add("Detach from Path", null, (s, e) => ExecuteDetachFromPath());
                    }
                }
            }
            
            // 5. Image Masking
            // Require 1 Image + 1 Shape (Circle/Rect/Path)
            if (hasSelection && selection.Count == 2)
            {
                var img = selection.OfType<LaserImage>().FirstOrDefault();
                var shape = selection.FirstOrDefault(o => o != img && (o is LaserCircle || o is LaserRectangle || o is LaserPath));
                
                if (img != null && shape != null)
                {
                    menu.Items.Add(new ToolStripSeparator());
                    menu.Items.Add("Mask Image", null, (s, e) => ExecuteMask());
                }
            }
            
            if (selection.OfType<LaserImage>().Any(i => i.MaskId != Guid.Empty))
            {
                 menu.Items.Add("Unmask Image", null, (s, e) => ExecuteUnmask());
            }

            if (menu.Items.Count > 0)
            {
                menu.Show(this, screenPos);
            }
        }

        private void ExecuteCopy() => MainForm.Instance.CopySelection();

        private void ExecutePaste() => MainForm.Instance.PasteSelection();

        private void ExecuteArrayModifier() => MainForm.Instance.ShowArrayModifierDialog();

        private void ExecuteGroup() => MainForm.Instance.GroupSelection();

        private void ExecuteUngroup() => MainForm.Instance.UngroupSelection();

        private void ExecuteDetachFromPath() => MainForm.Instance.DetachSelectedTextFromPath();

        private void ExecuteAttachToPath() => MainForm.Instance.AttachSelectedTextToPath();

        private void ExecuteMask() => MainForm.Instance.MaskSelectedImage();

        private void ExecuteUnmask() => MainForm.Instance.UnmaskSelectedImage();
    }
}
