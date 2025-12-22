using grbl_burn_em.Data;

namespace grbl_burn_em.Data.Generators;

public interface IGCodeGenerator
{
    string Name { get; }
    IEnumerable<string> Generate(IEnumerable<LaserObject> objects);
}
