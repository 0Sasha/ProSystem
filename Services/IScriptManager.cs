namespace ProSystem.Services;

public interface IScriptManager
{
    public void IdentifyOrdersAndTrades(Tool tool);

    public void BringOrdersInLine(Tool tool);

    public void ClearObsoleteData(Tool tool);

    public void UpdateView(Tool tool, Script script);

    public Task<bool> UpdateStateAsync(Tool tool);

    public Task ProcessOrdersAsync(Tool tool, ToolState toolState);
}
