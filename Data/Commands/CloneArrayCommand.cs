/*
 * Copyright (c) 2025 Marko Viitanen (Fador) and the project contributors. All rights reserved.
 * Licensed under the MIT License. See LICENSE file in the project root for full license information.
 *
 * This file is part of the GRBL Burn'Em laser control software
 */
using System.Linq;
using System.Collections.Generic;
using grbl_burn_em.Data.Generators;

namespace grbl_burn_em.Data.Commands;

public class CloneArrayCommand : ICommand
{
    private List<LaserObject> _newObjects = new();
    private List<LaserObject> _sourceObjects;
    private ArrayParameters _params;

    public CloneArrayCommand(IEnumerable<LaserObject> source, ArrayParameters parameters)
    {
        _sourceObjects = source.ToList();
        _params = parameters;
    }

    public void Execute()
    {
        _newObjects.Clear();
        var generator = new ArrayLayoutGenerator(_params.Seed);
        
        var generated = generator.Generate(_sourceObjects, _params);

        // Remove source objects (replacement logic)
        foreach (var src in _sourceObjects)
        {
            ProjectState.Instance.Objects.Remove(src);
        }

        foreach (var obj in generated)
        {
            _newObjects.Add(obj);
            ProjectState.Instance.Objects.Add(obj);
        }

        ProjectState.Instance.SelectedObjects = new List<LaserObject>(_newObjects);
    }

    public void Undo()
    {
        foreach (var obj in _newObjects)
        {
            ProjectState.Instance.Objects.Remove(obj);
        }
        
        foreach(var obj in _sourceObjects)
        {
             ProjectState.Instance.Objects.Add(obj);
        }
        
        ProjectState.Instance.SelectedObjects = new List<LaserObject>(_sourceObjects);
    }

    public string Description => $"Array Clone {_params.Rows}x{_params.Cols}";
}
