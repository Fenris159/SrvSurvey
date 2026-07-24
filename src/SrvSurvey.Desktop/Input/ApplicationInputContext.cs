namespace SrvSurvey.Desktop.Input;

public sealed class ApplicationInputContext
{
    private int isActive;
    private int isTextInputActive;

    public bool IsActive => Volatile.Read(ref isActive) != 0;

    public bool IsTextInputActive =>
        Volatile.Read(ref isTextInputActive) != 0;

    public bool AreShortcutsActive => IsActive && !IsTextInputActive;

    public void SetActive(bool value)
    {
        Volatile.Write(ref isActive, value ? 1 : 0);
    }

    public void SetTextInputActive(bool value)
    {
        Volatile.Write(ref isTextInputActive, value ? 1 : 0);
    }
}
