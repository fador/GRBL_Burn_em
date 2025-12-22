/*
 * Copyright (c) 2025 Marko Viitanen (Fador) and the project contributors. All rights reserved.
 * Licensed under the MIT License. See LICENSE file in the project root for full license information.
 *
 * This file is part of the GRBL Burn'Em laser control software
 */
using System.Drawing;
using grbl_burn_em.Data;
using Xunit;

namespace grbl_burn_em.Tests
{
    public class LayerTests
    {
        [Fact]
        public void ScaleToPower_ScalesSpeedProportionally()
        {
            // Arrange
            var layer = new Layer { Power = 80.0f, Speed = 1000.0f };

            // Act
            // Scale power to 40 (half) -> Speed should also halve to 500
            layer.ScaleToPower(40.0f);

            // Assert
            Assert.Equal(40.0f, layer.Power, 3); // 3 decimal places precision
            Assert.Equal(500.0f, layer.Speed, 3);
        }

        [Fact]
        public void ScaleToSpeed_ScalesPowerProportionally()
        {
            // Arrange
            var layer = new Layer { Power = 80.0f, Speed = 1000.0f };

            // Act
            // Scale speed to 2000 (double) -> Power should also double to 160
            layer.ScaleToSpeed(2000.0f);

            // Assert
            Assert.Equal(2000.0f, layer.Speed, 3);
            Assert.Equal(160.0f, layer.Power, 3);
        }

        [Fact]
        public void ScaleToPower_HandlesZeroValues()
        {
             // Arrange
            var layer = new Layer { Power = 0.0f, Speed = 1000.0f };

            // Act
            // If current power is 0, scaling might be tricky mathematically if we divide by old power.
            // But requirement is linear scaling. S_new = S_old * (P_new / P_old).
            // If P_old is 0, we can't scale by ratio. 
            // Implementation returns if Power is 0.
            
            // Let's test a non-zero case that scales TO zero.
            layer.Power = 100;
            layer.ScaleToPower(0);
            
            Assert.Equal(0.0f, layer.Power, 3);
            Assert.Equal(0.0f, layer.Speed, 3);
        }
    }
}
