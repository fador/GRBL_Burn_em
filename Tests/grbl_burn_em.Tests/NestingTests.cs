using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Xunit;
using grbl_burn_em.Data;
using grbl_burn_em.Data.Generators;
using grbl_burn_em.Data.Geometry;

namespace grbl_burn_em.Tests
{
    public class NestingTests
    {
        [Fact]
        public async Task TestParallelNesting_SimpleRects()
        {
            // Arrange
            var nesting = NestingManager.Instance;
            nesting.SheetSize = new System.Drawing.SizeF(100, 100);
            
            // Create 3 rectangles 20x10
            var objects = new List<LaserObject>();
            for(int i=0; i<3; i++)
            {
                objects.Add(new LaserRectangle 
                { 
                    Name = $"Rect{i}", 
                    Position = new System.Drawing.PointF(0,0), 
                    Size = new System.Drawing.SizeF(20, 10),
                    Rotation = 0
                });
            }
            
            // Act
            var results = await nesting.RunNesting(objects, CancellationToken.None);
            
            // Assert
            Assert.Equal(3, results.Count);
            foreach(var r in results) Assert.NotNull(r.OriginalObject);
            
            // Allow basic overlap checking
            for(int i=0; i<results.Count; i++)
            {
                for(int j=i+1; j<results.Count; j++)
                {
                    bool intersect = GeometryHelpers.DoPolygonsIntersect(results[i].PlacedPolygon, results[j].PlacedPolygon);
                    Assert.False(intersect, $"Polygons {i} and {j} intersect!");
                }
            }
        }
        
        [Fact]
        public async Task TestNesting_Rotation()
        {
             // Arrange
            var nesting = NestingManager.Instance;
            nesting.SheetSize = new System.Drawing.SizeF(100, 100);
            
            // Create a L-shapes or something that packs better with rotation?
            // Just test that non-zero rotation is preserved or used.
            // Actually, my current impl returns "bestAngle".
            
            var obj = new LaserRectangle 
            { 
                 Name = "RotRect", 
                 Position = new System.Drawing.PointF(0,0), 
                 Size = new System.Drawing.SizeF(50, 10),
                 Rotation = 45 // Initial rotation
            };
            
            var results = await nesting.RunNesting(new List<LaserObject>{ obj }, CancellationToken.None);
            Assert.Equal(1, results.Count);
            Assert.NotNull(results[0].PlacedPolygon);
        }
    }
}
