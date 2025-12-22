/*
 * Copyright (c) 2025 Marko Viitanen (Fador) and the project contributors. All rights reserved.
 * Licensed under the MIT License. See LICENSE file in the project root for full license information.
 *
 * This file is part of the GRBL Burn'Em laser control software
 */
namespace grbl_burn_em.Data.Commands;

public interface ICommand
{
    void Execute();
    void Undo();
    string Description { get; }
}
