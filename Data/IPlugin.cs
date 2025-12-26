/*
 * Copyright (c) 2025 Marko Viitanen (Fador) and the project contributors. All rights reserved.
 * Licensed under the MIT License. See LICENSE file in the project root for full license information.
 *
 * This file is part of the GRBL Burn'Em laser control software
 */
using System;
using System.Collections.Generic;
using grbl_burn_em.Data.Generators;

namespace grbl_burn_em.Data;

public interface IPlugin
{
    string Name { get; }
    string Version { get; }
    string Author { get; }
    void Initialize(IPluginHost host);
}

public interface IPluginHost
{
    void RegisterMenuItem(string menuPath, string menuItemName, Action action);
    void RegisterContextMenuAction(string name, Action<LaserObject> action);
     void RegisterGCodeGenerator(IGCodeGenerator generator);
    void AddObject(LaserObject obj);
    IEnumerable<LaserObject> GetSelectedObjects();
    void RefreshUI();
}
