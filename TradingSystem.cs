using ProSystem.Services;
using System.Collections.ObjectModel;
using System.IO;

namespace ProSystem;

public delegate void AddInformation(string data, bool important = true, bool notify = false);

public class TradingSystem
{
    private int isOccupied;
    private Thread? stateChecker;
    private DateTime triggerRequestInfo;
    private DateTime triggerCheckState;
    private DateTime triggerRecalc;
    private DateTime triggerUpdateModels;
    private DateTime triggerCheckPortfolio;

    private readonly AddInformation AddInfo;

    private DateTime ServerTime { get => Connector.ServerTime; }

    public const int RecalcInterval = 30;

    public MainWindow Window { get; init; }
    public Connector Connector { get; init; }
    public Portfolio Portfolio { get; init; }
    public Settings Settings { get; init; }
    public IToolManager ToolManager { get; init; }
    public IScriptManager ScriptManager { get; init; }
    public IPortfolioManager PortfolioManager { get; init; }
    public bool ReadyToTrade { get; set; }
    public bool IsWorking { get; private set; }

    public ObservableCollection<Tool> Tools { get; init; } = [];
    public ObservableCollection<Trade> Trades { get; init; } = [];
    public ObservableCollection<Order> Orders { get; init; } = [];
    public ObservableCollection<Order> SystemOrders { get; init; } = [];
    public ObservableCollection<Trade> SystemTrades { get; init; } = [];

    public TradingSystem(MainWindow window, Settings settings,
        Portfolio portfolio, ObservableCollection<Tool> tools, ObservableCollection<Trade> trades)
    {
        Window = window;
        Portfolio = portfolio;
        Settings = settings;
        Tools = tools;
        Trades = trades;
        AddInfo = window.AddInfo;

        if (settings.Connector == nameof(TXmlConnector)) Connector = new TXmlConnector(this, AddInfo);
        else if (settings.Connector == nameof(BnbConnector)) Connector = new BnbConnector(this, AddInfo);
        else throw new ArgumentException("Unknown connector", nameof(settings));

        ScriptManager = new ScriptManager(Window, this, AddInfo);
        ToolManager = new ToolManager(Window, this, ScriptManager, AddInfo);
        PortfolioManager = new PortfolioManager(this, AddInfo);
    }

    public void Start()
    {
        if (Connector.GetType() == typeof(TXmlConnector) && File.Exists("txmlconnector64.dll") ||
            Connector.GetType() == typeof(BnbConnector))
        {
            if (!Connector.Initialized) Connector.Initialize(Settings.DeepLog ? 3 : 2);
        }
        else
        {
            AddInfo("Connector is not found");
            return;
        }

        IsWorking = true;
        stateChecker = new(CheckStateAsync) { IsBackground = true, Name = "StateChecker" };
        stateChecker.Start();
    }

    public async Task StopAsync()
    {
        IsWorking = false;
        await Task.Delay(50);
        if (Connector.Connection != ConnectionState.Disconnected) await Connector.DisconnectAsync();
        if (Connector.Initialized) Connector.Uninitialize();
    }

    public async Task PrepareForTrading()
    {
        if (Interlocked.Exchange(ref isOccupied, 1) != 0) return;
        try
        {
            if (!ReadyToTrade)
            {
                if (await Connector.OrderPortfolioInfoAsync(Portfolio) &&
                    await Connector.PrepareForTradingAsync() && await PrepareToolsAsync())
                {
                    if (ServerTime < DateTime.Today.AddHours(7)) ClearObsoleteData();
                    if (Connector.Connection != ConnectionState.Connected) return;
                    ReadyToTrade = true;
                    AddInfo("System is ready to trade.", false);
                }
                else if (Connector.Connection == ConnectionState.Connected)
                    await Connector.DisconnectAsync();
            }
            else
            {
                await Connector.OrderPortfolioInfoAsync(Portfolio);
                foreach (var tool in Tools) await Connector.RequestBarsAsync(tool);
                AddInfo("PrepareForTrading: bars updated.", false);
            }
        }
        finally
        {
            Interlocked.Exchange(ref isOccupied, 0);
        }
    }

    private async Task<bool> PrepareToolsAsync()
    {
        foreach (var tool in Tools)
        {
            if (Connector.Connection != ConnectionState.Connected) return false;
            ScriptManager.BringOrdersInLine(tool);

            if (tool.Active)
            {
                if (!await Connector.SubscribeToTradesAsync(tool.Security)) return false;
                if (tool.BasicSecurity != null &&
                    !await Connector.SubscribeToTradesAsync(tool.BasicSecurity)) return false;
            }

            if (!await Connector.RequestBarsAsync(tool) ||
                !await Connector.OrderSecurityInfoAsync(tool.Security)) return false;
        }
        return true;
    }


    private async void CheckStateAsync()
    {
        while (IsWorking)
        {
            if (ServerTime > triggerCheckState)
            {
                triggerCheckState = ServerTime.AddSeconds(1);
                try
                {
                    if (Connector.ReconnectTime) await ResetAsync();
                    else if (Connector.Connection == ConnectionState.Connected) await UpdateStateAsync();
                    else if (Connector.Connection == ConnectionState.Connecting) await ReconnectAsync();
                    else await ConnectAsync();
                }
                catch (Exception ex)
                {
                    AddInfo("CheckState: " + ex.Message, notify: true);
                    AddInfo("StackTrace: " + ex.StackTrace, false);
                }
            }
            else Thread.Sleep(10);
        }
    }

    private async Task UpdateStateAsync()
    {
        if (ReadyToTrade && ServerTime > triggerCheckPortfolio)
        {
            triggerCheckPortfolio = ServerTime.AddSeconds(330 - ServerTime.Second);
            await PortfolioManager.CheckPortfolioAsync();
        }

        if (ReadyToTrade && ServerTime > triggerRecalc) await RecalculateToolsAsync();
        else if (ServerTime > triggerUpdateModels)
        {
            triggerUpdateModels = ServerTime.AddSeconds(Settings.ModelUpdateInterval);
            foreach (var tool in Tools) if (tool.Active) ToolManager.UpdateView(tool, false);
        }

        if (ServerTime > triggerRequestInfo)
        {
            var next = new List<int>([10, 25, 40, 55, 70]).First(m => m > ServerTime.Minute);
            triggerRequestInfo = ServerTime.AddMinutes(next - ServerTime.Minute);
            if (!await Connector.OrderPortfolioInfoAsync(Portfolio))
            {
                await Task.Delay(5000);
                if (!await Connector.OrderPortfolioInfoAsync(Portfolio)) ReadyToTrade = false;
            }
            foreach (var tool in Tools) await Connector.OrderSecurityInfoAsync(tool.Security);
        }

        if (!ReadyToTrade)
        {
            await Task.Delay(5000);
            if (!ReadyToTrade && Connector.Connection == ConnectionState.Connected) _ = Task.Run(PrepareForTrading);
        }
    }

    private async Task ConnectAsync()
    {
        if (Settings.ScheduledConnection)
        {
            if (Connector.ServerAvailable || ServerTime > Connector.ReconnectTrigger)
            {
                var cred = Window.GetCredential();
                if (cred.UserName.Length > 0 && cred.SecurePassword.Length > 0)
                    await Connector.ConnectAsync(cred.UserName, cred.SecurePassword);
            }
        }
        else if (ServerTime.Minute is 0 or 30) Settings.ScheduledConnection = true;
    }

    private async Task ReconnectAsync()
    {
        if (ServerTime > Connector.ReconnectTrigger)
        {
            await Connector.DisconnectAsync();
            if (!Settings.ScheduledConnection)
            {
                var cred = Window.GetCredential();
                await Connector.ConnectAsync(cred.UserName, cred.SecurePassword);
            }
        }
    }

    private async Task ResetAsync()
    {
        if (Connector.Connection != ConnectionState.Disconnected)
            await Connector.DisconnectAsync();
        await Task.Delay(10000);
        await Window.ResetAsync();
        PortfolioManager.UpdateEquity();
        PortfolioManager.UpdatePositions();
        await Connector.ResetAsync();
        await ConnectAsync();
        await Task.Delay(120000);
    }


    private async Task RecalculateToolsAsync()
    {
        triggerRecalc = ServerTime.AddSeconds(RecalcInterval);
        if (triggerRecalc.Second < 10) triggerRecalc = triggerRecalc.AddSeconds(10);
        else if (triggerRecalc.Second > 55) triggerRecalc = triggerRecalc.AddSeconds(-4);

        triggerUpdateModels = ServerTime.AddSeconds(Settings.ModelUpdateInterval);
        foreach (var tool in Tools)
        {
            if (tool.Active)
            {
                if (ServerTime > tool.NextRecalc) await ToolManager.CalculateAsync(tool);
                ToolManager.UpdateView(tool, false);
            }
        }
    }

    private void ClearObsoleteData()
    {
        var old = Trades.ToArray().Where(x => x.Time.Date < DateTime.Today.AddDays(-7));
        Window.Dispatcher.Invoke(() =>
        {
            foreach (var trade in old) Trades.Remove(trade);
        });
        foreach (var tool in Tools) ScriptManager.ClearObsoleteData(tool);
        AddInfo("ClearObsoleteData: data is cleared", false);
    }
}
