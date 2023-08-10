using System;
using System.Threading.Tasks;

namespace ProSystem.Services;

public interface IPortfolioManager
{
    public bool CheckEquity();

    public void UpdateEquity();

    public void UpdatePositions();

    public Task NormalizePortfolioAsync();
}
