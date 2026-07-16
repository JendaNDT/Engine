namespace MiniEngine.Editor;

/// <summary>
/// Aktualne vybrana entita v editoru. Drzi se jen index (int) -
/// platnost se overuje pres transforms.Has(index) pri kazdem pouziti,
/// takze smazana entita "zmizi" z vyberu sama.
/// </summary>
public sealed class EditorSelection
{
    public int EntityIndex = -1;

    public bool HasSelection => EntityIndex >= 0;

    public void Clear() => EntityIndex = -1;
}
