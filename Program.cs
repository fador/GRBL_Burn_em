/*
 * Copyright (c) 2025 Marko Viitanen (Fador) and the project contributors. All rights reserved.
 * Licensed under the MIT License. See LICENSE file in the project root for full license information.
 *
 * This file is part of the GRBL Burn'Em laser control software
 */
using grbl_burn_em.Forms;
using grbl_burn_em.Data;

namespace grbl_burn_em;

static class Program
{
    /// <summary>
    ///  The main entry point for the application.
    /// </summary>
    [STAThread]
    static void Main(string[] args)
    {
        // To customize application configuration such as set high DPI settings or default font,
        // see https://aka.ms/applicationconfiguration.
        ApplicationConfiguration.Initialize();
        
        if (args.Contains("--calibrator"))
        {
            Application.Run(new StandaloneLensCalibratorForm());
            return;
        }

        // Show Splash Screen
        if (!AppConfiguration.Instance.SkipSplashScreen)
        {
            var splash = new SplashForm();
            Application.Run(splash); // Runs message loop, closes when splash closes
        }
        
        Application.Run(MainForm.Instance);
    }    
}