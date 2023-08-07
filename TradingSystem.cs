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
    private bool isWorking;
    private DateTime triggerRequestInfo;
    private DateTime triggerCheckState;
    private DateTime triggerRecalc;
    private DateTime triggerUpdateModels;
    private DateTime triggerCheckPortfolio;
    private Thread stateChecker;

    public MainWindow Window { get; init; }
    public Connector Connector { get; init; }
    public UnitedPortfolio Portfolio { get; init; }
    public Settings Settings { get; init; }
    public IToolManager ToolManager { get; init; }
    public IScriptManager ScriptManager { get; init; }
    public IPortfolioManager PortfolioManager { get; init; }
    public bool ReadyToTrade { get; set; }
    public bool IsWorking { get => isWorking; }

    public ObservableCollection<Tool> Tools { get; init; } = new();
    public ObservableCollection<Trade> Trades { get; init; } = new();
    public ObservableCollection<Order> Orders { get; init; } = new();
    public ObservableCollection<Order> SystemOrders { get; init; } = new();
    public ObservableCollection<Trade> SystemTrades { get; init; } = new();

    public TradingSystem(MainWindow window, Type connectorType, UnitedPortfolio portfolio, Settings settings)
    {
        Window = window ?? throw new ArgumentNullException(nameof(window));
        if (connectorType == typeof(TXmlConnector)) Connector = new TXmlConnector(this, Window.AddInfo);
        else throw new ArgumentException("Unknow connector", nameof(connectorType));
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
        if (File.Exists("txmlconnector64.dll")) Connector.Initialize(Settings.LogLevelConnector);
        else Window.AddInfo("Не найден коннектор txmlconnector64.dll");

        isWorking = true;
        stateChecker = new(CheckState) { IsBackground = true, Name = "StateChecker" };
        stateChecker.Start();
    }

    public async Task Stop()
    {
        isWorking = false;
        Thread.Sleep(50);
        if (Connector.Connection != ConnectionState.Disconnected) await Connector.DisconnectAsync();
        if (!Connector.Initialized) Connector.Uninitialize();
    }

    public async Task PrepareForTrading()
    {
        if (ReadyToTrade)
        {
            await Connector.OrderPortfolioInfoAsync(Portfolio);
            foreach (var tool in Tools) await ToolManager.RequestBarsAsync(tool);
            Window.AddInfo("PrepareForTrading: Bars updated.", false);
            return;
        }

        await RequestInfo(false);
        await Connector.OrderHistoricalDataAsync(new("CETS", "USD000UTSTOM"), new("1"), 1);
        await Connector.OrderHistoricalDataAsync(new("CETS", "EUR_RUB__TOM"), new("1"), 1);
        foreach (var tool in Tools)
        {
            ScriptManager.BringOrdersInLine(tool);

            if (tool.MySecurity.Bars == null || tool.BasicSecurity != null && tool.BasicSecurity.Bars == null)
            {
                tool.Active = false;
                Window.AddInfo("PrepareForTrading: " + tool.Name + " деактивирован, потому что не пришли бары.");
            }
            else if (tool.Active)
            {
                await Connector.SubscribeToTradesAsync(tool.MySecurity);
                if (tool.BasicSecurity != null) await Connector.SubscribeToTradesAsync(tool.BasicSecurity);
            }

            await ToolManager.RequestBarsAsync(tool);
            await Connector.OrderSecurityInfoAsync(tool.MySecurity);
        }

        if (DateTime.Now < DateTime.Today.AddHours(7)) ClearObsoleteData();
        if (Connector.Connection != ConnectionState.Connected) return;
        ReadyToTrade = true;
        Window.AddInfo("System is ready to trade.", false);
    }

    private async void CheckState()
    {
        while (isWorking)
        {
            if (DateTime.Now > triggerCheckState)
            {
                triggerCheckState = DateTime.Now.AddSeconds(1);
                if (Connector.Connection == ConnectionState.Connected) await UpdateState();
                else if (Connector.Connection == ConnectionState.Connecting) await Reconnect();
                else if (DateTime.Now > DateTime.Today.AddMinutes(400)) await Connect();
                else if (DateTime.Now.Hour == 1 && DateTime.Now.Minute == 0)
                {
                    Connector.BackupServer = false;
                    await Window.RelogAsync();
                    PortfolioManager.UpdateEquity();
                    PortfolioManager.CheckEquity();
                    PortfolioManager.UpdatePositions();
                    Thread.Sleep(65000);
                }
            }
            else Thread.Sleep(10);
        }
    }

    private async Task UpdateState() // TODO check catching of all exceptions
    {
        if (ReadyToTrade && DateTime.Now > triggerCheckPortfolio) await CheckPortfolio();

        if (ReadyToTrade && DateTime.Now > triggerRecalc)
        {
            triggerRecalc = DateTime.Now.AddSeconds(Settings.RecalcInterval);
            if (triggerRecalc.Second < 10) triggerRecalc = triggerRecalc.AddSeconds(10);
            else if (triggerRecalc.Second > 55) triggerRecalc = triggerRecalc.AddSeconds(-4);

            triggerUpdateModels = DateTime.Now.AddSeconds(Settings.ModelUpdateInterval);
            foreach (Tool MyTool in Tools)
            {
                if (MyTool.Active)
                {
                    if (DateTime.Now > MyTool.TimeNextRecalc) await ToolManager.CalculateAsync(MyTool);
                    ToolManager.UpdateView(MyTool, false);
                }
            }
        }
        else if (DateTime.Now > triggerUpdateModels)
        {
            triggerUpdateModels = DateTime.Now.AddSeconds(Settings.ModelUpdateInterval);
            foreach (Tool MyTool in Tools) if (MyTool.Active) ToolManager.UpdateView(MyTool, false);
        }

        if (DateTime.Now > triggerRequestInfo) await RequestInfo();

        if (Settings.ScheduledConnection &&
            DateTime.Now.Minute == 50 && DateTime.Now < DateTime.Today.AddMinutes(400))
            await Connector.DisconnectAsync();
    }

    private async Task Connect()
    {
        if (Settings.ScheduledConnection &&
            (Connector.ServerAvailable || DateTime.Now > Connector.TriggerReconnection) &&
            Window.Dispatcher.Invoke(() => Window.TxtLog.Text.Length > 0 && Window.TxtPas.SecurePassword.Length > 0))
        {
            await Window.Dispatcher.Invoke(async () =>
                await Connector.ConnectAsync(Window.TxtLog.Text, Window.TxtPas.SecurePassword));
        }
        else if (!Settings.ScheduledConnection && DateTime.Now.Minute is 0 or 30)
            Settings.ScheduledConnection = true;
    }

    private async Task Reconnect()
    {
        if (DateTime.Now > Connector.TriggerReconnection)
        {
            Window.AddInfo("Reconnection on timeout");
            await Connector.DisconnectAsync();
            if (!Settings.ScheduledConnection) await Window.Dispatcher.Invoke(async () =>
                await Connector.ConnectAsync(Window.TxtLog.Text, Window.TxtPas.SecurePassword));
        }
    }

    private async Task RequestInfo(bool orderBars = true)
    {
        triggerRequestInfo = DateTime.Now.AddMinutes(95 - DateTime.Now.Minute).AddSeconds(-DateTime.Now.Second);
        await Connector.OrderPortfolioInfoAsync(Portfolio);
        foreach (Tool tool in Tools)
        {
            await Connector.OrderSecurityInfoAsync(tool.MySecurity);
            if (orderBars && !tool.Active) await ToolManager.RequestBarsAsync(tool);
        }
    }

    private async Task CheckPortfolio()
    {
        triggerCheckPortfolio = DateTime.Now.AddSeconds(330 - DateTime.Now.Second);
        if (DateTime.Now > DateTime.Today.AddMinutes(840) && DateTime.Now < DateTime.Today.AddMinutes(845)) return;
        await PortfolioManager.NormalizePortfolioAsync();
    }

    private void ClearObsoleteData()
    {
        var obsolete = Trades.Where(x => x.DateTime.Date < DateTime.Today.AddDays(-Settings.ShelfLifeTrades));
        Window.Dispatcher.Invoke(() =>
        {
            foreach (var trade in obsolete) Trades.Remove(trade);
        });
        foreach (var tool in Tools) ScriptManager.ClearObsoleteData(tool);
        Window.AddInfo("ClearObsoleteData: data is cleared", false);
    }
}
