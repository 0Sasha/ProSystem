using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using ProSystem.Services;

namespace ProSystem;

internal delegate void AddInformation(string data, bool important = true, bool notify = false);

public class TradingSystem
{
    private Thread stateChecker;
    private DateTime triggerRequestInfo;
    private DateTime triggerCheckState;
    private DateTime triggerRecalc;
    private DateTime triggerUpdateModels;
    private DateTime triggerCheckPortfolio;

    public MainWindow Window { get; init; }
    public Connector Connector { get; init; }
    public UnitedPortfolio Portfolio { get; init; }
    public Settings Settings { get; init; }
    public IToolManager ToolManager { get; init; }
    public IScriptManager ScriptManager { get; init; }
    public IPortfolioManager PortfolioManager { get; init; }
    public bool ReadyToTrade { get; set; }
    public bool IsWorking { get; private set; }

    public ObservableCollection<Tool> Tools { get; init; } = new();
    public ObservableCollection<Trade> Trades { get; init; } = new();
    public ObservableCollection<Order> Orders { get; init; } = new();
    public ObservableCollection<Order> SystemOrders { get; init; } = new();
    public ObservableCollection<Trade> SystemTrades { get; init; } = new();

    public TradingSystem(MainWindow window, Type connectorType, UnitedPortfolio portfolio, Settings settings)
    {
        Window = window ?? throw new ArgumentNullException(nameof(window));
        if (connectorType == typeof(TXmlConnector)) Connector = new TXmlConnector(this, Window.AddInfo);
        else throw new ArgumentException("Unknown connector", nameof(connectorType));
        Portfolio = portfolio ?? throw new ArgumentNullException(nameof(portfolio));
        Settings = settings ?? throw new ArgumentNullException(nameof(settings));
        ScriptManager = new ScriptManager(Window, this, Window.AddInfo);
        ToolManager = new ToolManager(Window, this, ScriptManager, Window.AddInfo);
        PortfolioManager = new PortfolioManager(this, Window.AddInfo);
    }

    public TradingSystem(MainWindow window, Type connectorType, UnitedPortfolio portfolio, Settings settings,
        IEnumerable<Tool> tools, IEnumerable<Trade> trades) : this(window, connectorType, portfolio, settings)
    {
        Tools = new(tools);
        Trades = new(trades);
    }


    public void Start()
    {
        if (Connector.GetType() == typeof(TXmlConnector) && File.Exists("txmlconnector64.dll"))
            Connector.Initialize(Settings.LogLevelConnector);
        else
        {
            Window.AddInfo("Connector is not found");
            return;
        }

        IsWorking = true;
        stateChecker = new(CheckStateAsync) { IsBackground = true, Name = "StateChecker" };
        stateChecker.Start();
    }

    public async Task StopAsync()
    {
        IsWorking = false;
        Thread.Sleep(50);
        if (Connector.Connection != ConnectionState.Disconnected) await Connector.DisconnectAsync();
        if (Connector.Initialized) Connector.Uninitialize();
    }

    public async Task PrepareForTrading()
    {
        if (!ReadyToTrade)
        {
            await Connector.OrderPortfolioInfoAsync(Portfolio);
            await Connector.OrderHistoricalDataAsync(new("CETS", "USD000UTSTOM"), new("1"), 1);
            await Connector.OrderHistoricalDataAsync(new("CETS", "EUR_RUB__TOM"), new("1"), 1);
            await PrepareToolsAsync();

            if (DateTime.Now < DateTime.Today.AddHours(7)) ClearObsoleteData();
            if (Connector.Connection != ConnectionState.Connected) return;
            ReadyToTrade = true;
            Window.AddInfo("System is ready to trade.", false);
        }
        else
        {
            await Connector.OrderPortfolioInfoAsync(Portfolio);
            foreach (var tool in Tools) await ToolManager.RequestBarsAsync(tool);
            Window.AddInfo("PrepareForTrading: bars updated.", false);
        }
    }

    private async Task PrepareToolsAsync()
    {
        foreach (var tool in Tools)
        {
            if (Connector.Connection != ConnectionState.Connected) return;
            ScriptManager.BringOrdersInLine(tool);

            if (tool.Active)
            {
                if (tool.Security.Bars == null || tool.BasicSecurity != null && tool.BasicSecurity.Bars == null)
                {
                    await ToolManager.ChangeActivityAsync(tool);
                    Window.AddInfo("PrepareForTrading: " + tool.Name + " deactivated because there is no bars.");
                }
                else
                {
                    await Connector.SubscribeToTradesAsync(tool.Security);
                    if (tool.BasicSecurity != null) await Connector.SubscribeToTradesAsync(tool.BasicSecurity);
                }
            }

            await Connector.OrderSecurityInfoAsync(tool.Security);
            await ToolManager.RequestBarsAsync(tool);
        }
    }


    private async void CheckStateAsync()
    {
        while (IsWorking)
        {
            if (DateTime.Now > triggerCheckState)
            {
                triggerCheckState = DateTime.Now.AddSeconds(1);
                try
                {
                    if (Connector.Connection == ConnectionState.Connected) await UpdateStateAsync();
                    else if (Connector.Connection == ConnectionState.Connecting) await ReconnectAsync();
                    else if (DateTime.Now > DateTime.Today.AddMinutes(400)) await ConnectAsync();
                    else if (DateTime.Now.Hour == 1 && DateTime.Now.Minute == 0) await ResetAsync();
                }
                catch (Exception ex)
                {
                    Window.AddInfo("CheckState: " + ex.Message, notify: true);
                    Window.AddInfo("StackTrace: " + ex.StackTrace, false);
                }
            }
            else Thread.Sleep(10);
        }
    }

    private async Task UpdateStateAsync()
    {
        if (ReadyToTrade && DateTime.Now > triggerCheckPortfolio)
        {
            triggerCheckPortfolio = DateTime.Now.AddSeconds(330 - DateTime.Now.Second);
            await PortfolioManager.CheckPortfolioAsync();
        }

        if (ReadyToTrade && DateTime.Now > triggerRecalc) await RecalculateToolsAsync();
        else if (DateTime.Now > triggerUpdateModels)
        {
            triggerUpdateModels = DateTime.Now.AddSeconds(Settings.ModelUpdateInterval);
            foreach (var tool in Tools) if (tool.Active) ToolManager.UpdateView(tool, false);
        }

        if (DateTime.Now > triggerRequestInfo)
        {
            triggerRequestInfo = DateTime.Now.AddMinutes(95 - DateTime.Now.Minute);
            await Connector.OrderPortfolioInfoAsync(Portfolio);
            foreach (var tool in Tools) await Connector.OrderSecurityInfoAsync(tool.Security);
        }

        if (Settings.ScheduledConnection && DateTime.Now.Minute == 50 &&
            DateTime.Now < DateTime.Today.AddMinutes(400)) await Connector.DisconnectAsync();
    } 

    private async Task ConnectAsync()
    {
        if (Settings.ScheduledConnection &&
            (Connector.ServerAvailable || DateTime.Now > Connector.ReconnectionTrigger) &&
            Window.Dispatcher.Invoke(() => Window.TxtLog.Text.Length > 0 && Window.TxtPas.SecurePassword.Length > 0))
        {
            await Window.Dispatcher.Invoke(async () =>
                await Connector.ConnectAsync(Window.TxtLog.Text, Window.TxtPas.SecurePassword));
        }
        else if (!Settings.ScheduledConnection && DateTime.Now.Minute is 0 or 30)
            Settings.ScheduledConnection = true;
    }

    private async Task ReconnectAsync()
    {
        if (DateTime.Now > Connector.ReconnectionTrigger)
        {
            await Connector.DisconnectAsync();
            if (!Settings.ScheduledConnection) await Window.Dispatcher.Invoke(async () =>
                await Connector.ConnectAsync(Window.TxtLog.Text, Window.TxtPas.SecurePassword));
        }
    }

    private async Task ResetAsync()
    {
        Connector.BackupServer = false;
        Window.RestartLogging();
        await FileManager.ArchiveFiles("Logs/Transaq", DateTime.Now.AddDays(-1).ToString("yyyyMMdd"),
            DateTime.Now.AddDays(-1).ToString("yyyyMMdd") + " archive", true);
        await FileManager.ArchiveFiles("Data", ".xml", "Data", false);
        PortfolioManager.UpdateEquity();
        PortfolioManager.CheckEquity();
        PortfolioManager.UpdatePositions();
        await Task.Delay(65000);
    }

    private async Task RecalculateToolsAsync()
    {
        triggerRecalc = DateTime.Now.AddSeconds(Settings.RecalcInterval);
        if (triggerRecalc.Second < 10) triggerRecalc = triggerRecalc.AddSeconds(10);
        else if (triggerRecalc.Second > 55) triggerRecalc = triggerRecalc.AddSeconds(-4);

        triggerUpdateModels = DateTime.Now.AddSeconds(Settings.ModelUpdateInterval);
        foreach (var tool in Tools)
        {
            if (tool.Active)
            {
                if (DateTime.Now > tool.TimeNextRecalc) await ToolManager.CalculateAsync(tool);
                ToolManager.UpdateView(tool, false);
            }
        }
    }

    private void ClearObsoleteData()
    {
        var old = Trades.ToArray().Where(x => x.DateTime.Date < DateTime.Today.AddDays(-Settings.ShelfLifeTrades));
        Window.Dispatcher.Invoke(() => { foreach (var trade in old) Trades.Remove(trade); });
        foreach (var tool in Tools) ScriptManager.ClearObsoleteData(tool);
        Window.AddInfo("ClearObsoleteData: data is cleared", false);
    }
}
