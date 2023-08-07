using System;
using System.Threading.Tasks;
using System.Windows.Controls;

namespace ProSystem.Services;

public interface IToolManager
{
    public void Initialize(Tool tool, TabItem tabTool);

    public void CreateTab(Tool tool);

    public void UpdateControlGrid(Tool tool);

    public Task ChangeActivityAsync(Tool tool);

    public Task CalculateAsync(Tool tool);

    public Task RequestBarsAsync(Tool tool);

    public void UpdateBars(Tool tool, bool updateBasicSecurity);

    public Task ReloadBarsAsync(Tool tool);

    public void UpdateView(Tool tool, bool updateScriptView);

    public void UpdateModel(Tool tool);

    public void UpdateMiniModel(Tool tool, Script script = null);

    public void UpdateLastTrade(Tool tool, Trade lastTrade);
}
