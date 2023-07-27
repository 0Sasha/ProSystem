using System;

namespace ProSystem.Services;

public interface IToolManager
{
    public void UpdateModel(Tool tool);

    public void UpdateMiniModel(Tool tool, Script script = null);
}
