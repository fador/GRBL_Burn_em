using grbl_burn_em.Forms;
using grbl_burn_em.Data;

namespace grbl_burn_em;

static class Program
{
    /// <summary>
    ///  The main entry point for the application.
    /// </summary>
    [STAThread]
    static void Main()
    {
        // To customize application configuration such as set high DPI settings or default font,
        // see https://aka.ms/applicationconfiguration.
        ApplicationConfiguration.Initialize();
        
        // Show Splash Screen
        if (!AppConfiguration.Instance.SkipSplashScreen)
        {
            var splash = new SplashForm();
            Application.Run(splash); // Runs message loop, closes when splash closes
        }
        
        Application.Run(MainForm.Instance);
    }    
}