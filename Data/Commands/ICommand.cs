namespace laser_gui_test.Data.Commands;

public interface ICommand
{
    void Execute();
    void Undo();
    string Description { get; }
}
