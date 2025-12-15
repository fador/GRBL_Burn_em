using System.Text.Json;
using System.Text.Json.Serialization;

namespace laser_gui_test.Data;

public class ProjectDataDto
{
    public List<LaserObjectDto> Objects { get; set; } = new();
    public List<Layer> Layers { get; set; } = new();
}

[JsonDerivedType(typeof(LaserPathDto), typeDiscriminator: "Path")]
[JsonDerivedType(typeof(LaserRectangleDto), typeDiscriminator: "Rectangle")]
[JsonDerivedType(typeof(LaserImageDto), typeDiscriminator: "Image")]
[JsonDerivedType(typeof(LaserTextDto), typeDiscriminator: "Text")]
public abstract class LaserObjectDto
{
    public Guid Id { get; set; }
    public string Name { get; set; } = "";
    public Guid LayerId { get; set; }
    public bool IsEnabled { get; set; }
    public float Power { get; set; }
    public float Speed { get; set; }
    public PointF Position { get; set; }
    public float Rotation { get; set; }
    public SizeF Size { get; set; }
}

public class LaserPathDto : LaserObjectDto
{
    public List<PointF> Points { get; set; } = new();
}

public class LaserRectangleDto : LaserObjectDto { }

public class LaserImageDto : LaserObjectDto
{
    public string ImagePath { get; set; } = "";
}

public class LaserTextDto : LaserObjectDto
{
    public string Text { get; set; } = "";
    public string FontName { get; set; } = "Arial";
    public float FontSize { get; set; }
}

public static class ProjectSerializer
{
    public static void Save(string path)
    {
        var dto = new ProjectDataDto();
        dto.Layers = ProjectState.Instance.Layers.ToList();
        
        foreach (var obj in ProjectState.Instance.Objects)
        {
            if (obj is LaserPath p)
            {
                dto.Objects.Add(new LaserPathDto 
                { 
                    Id = p.Id, Name = p.Name, LayerId = p.LayerId, IsEnabled = p.IsEnabled,
                    Power = p.Power, Speed = p.Speed, Position = p.Position, Rotation = p.Rotation, Size = p.Size,
                    Points = p.Points
                });
            }
            else if (obj is LaserRectangle r)
            {
                dto.Objects.Add(new LaserRectangleDto
                {
                    Id = r.Id, Name = r.Name, LayerId = r.LayerId, IsEnabled = r.IsEnabled,
                    Power = r.Power, Speed = r.Speed, Position = r.Position, Rotation = r.Rotation, Size = r.Size
                });
            }
            else if (obj is LaserImage i)
            {
                dto.Objects.Add(new LaserImageDto
                {
                    Id = i.Id, Name = i.Name, LayerId = i.LayerId, IsEnabled = i.IsEnabled,
                    Power = i.Power, Speed = i.Speed, Position = i.Position, Rotation = i.Rotation, Size = i.Size,
                    ImagePath = i.ImagePath
                });
            }
            else if (obj is LaserText t)
            {
                dto.Objects.Add(new LaserTextDto
                {
                    Id = t.Id, Name = t.Name, LayerId = t.LayerId, IsEnabled = t.IsEnabled,
                    Power = t.Power, Speed = t.Speed, Position = t.Position, Rotation = t.Rotation, Size = t.Size,
                    Text = t.Text, FontName = t.FontName, FontSize = t.FontSize
                });
            }
        }

        var options = new JsonSerializerOptions { WriteIndented = true };
        options.Converters.Add(new ColorJsonConverter());
        var json = JsonSerializer.Serialize(dto, options);
        File.WriteAllText(path, json);
    }

    public static void Load(string path)
    {
        if (!File.Exists(path)) return;
        var json = File.ReadAllText(path);
        var options = new JsonSerializerOptions();
        options.Converters.Add(new ColorJsonConverter());
        var dto = JsonSerializer.Deserialize<ProjectDataDto>(json, options);
        
        if (dto == null) return;

        ProjectState.Instance.Objects.Clear();
        ProjectState.Instance.Layers.Clear();

        foreach (var l in dto.Layers) ProjectState.Instance.Layers.Add(l);
        if (ProjectState.Instance.Layers.Count == 0) ProjectState.Instance.Layers.Add(new Layer("Default", Color.Black));
        ProjectState.Instance.ActiveLayer = ProjectState.Instance.Layers[0];

        foreach (var objDto in dto.Objects)
        {
            LaserObject? obj = null;
            if (objDto is LaserPathDto p)
            {
                var pathObj = new LaserPath();
                pathObj.Points = p.Points;
                obj = pathObj;
            }
            else if (objDto is LaserRectangleDto r)
            {
                obj = new LaserRectangle();
            }
            else if (objDto is LaserImageDto i)
            {
                var imgObj = new LaserImage { ImagePath = i.ImagePath };
                if (File.Exists(i.ImagePath))
                {
                    try { imgObj.Image = new Bitmap(i.ImagePath); } catch {}
                }
                obj = imgObj;
            }
            else if (objDto is LaserTextDto t)
            {
                obj = new LaserText
                {
                    Text = t.Text,
                    FontName = t.FontName,
                    FontSize = t.FontSize
                };
            }

            if (obj != null)
            {
                obj.Id = objDto.Id;
                obj.Name = objDto.Name;
                obj.LayerId = objDto.LayerId;
                obj.IsEnabled = objDto.IsEnabled;
                obj.Power = objDto.Power;
                obj.Speed = objDto.Speed;
                obj.Position = objDto.Position;
                obj.Rotation = objDto.Rotation;
                obj.Size = objDto.Size;
                ProjectState.Instance.AddObject(obj);
            }
        }
    }
}

public class ColorJsonConverter : JsonConverter<System.Drawing.Color>
{
    public override System.Drawing.Color Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        // Simple int ARGB or string name support?
        // Let's assume we write as specific struct
        
        if (reader.TokenType == JsonTokenType.StartObject)
        {
            int a = 255, r = 0, g = 0, b = 0;
            string? name = null;
            
            while (reader.Read())
            {
                if (reader.TokenType == JsonTokenType.EndObject) break;
                
                if (reader.TokenType == JsonTokenType.PropertyName)
                {
                    string? prop = reader.GetString();
                    reader.Read();
                    
                    switch(prop)
                    {
                        case "A": a = reader.GetInt32(); break;
                        case "R": r = reader.GetInt32(); break;
                        case "G": g = reader.GetInt32(); break;
                        case "B": b = reader.GetInt32(); break;
                        case "Name": name = reader.GetString(); break;
                    }
                }
            }
            
            if (!string.IsNullOrEmpty(name) && name != "0") // "0" is default for unnamed?
            {
                // Try known color
                var k = Color.FromName(name);
                if (k.IsKnownColor) return k;
            }
            return Color.FromArgb(a, r, g, b);
        }
        return Color.Black;
    }

    public override void Write(Utf8JsonWriter writer, System.Drawing.Color value, JsonSerializerOptions options)
    {
        writer.WriteStartObject();
        writer.WriteNumber("A", value.A);
        writer.WriteNumber("R", value.R);
        writer.WriteNumber("G", value.G);
        writer.WriteNumber("B", value.B);
        if (value.IsKnownColor) writer.WriteString("Name", value.Name);
        writer.WriteEndObject();
    }
}
