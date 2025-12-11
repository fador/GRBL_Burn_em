using laser_gui_test.Data;

namespace laser_gui_test.Data.Generators;

public interface IGCodeGenerator
{
    string Name { get; }
    IEnumerable<string> Generate(IEnumerable<LaserObject> objects);
}
