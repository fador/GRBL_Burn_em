using System.Collections.Generic;
using System;

namespace laser_gui_test.Data.Commands;

public class CommandManager
{
    private static CommandManager? _instance;
    public static CommandManager Instance => _instance ??= new CommandManager();

    private readonly Stack<ICommand> _undoStack = new();
    private readonly Stack<ICommand> _redoStack = new();
    
    public event EventHandler? StateChanged;

    public void Execute(ICommand command)
    {
        command.Execute();
        _undoStack.Push(command);
        _redoStack.Clear();
        StateChanged?.Invoke(this, EventArgs.Empty);
    }

    public void Undo()
    {
        if (_undoStack.Count > 0)
        {
            var command = _undoStack.Pop();
            command.Undo();
            _redoStack.Push(command);
            StateChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    public void Redo()
    {
        if (_redoStack.Count > 0)
        {
            var command = _redoStack.Pop();
            command.Execute();
            _undoStack.Push(command);
            StateChanged?.Invoke(this, EventArgs.Empty);
        }
    }
    
    public IEnumerable<string> GetHistory()
    {
        foreach(var cmd in _undoStack)
        {
            yield return cmd.Description;
        }
    }
}
