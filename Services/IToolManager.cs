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

    public void Calculate(Tool tool, double waitingAfterLastRecalc = 3);

    public void RequestBars(Tool tool); // async

    public void UpdateBars(Tool tool, bool updateBasicSecurity);  // async

    public Task ReloadBarsAsync(Tool tool);

    public void UpdateView(Tool tool, bool updateScriptView);

    public void UpdateModel(Tool tool);

    public void UpdateMiniModel(Tool tool, Script script = null);
}
