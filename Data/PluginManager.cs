/*
 * Copyright (c) 2025 Marko Viitanen (Fador) and the project contributors. All rights reserved.
 * Licensed under the MIT License. See LICENSE file in the project root for full license information.
 *
 * This file is part of the GRBL Burn'Em laser control software
 */
using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Linq;
using grbl_burn_em.Data.Generators;

namespace grbl_burn_em.Data;

public class PluginManager : IPluginHost
{
    private static PluginManager? _instance;
    public static PluginManager Instance => _instance ??= new PluginManager();

    private List<IPlugin> _plugins = new();
    private MainForm? _mainForm;

    public void Initialize(MainForm mainForm)
    {
        _mainForm = mainForm;
        LoadPlugins();
    }

    private void LoadPlugins()
    {
        var pluginDir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Plugins");
        if (!Directory.Exists(pluginDir))
        {
            Directory.CreateDirectory(pluginDir);
        }

        foreach (var file in Directory.GetFiles(pluginDir, "*.dll"))
        {
            try
            {
                var asm = Assembly.LoadFrom(file);
                foreach (var type in asm.GetTypes())
                {
                    if (typeof(IPlugin).IsAssignableFrom(type) && !type.IsInterface && !type.IsAbstract)
                    {
                        var plugin = (IPlugin?)Activator.CreateInstance(type);
                        if (plugin != null)
                        {
                            _plugins.Add(plugin);
                            plugin.Initialize(this);
                            System.Diagnostics.Debug.WriteLine($"Loaded plugin: {plugin.Name}");
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Failed to load plugin {file}: {ex.Message}");
            }
        }
    }

    // IPluginHost Implementation

    public void RegisterMenuItem(string menuPath, string menuItemName, Action action)
    {
        _mainForm?.AddMenuItem(menuPath, menuItemName, action);
    }

    public void RegisterContextMenuAction(string name, Action<LaserObject> action)
    {
        _mainForm?.AddContextMenuItem(name, action);
    }

    public void RegisterGCodeGenerator(IGCodeGenerator generator)
    {
        _mainForm?.RegisterGCodeGenerator(generator);
    }

    public void AddObject(LaserObject obj)
    {
        ProjectState.Instance.AddObject(obj);
        _mainForm?.RefreshObjectList(); // Helper to refresh list if needed, or binding logic handles it
    }

    public IEnumerable<LaserObject> GetSelectedObjects()
    {
        return ProjectState.Instance.SelectedObjects;
    }

    public void RefreshUI()
    {
        _mainForm?.InvalidateWorkbench();
    }
}
