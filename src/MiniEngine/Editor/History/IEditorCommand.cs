namespace MiniEngine.Editor.History;

public interface IEditorCommand
{
    void Execute();
    void Undo();
}
