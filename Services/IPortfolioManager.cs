using System;
using System.Collections.Generic;

namespace ProSystem;

public interface IPortfolioManager
{
    public UnitedPortfolio Portfolio { get; }

    public void UpdateEquity();

    public void UpdatePositions();

    public void UpdateShares(IEnumerable<Tool> tools);

    public bool CheckShares(Settings settings);

    public bool CheckEquity(Settings settings);
}
