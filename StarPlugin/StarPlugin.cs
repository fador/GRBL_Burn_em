using System;
using System.Drawing;
using System.Collections.Generic;
using System.Linq;
using System.Windows.Forms;
using grbl_burn_em.Data;
using grbl_burn_em.Data.Generators;

namespace StarPlugin;

public class StarPlugin : IPlugin
{
    public string Name => "Star Plugin (example plugin)";
    public string Version => "1.0";
    public string Author => "Fador";

    public void Initialize(IPluginHost host)
    {
        host.RegisterMenuItem("Insert/Shapes", "Star", () => CreateStar(host));
        host.RegisterContextMenuAction("Log Info", (obj) => MessageBox.Show($"Object: {obj.Name} ({obj.Type})\nPos: {obj.Position}"));
        host.RegisterGCodeGenerator(new StarGCodeGenerator());
    }

    private void CreateStar(IPluginHost host)
    {
        // Simple star generator
        var path = new LaserPath();
        path.Name = "Star";
        
        float cx = 100, cy = 100;
        float outerRadius = 50;
        float innerRadius = 20;
        int points = 5;

        for (int i = 0; i < points * 2; i++)
        {
            float radius = (i % 2 == 0) ? outerRadius : innerRadius;
            float angle = (float)(i * Math.PI / points);
            // Rotate -90 deg to start top
            angle -= (float)(Math.PI / 2);
            
            float x = cx + radius * (float)Math.Cos(angle);
            float y = cy + radius * (float)Math.Sin(angle);
            path.Points.Add(new PointF(x, y));
        }
        path.Points.Add(path.Points[0]); // Close loop

        path.UpdateBounds();
        host.AddObject(path);
    }
}

public class StarGCodeGenerator : IGCodeGenerator
{
    public string Name => "StarCode";

    public IEnumerable<string> Generate(IEnumerable<LaserObject> objects)
    {
        var list = new List<string>();
        list.Add("; StarCode Generator v1.0");
        list.Add("G21");
        list.Add("G90");
        
        foreach(var obj in objects)
        {
            list.Add($"; Processing {obj.Name}");
            // Dummy implementation
            list.Add($"G0 X{obj.Position.X} Y{obj.Position.Y}");
        }
        
        list.Add("M2");
        return list;
    }
}
