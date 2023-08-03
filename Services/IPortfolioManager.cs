using System;
using System.Threading.Tasks;

namespace ProSystem.Services;

public interface IPortfolioManager
{
    public TradingSystem TradingSystem { get; }

    public bool CheckShares();

    public bool CheckEquity();

    public void UpdateEquity();

    public void UpdatePositions();

    public void UpdateShares();

    public Task NormalizePortfolioAsync();
}
