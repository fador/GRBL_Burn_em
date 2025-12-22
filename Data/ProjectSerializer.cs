/*
 * Copyright (c) 2025 Marko Viitanen (Fador) and the project contributors. All rights reserved.
 * Licensed under the MIT License. See LICENSE file in the project root for full license information.
 *
 * This file is part of the GRBL Burn'Em laser control software
 */
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Drawing;
using System.IO;

namespace grbl_burn_em.Data;

public class LayerDto
{
    public Guid Id { get; set; }
    public string Name { get; set; } = "Layer";
    public System.Drawing.Color Color { get; set; } = System.Drawing.Color.Black;
    public bool IsVisible { get; set; } = true;
    public bool IsLocked { get; set; } = false;
    public float Power { get; set; }
    public float Speed { get; set; }
    public LayerMode Mode { get; set; }
}

public class ProjectDataDto
{
    public List<LaserObjectDto> Objects { get; set; } = new();
    public List<LayerDto> Layers { get; set; } = new();
    public List<string> ImageLibrary { get; set; } = new();
}

[JsonDerivedType(typeof(LaserPathDto), typeDiscriminator: "Path")]
[JsonDerivedType(typeof(LaserRectangleDto), typeDiscriminator: "Rectangle")]
[JsonDerivedType(typeof(LaserImageDto), typeDiscriminator: "Image")]
[JsonDerivedType(typeof(LaserTextDto), typeDiscriminator: "Text")]
[JsonDerivedType(typeof(LaserCircleDto), typeDiscriminator: "Circle")]
[JsonDerivedType(typeof(LaserBezierDto), typeDiscriminator: "Bezier")]
public abstract class LaserObjectDto
{
    public Guid Id { get; set; }
    public string Name { get; set; } = "";
    public Guid LayerId { get; set; }
    public bool IsEnabled { get; set; }
    public float? Power { get; set; }

    public float? Speed { get; set; }
    public LayerMode? Mode { get; set; }
    public PointF Position { get; set; }
    public float Rotation { get; set; }
    public SizeF Size { get; set; }
}

// ...

public class LaserCircleDto : LaserObjectDto { }

public class LaserPathDto : LaserObjectDto
{
    public List<PointF> Points { get; set; } = new();
}

public class LaserRectangleDto : LaserObjectDto { }

public class LaserImageDto : LaserObjectDto
{
    public string ImagePath { get; set; } = "";
    public string Base64Data { get; set; } = "";
    public int? ImageLibraryIndex { get; set; }
    public Guid MaskId { get; set; }
}

public class LaserTextDto : LaserObjectDto
{
    public string Text { get; set; } = "";
    public string FontName { get; set; } = "Arial";
    public float FontSize { get; set; }
    public Guid PathId { get; set; }
    public float PathOffset { get; set; }
    public float VerticalOffset { get; set; }
    public bool ReversePath { get; set; }
    public bool UpsideDown { get; set; }
    public FontStyle FontStyle { get; set; }
}

public class LaserBezierDto : LaserObjectDto
{
    public List<PointF> Points { get; set; } = new();
}


public static class ProjectSerializer
{
    public static LaserObjectDto ToDto(LaserObject obj)
    {
        LaserObjectDto dto = null!;
        if (obj is LaserPath p)
        {
            dto = new LaserPathDto { Points = p.Points };
        }
        else if (obj is LaserRectangle r)
        {
            dto = new LaserRectangleDto();
        }
        else if (obj is LaserImage i)
        {
            var imgDto = new LaserImageDto
            {
                ImagePath = i.ImagePath,
                MaskId = i.MaskId
            };
            
            if (AppConfiguration.Instance.EmbedImagesInProject)
            {
                string? base64 = null;
                try
                {
                    if (File.Exists(i.ImagePath))
                    {
                        var bytes = File.ReadAllBytes(i.ImagePath);
                        base64 = Convert.ToBase64String(bytes);
                    }
                    else if (i.Image != null)
                    {
                        using var ms = new MemoryStream();
                        i.Image.Save(ms, System.Drawing.Imaging.ImageFormat.Png);
                        base64 = Convert.ToBase64String(ms.ToArray());
                    }
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"Failed to embed image: {ex.Message}");
                }

                if (base64 != null)
                {
                    imgDto.Base64Data = base64;
                }
            }
            dto = imgDto;
        }
        else if (obj is LaserText t)
        {
            dto = new LaserTextDto
            {
                Text = t.Text, FontName = t.FontName, FontSize = t.FontSize, PathId = t.PathId, PathOffset = t.PathOffset,
                VerticalOffset = t.VerticalOffset, ReversePath = t.ReversePath, UpsideDown = t.UpsideDown, FontStyle = t.FontStyle
            };
        }
        else if (obj is LaserCircle c)
        {
            dto = new LaserCircleDto();
        }
        else if (obj is LaserBezier b)
        {
            dto = new LaserBezierDto { Points = b.Points };
        }

        if (dto != null)
        {
            dto.Id = obj.Id;
            dto.Name = obj.Name;
            dto.LayerId = obj.LayerId;
            dto.IsEnabled = obj.IsEnabled;
            dto.Power = obj.Power;

            dto.Speed = obj.Speed;
            dto.Mode = obj.Mode;
            dto.Position = obj.Position;
            dto.Rotation = obj.Rotation;
            dto.Size = obj.Size;
        }
        return dto!;
    }

    public static LaserObject? FromDto(LaserObjectDto objDto, List<string>? imageLibrary = null)
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
            var imgObj = new LaserImage { ImagePath = i.ImagePath, MaskId = i.MaskId };
            
            string? base64Data = null;
            if (i.ImageLibraryIndex.HasValue && imageLibrary != null && i.ImageLibraryIndex.Value < imageLibrary.Count)
            {
                base64Data = imageLibrary[i.ImageLibraryIndex.Value];
            }
            else if (!string.IsNullOrEmpty(i.Base64Data))
            {
                base64Data = i.Base64Data;
            }

            if (!string.IsNullOrEmpty(base64Data))
            {
                try
                {
                    var bytes = Convert.FromBase64String(base64Data);
                    using var ms = new MemoryStream(bytes);
                    using var temp = new Bitmap(ms);
                    imgObj.Image = new Bitmap(temp);
                }
                catch (Exception ex)
                {
                     System.Diagnostics.Debug.WriteLine($"Failed to load embedded image: {ex.Message}");
                }
            }
            
            if (imgObj.Image == null && File.Exists(i.ImagePath))
            {
                try 
                { 
                    using var fs = new FileStream(i.ImagePath, FileMode.Open, FileAccess.Read);
                    var temp = new Bitmap(fs); 
                    imgObj.Image = new Bitmap(temp);
                } catch {}
            }
            obj = imgObj;
        }
        else if (objDto is LaserTextDto t)
        {
            obj = new LaserText
            {
                Text = t.Text,
                FontName = t.FontName,
                FontSize = t.FontSize,
                PathId = t.PathId,
                PathOffset = t.PathOffset,
                VerticalOffset = t.VerticalOffset,
                ReversePath = t.ReversePath,
                UpsideDown = t.UpsideDown,
                FontStyle = t.FontStyle
            };
        }
        else if (objDto is LaserCircleDto)
        {
            obj = new LaserCircle();
        }
        else if (objDto is LaserBezierDto bDto)
        {
            obj = new LaserBezier
            {
                Points = bDto.Points ?? new List<PointF>()
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
            obj.Mode = objDto.Mode;
            obj.Position = objDto.Position;
            obj.Rotation = objDto.Rotation;
            obj.Size = objDto.Size;
        }
        return obj;
    }

    public static void Save(string path)
    {
        var dto = new ProjectDataDto();
        dto.Layers = ProjectState.Instance.Layers.Select(l => new LayerDto 
        {
            Id = l.Id,
            Name = l.Name,
            Color = l.Color,
            IsVisible = l.IsVisible,
            IsLocked = l.IsLocked,
            Power = l.Power,
            Speed = l.Speed,
            Mode = l.Mode
        }).ToList();
        
        foreach (var obj in ProjectState.Instance.Objects)
        {
            var objDto = ToDto(obj);
            if (objDto is LaserImageDto imgDto && !string.IsNullOrEmpty(imgDto.Base64Data))
            {
                int index = dto.ImageLibrary.IndexOf(imgDto.Base64Data);
                if (index == -1)
                {
                    index = dto.ImageLibrary.Count;
                    dto.ImageLibrary.Add(imgDto.Base64Data);
                }
                imgDto.ImageLibraryIndex = index;
                imgDto.Base64Data = ""; 
            }
            
            if (objDto != null) dto.Objects.Add(objDto);
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
        var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
        options.Converters.Add(new ColorJsonConverter());
        var dto = JsonSerializer.Deserialize<ProjectDataDto>(json, options);
        
        if (dto == null) return;
        
        ProjectState.Instance.Objects.Clear();
        ProjectState.Instance.Layers.Clear();

        foreach (var lDto in dto.Layers) 
        {
            var l = new Layer(lDto.Name, lDto.Color)
            {
                Id = lDto.Id,
                IsVisible = lDto.IsVisible,
                IsLocked = lDto.IsLocked,
                Power = lDto.Power,
                Speed = lDto.Speed,
                Mode = lDto.Mode
            };
            ProjectState.Instance.Layers.Add(l);
        }
        
        if (ProjectState.Instance.Layers.Count == 0) ProjectState.Instance.Layers.Add(new Layer("Default", Color.Black));
        ProjectState.Instance.ActiveLayer = ProjectState.Instance.Layers[0];

        foreach (var objDto in dto.Objects)
        {
            var obj = FromDto(objDto, dto.ImageLibrary);
            if (obj != null) ProjectState.Instance.AddObject(obj);
        }
    }
}

public class ColorJsonConverter : JsonConverter<System.Drawing.Color>
{
    public override System.Drawing.Color Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
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
                    
                    if (string.Equals(prop, "A", StringComparison.OrdinalIgnoreCase)) a = reader.GetInt32();
                    else if (string.Equals(prop, "R", StringComparison.OrdinalIgnoreCase)) r = reader.GetInt32();
                    else if (string.Equals(prop, "G", StringComparison.OrdinalIgnoreCase)) g = reader.GetInt32();
                    else if (string.Equals(prop, "B", StringComparison.OrdinalIgnoreCase)) b = reader.GetInt32();
                    else if (string.Equals(prop, "Name", StringComparison.OrdinalIgnoreCase)) name = reader.GetString();
                }
            }
            
            if (!string.IsNullOrEmpty(name) && name != "0")
            {
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
