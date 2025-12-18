using Xunit;
using laser_gui_test.Data;
using System.Drawing;
using System.IO;
using System.Collections.Generic;
using System.Linq;
using System;

namespace laser_gui_test.Tests;

public class ProjectSerializerTests : IDisposable
{
    private readonly string _tempPath;

    public ProjectSerializerTests()
    {
        _tempPath = Path.Combine(Path.GetTempPath(), "laser_test_project_" + Guid.NewGuid().ToString() + ".json");
    }

    public void Dispose()
    {
        if (File.Exists(_tempPath)) File.Delete(_tempPath);
    }

    [Fact]
    public void TestProjectRoundTrip()
    {
        // Setup ProjectState
        ProjectState.Instance.Objects.Clear();
        ProjectState.Instance.Layers.Clear();

        var layer1 = new Layer("Layer 1", Color.Red) { Power = 50, Speed = 1000, Mode = LayerMode.Cut };
        ProjectState.Instance.Layers.Add(layer1);
        ProjectState.Instance.ActiveLayer = layer1;

        var rect = new LaserRectangle
        {
            Name = "Test Rect",
            Position = new PointF(10, 10),
            Size = new SizeF(20, 20),
            LayerId = layer1.Id,
            IsEnabled = true
        };
        ProjectState.Instance.AddObject(rect);

        var text = new LaserText
        {
            Name = "Test Text",
            Text = "Hello World",
            Position = new PointF(0, 0),
            FontSize = 12,
            LayerId = layer1.Id,
            IsEnabled = true
        };
        ProjectState.Instance.AddObject(text);

        // Save
        ProjectSerializer.Save(_tempPath);
        Assert.True(File.Exists(_tempPath));

        // Clear and Load
        ProjectState.Instance.Objects.Clear();
        ProjectState.Instance.Layers.Clear();
        
        ProjectSerializer.Load(_tempPath);

        // Verify
        Assert.Single(ProjectState.Instance.Layers);
        Assert.Equal("Layer 1", ProjectState.Instance.Layers[0].Name);
        Assert.Equal(Color.Red.ToArgb(), ProjectState.Instance.Layers[0].Color.ToArgb());
        Assert.Equal(LayerMode.Cut, ProjectState.Instance.Layers[0].Mode);

        Assert.Equal(2, ProjectState.Instance.Objects.Count);
        
        var loadedRect = ProjectState.Instance.Objects.OfType<LaserRectangle>().FirstOrDefault();
        Assert.NotNull(loadedRect);
        Assert.Equal("Test Rect", loadedRect.Name);
        Assert.Equal(10, loadedRect.Position.X);
        Assert.Equal(20, loadedRect.Size.Width);

        var loadedText = ProjectState.Instance.Objects.OfType<LaserText>().FirstOrDefault();
        Assert.NotNull(loadedText);
        Assert.Equal("Test Text", loadedText.Name);
        Assert.Equal("Hello World", loadedText.Text);
        Assert.Equal(12, loadedText.FontSize);
    }

    [Fact]
    public void TestImageEncodingDecoding()
    {
        ProjectState.Instance.Objects.Clear();
        ProjectState.Instance.Layers.Clear();
        
        // Ensure configuration is set to embed images
        AppConfiguration.Instance.EmbedImagesInProject = true;

        using var bmp = new Bitmap(10, 10);
        using (var g = Graphics.FromImage(bmp))
        {
            g.Clear(Color.Blue);
        }

        var img = new LaserImage
        {
            Name = "Test Image",
            Image = bmp,
            Position = new PointF(0, 0),
            Size = new SizeF(10, 10)
        };
        ProjectState.Instance.AddObject(img);

        // Save
        ProjectSerializer.Save(_tempPath);

        // Clear and Load
        ProjectState.Instance.Objects.Clear();
        ProjectSerializer.Load(_tempPath);

        // Verify
        var loadedImg = ProjectState.Instance.Objects.OfType<LaserImage>().FirstOrDefault();
        Assert.NotNull(loadedImg);
        Assert.NotNull(loadedImg.Image);
        Assert.Equal(10, loadedImg.Image.Width);
        Assert.Equal(Color.Blue.ToArgb(), loadedImg.Image.GetPixel(5, 5).ToArgb());
    }
}
