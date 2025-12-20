using System.Drawing;
using laser_gui_test.Data;
using Xunit;

namespace laser_gui_test.Tests
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
