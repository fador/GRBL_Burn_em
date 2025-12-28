/*
 * Copyright (c) 2025 Marko Viitanen (Fador) and the project contributors. All rights reserved.
 * Licensed under the MIT License. See LICENSE file in the project root for full license information.
 *
 * This file is part of the GRBL Burn'Em laser control software
 */
using System.Collections.Generic;
using System.Drawing;
using grbl_burn_em.Data;

namespace grbl_burn_em.Data.Commands
{
    public class NestingApplyCommand : ICommand
    {
        private List<ObjectState> _previousStates = new List<ObjectState>();
        private List<ObjectState> _newStates = new List<ObjectState>();

        public string Description => "Apply Nesting";

        public struct ObjectState
        {
            public LaserObject Object;
            public PointF Position;
            public float Rotation;
            public List<PointF> Points; // For LaserPath
        }

        public void AddChange(LaserObject obj, PointF newPos, float newRot, List<PointF>? newPoints = null)
        {
            // Capture Old State
            var oldState = new ObjectState
            {
                Object = obj,
                Position = obj.Position,
                Rotation = obj.Rotation
            };
            
            if (obj is LaserPath path)
            {
                oldState.Points = new List<PointF>(path.Points);
            }
            
            _previousStates.Add(oldState);

            // Capture New State
            var newState = new ObjectState
            {
                Object = obj,
                Position = newPos,
                Rotation = newRot
            };
            if (newPoints != null)
            {
                newState.Points = new List<PointF>(newPoints);
            }
            // For Paths, if no new points provided, keep old (move only)
            else if (obj is LaserPath pathMsg)
            {
                 newState.Points = new List<PointF>(pathMsg.Points);
            }

            _newStates.Add(newState);
        }

        public void Execute()
        {
            ApplyStates(_newStates);
        }

        public void Undo()
        {
            ApplyStates(_previousStates);
        }

        private void ApplyStates(List<ObjectState> states)
        {
            foreach (var state in states)
            {
                state.Object.Position = state.Position;
                state.Object.Rotation = state.Rotation;
                
                if (state.Object is LaserPath path && state.Points != null)
                {
                    path.Points = new List<PointF>(state.Points);
                    path.UpdateBounds();
                }
            }
        }
    }
}
