using System.Threading.Tasks;

namespace ProSystem.Services;

public interface IScriptManager
{
    public void IdentifyOrdersAndTrades(Tool tool);

    public void BringOrdersInLine(Tool tool);

    public void ClearObsoleteData(Tool tool);

    public Task<bool> UpdateOrdersAndPositionAsync(Script script);

    public Task<bool> CalculateAsync(Script script, Security security);

    public Task ProcessOrdersAsync(Tool tool, ToolState toolState, Script script);

    public bool AlignData(Tool tool, Script script);

    public void UpdateView(Tool tool, Script script);

    public void WriteLog(Script script, string toolName);
}
