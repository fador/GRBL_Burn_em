using System.Text.Json;

namespace grbl_burn_em.Data;

public class AppConfiguration
{
    private static AppConfiguration? _instance;
    public static AppConfiguration Instance => _instance ??= Load();

    public string LastPortName { get; set; } = "";
    public int BaudRate { get; set; } = 115200;
    public string GCodeGenerator { get; set; } = "Grbl";
    
    public float WorkAreaWidth { get; set; } = 400f;
    public float WorkAreaHeight { get; set; } = 400f;
    public string WorkOrigin { get; set; } = "BottomLeft";
    public float RasterLineInterval { get; set; } = 0.3f;
    public float MinRasterSegmentLength { get; set; } = 0.2f;
    public bool EnableBicubicResampling { get; set; } = true;
    public float FramingPower { get; set; } = 0f;
    public float FramingSpeed { get; set; } = 1000f;
    public float SnapGridSize { get; set; } = 1.0f;
    public bool SkipSplashScreen { get; set; } = false;
    public float SvgCurveQuality { get; set; } = 0.002f; // Lower is better quality (more points)

    public bool Enable1BitDithering { get; set; } = false;
    public bool EmbedImagesInProject { get; set; } = false;

    // View Settings
    public float LastPanX { get; set; } = 0f;
    public float LastPanY { get; set; } = 0f;
    public float LastZoom { get; set; } = 1.0f;

    // Window Settings
    public int WindowX { get; set; } = -1;
    public int WindowY { get; set; } = -1;
    public int WindowWidth { get; set; } = 1200;
    public int WindowHeight { get; set; } = 800;
    public int WindowState { get; set; } = 0; // 0=Normal, 1=Min, 2=Max
    
    // Camera Settings
    public string LastCameraDevice { get; set; } = "";
    public bool ShowCameraOverlay { get; set; } = false;
    public float CameraOverlayOpacity { get; set; } = 0.5f;
    public bool CameraIsMounted { get; set; } = false; // False = Stationary
    
    // Manual Overlay Override (Legacy/Simple mode)
    public float CameraOverlayX { get; set; } = 0;
    public float CameraOverlayY { get; set; } = 0;
    public float CameraOverlayWidth { get; set; } = 100;
    public float CameraOverlayHeight { get; set; } = 100;

    private static readonly string ConfigPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "config.json");

    public void Save()
    {
        try
        {
            var json = JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(ConfigPath, json);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Failed to save config: {ex.Message}");
        }
    }

    private static AppConfiguration Load()
    {
        if (File.Exists(ConfigPath))
        {
            try
            {
                var json = File.ReadAllText(ConfigPath);
                var config = JsonSerializer.Deserialize<AppConfiguration>(json);
                if (config != null) return config;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Failed to load config: {ex.Message}");
            }
        }
        return new AppConfiguration();
    }
}
