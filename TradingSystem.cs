using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using ProSystem.Services;
namespace ProSystem;

public delegate void AddInformation(string data, bool important = true, bool notify = false);

public class TradingSystem
{
    private Thread stateChecker;
    private DateTime triggerRequestInfo;
    private DateTime triggerCheckState;
    private DateTime triggerRecalc;
    private DateTime triggerUpdateModels;
    private DateTime triggerCheckPortfolio;

    private readonly AddInformation AddInfo;
    private readonly Func<NetworkCredential> GetCredential;

    public Window Window { get; init; }
    public Connector Connector { get; init; }
    public Portfolio Portfolio { get; init; }
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

    public TradingSystem(Window window, AddInformation addInfo, Func<NetworkCredential> getCredential,
        Type connectorType, Portfolio portfolio, Settings settings)
    {
        Window = window ?? throw new ArgumentNullException(nameof(window));
        AddInfo = addInfo ?? throw new ArgumentNullException(nameof(addInfo));
        GetCredential = getCredential ?? throw new ArgumentNullException(nameof(getCredential));

        if (connectorType == typeof(TXmlConnector)) Connector = new TXmlConnector(this, addInfo);
        else if (connectorType == typeof(BnbConnector)) Connector = new BnbConnector(this, addInfo);
        else throw new ArgumentException("Unknown connector", nameof(connectorType));

        Portfolio = portfolio ?? throw new ArgumentNullException(nameof(portfolio));
        Settings = settings ?? throw new ArgumentNullException(nameof(settings));
        ScriptManager = new ScriptManager(Window, this, addInfo);
        ToolManager = new ToolManager(Window, this, ScriptManager, addInfo);
        PortfolioManager = new PortfolioManager(this, addInfo);
    }

    public TradingSystem(Window window, AddInformation addInfo, Func<NetworkCredential> getCredential,
        Type connectorType, Portfolio portfolio, Settings settings,
        IEnumerable<Tool> tools, IEnumerable<Trade> trades) :
        this(window, addInfo, getCredential, connectorType, portfolio, settings)
    {
        Tools = new(tools);
        Trades = new(trades);
    }


    public void Start()
    {
        if (Connector.GetType() == typeof(TXmlConnector) && File.Exists("txmlconnector64.dll") ||
            Connector.GetType() == typeof(BnbConnector))
        {
            if (!Connector.Initialized) Connector.Initialize(Settings.LogLevelConnector);
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
        await Connector.OrderPortfolioInfoAsync(Portfolio);
        if (!ReadyToTrade)
        {
            await Connector.OrderPreTradingData();
            await PrepareToolsAsync();

            if (DateTime.Now < DateTime.Today.AddHours(7)) ClearObsoleteData();
            if (Connector.Connection != ConnectionState.Connected) return;
            ReadyToTrade = true;
            AddInfo("System is ready to trade.", false);
        }
        else
        {
            foreach (var tool in Tools) await ToolManager.RequestBarsAsync(tool);
            AddInfo("PrepareForTrading: bars updated.", false);
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
                    AddInfo("PrepareForTrading: " + tool.Name + " deactivated because there is no bars.");
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
                    AddInfo("CheckState: " + ex.Message, notify: true);
                    AddInfo("StackTrace: " + ex.StackTrace, false);
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
            var min = DateTime.Now.Minute;
            var minutes = min < 25 ? 25 - min : min < 55 ? 55 - min : 85 - min;
            triggerRequestInfo = DateTime.Now.AddMinutes(minutes);
            await Connector.OrderPortfolioInfoAsync(Portfolio);
            foreach (var tool in Tools) await Connector.OrderSecurityInfoAsync(tool.Security);
        }

        if (Settings.ScheduledConnection && DateTime.Now.Minute == 50 &&
            DateTime.Now < DateTime.Today.AddMinutes(400)) await Connector.DisconnectAsync();
        else if (!ReadyToTrade)
        {
            await Task.Delay(5000);
            if (!ReadyToTrade && Connector.Connection == ConnectionState.Connected) _ = Task.Run(PrepareForTrading);
        }
    }

    private async Task ConnectAsync()
    {
        if (Settings.ScheduledConnection)
        {
            if (Connector.ServerAvailable || DateTime.Now > Connector.ReconnectionTrigger)
            {
                var cred = GetCredential();
                if (cred.UserName.Length > 0 && cred.SecurePassword.Length > 0)
                    await Connector.ConnectAsync(cred.UserName, cred.SecurePassword);
            }
        }
        else if (DateTime.Now.Minute is 0 or 30) Settings.ScheduledConnection = true;
    }

    private async Task ReconnectAsync()
    {
        if (DateTime.Now > Connector.ReconnectionTrigger)
        {
            await Connector.DisconnectAsync();
            if (!Settings.ScheduledConnection)
            {
                var cred = GetCredential();
                await Connector.ConnectAsync(cred.UserName, cred.SecurePassword);
            }
        }
    }

    private async Task ResetAsync()
    {
        Connector.BackupServer = false;
        Logger.Stop();
        Logger.Start();
        await FileManager.ArchiveFiles("Logs/Transaq", DateTime.Now.AddDays(-1).ToString("yyyyMMdd"),
            DateTime.Now.AddDays(-1).ToString("yyyyMMdd") + " archive", true);
        await FileManager.ArchiveFiles("Data", ".xml", "Data", false);
        PortfolioManager.UpdateEquity();
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
        Window.Dispatcher.Invoke(() =>
        {
            foreach (var trade in old) Trades.Remove(trade);
        });
        foreach (var tool in Tools) ScriptManager.ClearObsoleteData(tool);
        AddInfo("ClearObsoleteData: data is cleared", false);
    }
}
