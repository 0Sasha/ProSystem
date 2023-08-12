using System;
using System.Threading.Tasks;

namespace ProSystem.Services;

public interface IPortfolioManager
{
    public void UpdateEquity();

    public void UpdatePositions();

    public bool CheckEquity();

    public Task CheckPortfolioAsync();
}
