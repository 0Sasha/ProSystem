﻿using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Controls;

namespace ProSystem;

public partial class NewTool : Window
{
    private Security? traded = null;
    private Security? basic = null;
    private readonly Tool? SelectedTool = null;
    private readonly List<Script> Scripts = [];
    private readonly MainWindow Window;

    private readonly string[] Algorithms =
    [
        "RSI",
        "StochRSI",
        "MFI",
        "DeMarker",
        "Stochastic",
        "CMO",
        "CMF",
        "RVI",
        "CCI",
        "DPO",
        "EMV",
        "FRC",
        "OBV",
        "AD",
        "SumLine",
        "CHO",
        "ROC",
        "MACD",
        "MA",
        "Channel",
        "CrossMA",
        "ATRS",
        "PARS",
        "DI"
    ];

    private Security? TradedSecurity
    {
        get => traded;
        set
        {
            traded = value;
            if (traded == null)
            {
                TradedSec.Text = "";
                TradedBoard.Text = "";
            }
            else
            {
                TradedSec.Text = traded.Seccode;
                TradedBoard.Text = traded.Board;
            }
        }
    }
    private Security? BasicSecurity
    {
        get => basic;
        set
        {
            basic = value;
            if (basic == null)
            {
                BasicSec.Text = "";
                BasicBoard.Text = "";
            }
            else
            {
                BasicSec.Text = basic.Seccode;
                BasicBoard.Text = basic.Board;
            }
        }
    }

    private ObservableCollection<Tool> Tools { get => Window.TradingSystem.Tools; }
    private List<Market> Markets { get => Window.Connector.Markets; }
    private List<Security> Securities { get => Window.Connector.Securities; }

    public NewTool(MainWindow window)
    {
        InitializeComponent();
        Window = window;

        Array.Sort(Algorithms);
        BoxMarkets.ItemsSource = Markets.Select(m => m.Name);
        BoxScripts.ItemsSource = Algorithms;
        ScriptsView.ItemsSource = Scripts;
    }

    public NewTool(MainWindow window, Tool tool)
    {
        InitializeComponent();
        Window = window;
        SelectedTool = tool;

        Array.Sort(Algorithms);
        BoxMarkets.ItemsSource = Markets.Select(m => m.Name);
        BoxScripts.ItemsSource = Algorithms;

        ToolName.Text = SelectedTool.Name;
        Scripts = [.. SelectedTool.Scripts];
        ScriptsView.ItemsSource = Scripts;

        TradedSecurity = SelectedTool.Security;
        BasicSecurity = SelectedTool.BasicSecurity;

        SecuritiesView.ItemsSource = new List<Security>() { SelectedTool.Security };
        SecuritiesView.SelectedItem = SecuritiesView.Items[0];
    }

    private void SearchSecChanged(object sender, TextChangedEventArgs e)
    {
        try
        {
            SecuritiesView.ItemsSource = Securities.Where(x =>
                (x.Market == null || x.Market == Markets.Single(y => y.Name == BoxMarkets.Text).ID) &&
                (x.ShortName != null && x.ShortName.Contains(SearchSec.Text) || x.Seccode.Contains(SearchSec.Text)));
        }
        catch { }
    }

    private void SelectionScriptChanged(object sender, SelectionChangedEventArgs e) =>
        ScriptName.Text = ToolName.Text + "_" + BoxScripts.SelectedItem;

    private void SelectTraded(object sender, RoutedEventArgs e)
    {
        if (SecuritiesView.SelectedItem != null) TradedSecurity = SecuritiesView.SelectedItem as Security;
    }

    private void SelectBasic(object sender, RoutedEventArgs e)
    {
        if (SecuritiesView.SelectedItem != null) BasicSecurity = SecuritiesView.SelectedItem as Security;
    }

    private void RemoveTraded(object sender, RoutedEventArgs e) => TradedSecurity = null;

    private void RemoveBasic(object sender, RoutedEventArgs e) => BasicSecurity = null;

    private void AddScript(object sender, RoutedEventArgs e)
    {
        if (Scripts.SingleOrDefault(x => x.Name == ScriptName.Text) == null && BoxScripts.SelectedItem != null)
        {
            Script? newScript = BoxScripts.SelectedItem.ToString() switch
            {
                "ATRS" => new Algorithms.ATRS(ScriptName.Text),
                "PARS" => new Algorithms.PARS(ScriptName.Text),
                "CrossMA" => new Algorithms.CrossMA(ScriptName.Text),
                "RSI" => new Algorithms.RSI(ScriptName.Text),
                "Stochastic" => new Algorithms.Stochastic(ScriptName.Text),
                "MACD" => new Algorithms.MACD(ScriptName.Text),
                "ROC" => new Algorithms.ROC(ScriptName.Text),
                "MFI" => new Algorithms.MFI(ScriptName.Text),
                "OBV" => new Algorithms.OBV(ScriptName.Text),
                "AD" => new Algorithms.AD(ScriptName.Text),
                "CHO" => new Algorithms.CHO(ScriptName.Text),
                "CMF" => new Algorithms.CMF(ScriptName.Text),
                "DeMarker" => new Algorithms.DeMarker(ScriptName.Text),
                "StochRSI" => new Algorithms.StochRSI(ScriptName.Text),
                "Channel" => new Algorithms.Channel(ScriptName.Text),
                "CCI" => new Algorithms.CCI(ScriptName.Text),
                "DPO" => new Algorithms.DPO(ScriptName.Text),
                "EMV" => new Algorithms.EMV(ScriptName.Text),
                "FRC" => new Algorithms.FRC(ScriptName.Text),
                "CMO" => new Algorithms.CMO(ScriptName.Text),
                "RVI" => new Algorithms.RVI(ScriptName.Text),
                "MA" => new Algorithms.MA(ScriptName.Text),
                "SumLine" => new Algorithms.SumLine(ScriptName.Text),
                "DI" => new Algorithms.DI(ScriptName.Text),
                _ => null
            };

            if (newScript == null) return;
            Scripts.Add(newScript);
            ScriptsView.Items.Refresh();
        }
    }

    private void RemoveScript(object sender, RoutedEventArgs e)
    {
        if (ScriptsView.SelectedIndex > -1)
        {
            Scripts.Remove((Script)ScriptsView.SelectedItem);
            ScriptsView.Items.Refresh();
        }
    }
    private void SaveTool(object sender, RoutedEventArgs e)
    {
        if (SelectedTool == null)
        {
            if (Tools.SingleOrDefault(x => x.Name == ToolName.Text) == null && TradedSecurity != null &&
                Tools.SingleOrDefault(x => x.Security.Seccode == TradedSecurity.Seccode) == null)
            {
                BasicSecurity = BasicSecurity != null ? Securities.Single(x => x == BasicSecurity) : null;
                var name = ToolName.Text;
                Close();
                Window.Dispatcher.Invoke(() => Window.SaveTool(new Tool(name,
                    Securities.Single(x => x == TradedSecurity), BasicSecurity, [.. Scripts])));
            }
        }
        else
        {
            if (TradedSecurity == null ||
                Tools.SingleOrDefault(x => x.Name == ToolName.Text) != null && SelectedTool.Name != ToolName.Text ||
                Tools.SingleOrDefault(x => x.Security.Seccode == TradedSecurity.Seccode) != null &&
                SelectedTool.Security.Seccode != TradedSecurity.Seccode) return;

            if (SelectedTool.Name != ToolName.Text)
            {
                Window.Settings.ToolsByPriority.Remove(SelectedTool.Name);
                SelectedTool.Name = ToolName.Text;
                Window.Settings.ToolsByPriority.Add(SelectedTool.Name);
            }
            if (SelectedTool.Security.Seccode != TradedSecurity.Seccode)
            {
                SelectedTool.Security = Securities.Single(x => x == TradedSecurity);
                foreach (Script Script in SelectedTool.Scripts)
                {
                    Script.Orders.Clear();
                    Script.Trades.Clear();
                }
            }
            if (BasicSecurity == null)
            {
                SelectedTool.BasicSecurity = null;
                if (SelectedTool.ShowBasicSecurity == true) SelectedTool.ShowBasicSecurity = false;
            }
            else if (SelectedTool.BasicSecurity == null || SelectedTool.BasicSecurity.Seccode != BasicSecurity.Seccode)
                SelectedTool.BasicSecurity = Securities.Single(x => x == BasicSecurity);

            if (SelectedTool.Scripts.Length != Scripts.Count) SelectedTool.Scripts = [.. Scripts];
            else
            {
                for (int i = 0; i < SelectedTool.Scripts.Length; i++)
                    if (SelectedTool.Scripts[i].Name != Scripts[i].Name) SelectedTool.Scripts[i] = Scripts[i];
            }

            Close();
            Window.Dispatcher.Invoke(() => Window.SaveTool(SelectedTool, false));
        }
    }
}
