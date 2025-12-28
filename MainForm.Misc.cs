using grbl_burn_em.Data.Generators;
using grbl_burn_em.Data;

namespace grbl_burn_em;

public partial class MainForm
{
    public List<string> GetRegisteredGeneratorNames()
    {
        var names = new List<string> { "Grbl", "GCode", "Dummy" };
        foreach (var gen in _gcodeGenerators)
        {
            if (!names.Contains(gen.Name))
                names.Add(gen.Name);
        }
        return names;
    }

    public void RegisterGCodeGenerator(IGCodeGenerator generator)
    {
        _gcodeGenerators.Add(generator);
    }

    public void AddMenuItem(string menuPath, string menuItemName, Action action)
    {
        if (MainMenuStrip == null) return;

        var pathParts = menuPath.Split('/', StringSplitOptions.RemoveEmptyEntries);
        ToolStripItemCollection currentItems = MainMenuStrip.Items;
        ToolStripMenuItem? currentParent = null;

        foreach (var part in pathParts)
        {
            var found = currentItems.Cast<ToolStripItem>().OfType<ToolStripMenuItem>().FirstOrDefault(i => i.Text == part);
            if (found == null)
            {
                found = new ToolStripMenuItem(part);
                currentItems.Add(found);
            }
            currentParent = found;
            currentItems = found.DropDownItems;
        }

        var newItem = new ToolStripMenuItem(menuItemName, null, (s, e) => action());
        currentItems.Add(newItem);
    }

    public void AddContextMenuItem(string text, Action<LaserObject> action)
    {
        _pluginContextActions.Add((text, action));
    }

    public void RefreshObjectList()
    {
        _objectList?.Refresh();
        UpdateSelectedObjects();
    }

    public void InvalidateWorkbench()
    {
        _workbench?.Invalidate();
    }

    public static Bitmap ResizeImage(Image image, int width, int height)
    {
        var destRect = new Rectangle(0, 0, width, height);
        var destImage = new Bitmap(width, height);

        destImage.SetResolution(image.HorizontalResolution, image.VerticalResolution);

        using (var graphics = Graphics.FromImage(destImage))
        {
            graphics.CompositingMode = System.Drawing.Drawing2D.CompositingMode.SourceCopy;
            graphics.CompositingQuality = System.Drawing.Drawing2D.CompositingQuality.HighQuality;
            graphics.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic;
            graphics.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.HighQuality;
            graphics.PixelOffsetMode = System.Drawing.Drawing2D.PixelOffsetMode.HighQuality;

            using (var wrapMode = new System.Drawing.Imaging.ImageAttributes())
            {
                wrapMode.SetWrapMode(System.Drawing.Drawing2D.WrapMode.TileFlipXY);
                graphics.DrawImage(image, destRect, 0, 0, image.Width, image.Height, GraphicsUnit.Pixel, wrapMode);
            }
        }

        return destImage;
    }
}
