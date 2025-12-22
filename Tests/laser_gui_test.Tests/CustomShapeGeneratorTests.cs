using System;
using System.Collections.Generic;
using System.Linq;
using Xunit;
using grbl_burn_em.Data.Generators;

namespace grbl_burn_em.Tests
{
    public class CustomShapeGeneratorTests
    {
        [Fact]
        public void TestShapeGeneration()
        {
            var shapeParams = new CustomShapeParameters();
            shapeParams.Definitions = "a=10";
            shapeParams.Formula = "x = t * a\ny = t * a";
            shapeParams.StepSize = 1;
            shapeParams.MaxSteps = 5;

            var points = shapeParams.Generate();

            Assert.Equal(5, points.Count);
            Assert.Equal(0, points[0].X); // t=0
            Assert.Equal(10, points[1].X); // t=1 * 10
            Assert.Equal(20, points[2].X); // t=2 * 10
        }

        [Fact]
        public void TestVariableUpdates()
        {
            var shapeParams = new CustomShapeParameters();
            shapeParams.Definitions = "a=10";
            
            // Check property descriptor
            var props = shapeParams.GetProperties();
            var propA = props.Cast<System.ComponentModel.PropertyDescriptor>().FirstOrDefault(p => p.Name == "a");
            Assert.NotNull(propA);
            Assert.Equal(10.0, propA.GetValue(shapeParams));

            // Update definitions string
            shapeParams.Definitions = "a=20";
            
            // Re-fetch property
            Assert.Equal(20.0, propA.GetValue(shapeParams));
        }

        [Fact]
        public void TestComplexGeneration()
        {
             var shapeParams = new CustomShapeParameters();
             shapeParams.Definitions = "r=10";
             // Circle formula
             shapeParams.Formula = "x = r * cos(t)\ny = r * sin(t)";
             shapeParams.StepSize = (float)Math.PI; // 180 degrees
             shapeParams.MaxSteps = 3;
             
             // t=0: x=10, y=0
             // t=pi: x=-10, y=0
             // t=2pi: x=10, y=0
             
             var points = shapeParams.Generate();
             Assert.Equal(3, points.Count);
             
             Assert.Equal(10, points[0].X, 3);
             Assert.Equal(0, points[0].Y, 3);
             
             Assert.Equal(-10, points[1].X, 3);
             Assert.Equal(0, points[1].Y, 3);
        }
    }
}
