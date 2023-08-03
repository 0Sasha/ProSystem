using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using ProSystem.Services;

namespace ProSystem;

public class TradingSystem
{
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
    public bool IsWorking { get; set; } = true;

    public ObservableCollection<Tool> Tools { get; init; } = new();
    public ObservableCollection<Trade> Trades { get; init; } = new();
    public ObservableCollection<Order> Orders { get; init; } = new();
    public ObservableCollection<Order> SystemOrders { get; init; } = new();
    public ObservableCollection<Trade> SystemTrades { get; init; } = new();

    public TradingSystem(MainWindow window, Connector connector, UnitedPortfolio portfolio, Settings settings,
        IToolManager toolManager, IScriptManager scriptManager, IPortfolioManager portfolioManager)
    {
        Window = window ?? throw new ArgumentNullException(nameof(window));
        Connector = connector ?? throw new ArgumentNullException(nameof(connector));
        Portfolio = portfolio ?? throw new ArgumentNullException(nameof(portfolio));
        Settings = settings ?? throw new ArgumentNullException(nameof(settings));
        ToolManager = toolManager ?? throw new ArgumentNullException(nameof(toolManager));
        ScriptManager = scriptManager ?? throw new ArgumentNullException(nameof(scriptManager));
        PortfolioManager = portfolioManager ?? throw new ArgumentNullException(nameof(portfolioManager));
    }

    public TradingSystem(MainWindow window, Connector connector, UnitedPortfolio portfolio, Settings settings,
        IToolManager toolManager, IScriptManager scriptManager, IPortfolioManager portfolioManager,
        IEnumerable<Tool> tools, IEnumerable<Trade> trades) :
        this(window, connector, portfolio, settings, toolManager, scriptManager, portfolioManager)
    {
        Tools = new(tools);
        Trades = new(trades);
    }

    public void PrepareForTrading()
    {
        if (ReadyToTrade)
        {
            Connector.OrderPortfolioInfo(Portfolio);
            foreach (var tool in Tools) ToolManager.RequestBars(tool);
            Window.AddInfo("PrepareForTrading: Bars updated.", false);
            return;
        }

        RequestInfo(false);
        Connector.OrderHistoricalData(new("CETS", "USD000UTSTOM"), new("1"), 1);
        Connector.OrderHistoricalData(new("CETS", "EUR_RUB__TOM"), new("1"), 1);
        foreach (var tool in Tools)
        {
            ScriptManager.BringOrdersInLine(tool, Orders);

            if (tool.MySecurity.Bars == null || tool.BasicSecurity != null && tool.BasicSecurity.Bars == null)
            {
                tool.Active = false;
                Window.AddInfo("PrepareForTrading: " + tool.Name + " деактивирован, потому что не пришли бары.");
            }
            else if (tool.Active)
            {
                Connector.SubscribeToTrades(tool.MySecurity);
                if (tool.BasicSecurity != null) Connector.SubscribeToTrades(tool.BasicSecurity);
            }

            ToolManager.RequestBars(tool);
            Connector.OrderSecurityInfo(tool.MySecurity);
        }

        if (DateTime.Now < DateTime.Today.AddHours(7)) ClearObsoleteData();
        if (Connector.Connection != ConnectionState.Connected) return;
        ReadyToTrade = true;
        Window.AddInfo("System is ready to trade.", false);
    }

    private async void CheckState()
    {
        while (IsWorking)
        {
            if (DateTime.Now > triggerCheckState)
            {
                triggerCheckState = DateTime.Now.AddSeconds(1);
                if (Connector.Connection == ConnectionState.Connected)
                {
                    // Проверка требований единого портфеля
                    if (ReadyToTrade && DateTime.Now > triggerCheckPortfolio) await CheckPortfolio();

                    // Пересчёт скриптов и обновление графиков
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
                                if (DateTime.Now > MyTool.TimeNextRecalc) ToolManager.Calculate(MyTool);
                                ToolManager.UpdateView(MyTool, false);
                            }
                        }
                    }
                    else if (DateTime.Now > triggerUpdateModels)
                    {
                        triggerUpdateModels = DateTime.Now.AddSeconds(Settings.ModelUpdateInterval);
                        foreach (Tool MyTool in Tools) if (MyTool.Active) ToolManager.UpdateView(MyTool, false);
                    }

                    // Запрос информации
                    if (DateTime.Now > triggerRequestInfo) await Task.Run(() => RequestInfo());

                    // Переподключение по расписанию
                    if (Settings.ScheduledConnection && DateTime.Now.Minute == 50)
                    {
                        if (DateTime.Now < DateTime.Today.AddMinutes(400)) //DateTime.Now.Hour == 18 && DateTime.Now.Second < 3
                        {
                            Task disconnection = Task.Run(() => Connector.Disconnect(true));
                            if (!disconnection.Wait(300000))
                                Window.AddInfo("CheckConditions: Превышено время ожидания disconnection task.", notify: true);
                        }
                    }
                }
                else if (Connector.Connection == ConnectionState.Connecting)
                {
                    if (DateTime.Now > Connector.TriggerReconnection)
                    {
                        Window.AddInfo("Переподключение по таймауту.");
                        Task disconnection = Task.Run(() => Connector.Disconnect(false));
                        if (!disconnection.Wait(300000))
                            Window.AddInfo("CheckState: Превышено время ожидания disconnection task.", notify: true);
                        else if (!Settings.ScheduledConnection)
                        {
                            Connector.TriggerReconnection = DateTime.Now.AddSeconds(Settings.SessionTM);
                            Task connection = Task.Run(() => Connector.Connect(false));
                            if (!connection.Wait(300000))
                                Window.AddInfo("CheckState: Превышено время ожидания connection task.", notify: true);
                        }
                    }
                }
                else if (DateTime.Now > DateTime.Today.AddMinutes(400))
                {
                    if (Settings.ScheduledConnection &&
                        (Connector.ServerAvailable || DateTime.Now > Connector.TriggerReconnection) &&
                        Window.Dispatcher.Invoke(() =>
                        Window.TxtLog.Text.Length > 0 && Window.TxtPas.SecurePassword.Length > 0))
                    {
                        Connector.TriggerReconnection = DateTime.Now.AddSeconds(Settings.SessionTM);
                        bool scheduled = DateTime.Now.Minute == 40 && DateTime.Now.Hour == 6;
                        Task connection = Task.Run(() => Connector.Connect(scheduled));
                        if (!connection.Wait(300000))
                            Window.AddInfo("CheckState: Превышено время ожидания connection task.", notify: true);
                    }
                    else if (!Settings.ScheduledConnection && DateTime.Now.Minute is 0 or 30)
                        Settings.ScheduledConnection = true;
                }
                else if (DateTime.Now.Hour == 1 && DateTime.Now.Minute == 0)
                {
                    Connector.BackupServer = false;
                    await Task.Run(async () =>
                    {
                        Logger.StopLogging();
                        Logger.StartLogging();
                        PortfolioManager.UpdateEquity();
                        PortfolioManager.CheckEquity(Settings);
                        PortfolioManager.UpdatePositions();
                        await Logger.ArchiveFiles("Logs/Transaq", DateTime.Now.AddDays(-1).ToString("yyyyMMdd"),
                            DateTime.Now.AddDays(-1).ToString("yyyyMMdd") + " archive", true);
                        await Logger.ArchiveFiles("Data", ".xml", "Data", false);
                    });
                    Thread.Sleep(65000);
                }
            }
            else Thread.Sleep(10);
        }
    }

    private void RequestInfo(bool orderBars = true)
    {
        triggerRequestInfo = DateTime.Now.AddMinutes(95 - DateTime.Now.Minute).AddSeconds(-DateTime.Now.Second);
        Connector.OrderPortfolioInfo(Portfolio);
        foreach (Tool tool in Tools)
        {
            Connector.OrderSecurityInfo(tool.MySecurity);
            if (orderBars && !tool.Active) ToolManager.RequestBars(tool);
        }
    }

    private async Task CheckPortfolio()
    {
        triggerCheckPortfolio = DateTime.Now.AddSeconds(330 - DateTime.Now.Second);
        if (DateTime.Now > DateTime.Today.AddMinutes(840) && DateTime.Now < DateTime.Today.AddMinutes(845)) return;


    }

    private void ClearObsoleteData()
    {
        var obsolete = Trades.Where(x => x.DateTime.Date < DateTime.Today.AddDays(-Settings.ShelfLifeTrades));
        Window.Dispatcher.Invoke(() =>
        {
            foreach (var trade in obsolete) Trades.Remove(trade);
        });
        foreach (var tool in Tools) ScriptManager.ClearObsoleteData(tool, Settings);
        Window.AddInfo("ClearObsoleteData: удалены устаревшие заявки и сделки", false);
    }
}
