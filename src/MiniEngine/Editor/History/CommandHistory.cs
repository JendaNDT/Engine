using System.Collections.Generic;

namespace MiniEngine.Editor.History;

public sealed class CommandHistory
{
    private readonly LinkedList<IEditorCommand> _undoStack = new();
    private readonly Stack<IEditorCommand> _redoStack = new();
    private readonly int _maxCapacity;

    public CommandHistory(int maxCapacity = 100)
    {
        _maxCapacity = maxCapacity;
    }

    public void ExecuteAndPush(IEditorCommand command)
    {
        if (command == null) return;

        command.Execute();
        _undoStack.AddLast(command);

        if (_undoStack.Count > _maxCapacity)
        {
            _undoStack.RemoveFirst();
        }

        _redoStack.Clear();
    }

    public void PushWithoutExecute(IEditorCommand command)
    {
        if (command == null) return;

        // Pokud je to kompozitní příkaz a je prázdný, neukládáme ho
        if (command is CompositeCommand comp && comp.IsEmpty) return;

        _undoStack.AddLast(command);

        if (_undoStack.Count > _maxCapacity)
        {
            _undoStack.RemoveFirst();
        }

        _redoStack.Clear();
    }

    public bool CanUndo => _undoStack.Count > 0;
    public bool CanRedo => _redoStack.Count > 0;

    public void Undo()
    {
        if (_undoStack.Count == 0) return;

        var command = _undoStack.Last!.Value;
        _undoStack.RemoveLast();

        command.Undo();
        _redoStack.Push(command);
    }

    public void Redo()
    {
        if (_redoStack.Count == 0) return;

        var command = _redoStack.Pop();
        command.Execute();
        _undoStack.AddLast(command);
    }

    public void Clear()
    {
        _undoStack.Clear();
        _redoStack.Clear();
    }
}
