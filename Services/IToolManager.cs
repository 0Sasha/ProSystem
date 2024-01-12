namespace ProSystem.Services;

public interface IToolManager
{
    public void Initialize(Tool tool);

    public void UpdateControlPanel(Tool tool, bool updateScriptPanel);

    public Task ChangeActivityAsync(Tool tool);

    public Task CalculateAsync(Tool tool);

    public Task ReloadBarsAsync(Tool tool);

    public void UpdateView(Tool tool, bool updateScriptView);
}
