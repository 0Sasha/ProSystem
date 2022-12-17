using System.Linq;
using System.Windows;
using static ProSystem.MainWindow;
namespace ProSystem;

public partial class NewTool : Window
{
    private Security TrSec = null;
    private Security BsSec = null;
    private readonly Tool SelectedTool = null;
    private readonly System.Collections.Generic.List<IScript> Scripts = new();

    private Security TradedSecurity
    {
        get => TrSec;
        set
        {
            TrSec = value;
            if (TrSec == null)
            {
                TradedSec.Text = "";
                TradedBoard.Text = "";
            }
            else
            {
                TradedSec.Text = TrSec.Seccode;
                TradedBoard.Text = TrSec.Board;
            }
        }
    }
    private Security BasicSecurity
    {
        get => BsSec;
        set
        {
            BsSec = value;
            if (BsSec == null)
            {
                BasicSec.Text = "";
                BasicBoard.Text = "";
            }
            else
            {
                BasicSec.Text = BsSec.Seccode;
                BasicBoard.Text = BsSec.Board;
            }
        }
    }

    public NewTool()
    {
        InitializeComponent();

        string[] MyMarkets = new string[Markets.Count];
        for (int i = 0; i < Markets.Count; i++) MyMarkets[i] = Markets[i].Name;

        System.Array.Sort(MyAlgorithms);
        BoxMarkets.ItemsSource = MyMarkets;
        BoxScripts.ItemsSource = MyAlgorithms;
        ScriptsView.ItemsSource = Scripts;
    }
    public NewTool(Tool MyTool)
    {
        InitializeComponent();
        SelectedTool = MyTool;

        string[] MyMarkets = new string[Markets.Count];
        for (int i = 0; i < Markets.Count; i++) MyMarkets[i] = Markets[i].Name;

        BoxMarkets.ItemsSource = MyMarkets;
        BoxScripts.ItemsSource = MyAlgorithms;

        ToolName.Text = SelectedTool.Name;
        Scripts = SelectedTool.Scripts.ToList();
        ScriptsView.ItemsSource = Scripts;

        TradedSecurity = SelectedTool.MySecurity;
        BasicSecurity = SelectedTool.BasicSecurity;

        SecuritiesView.ItemsSource = new System.Collections.Generic.List<Security>() { SelectedTool.MySecurity };
        SecuritiesView.SelectedItem = SecuritiesView.Items[0];
    }

    private void SearchSecChanged(object sender, System.Windows.Controls.TextChangedEventArgs e)
    {
        try
        {
            SecuritiesView.ItemsSource = AllSecurities.Where(x => x.Market == Markets.Single(y => y.Name == BoxMarkets.Text).ID &&
            (x.ShortName.Contains(SearchSec.Text) || x.Seccode.Contains(SearchSec.Text)));
        }
        catch { }
    }
    private void SelectionScriptChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e) =>
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
            string Name = BoxScripts.SelectedItem.ToString();
            if (Name == "ATRS") Scripts.Add(new Algorithms.ATRS(ScriptName.Text));
            else if (Name == "PARS") Scripts.Add(new Algorithms.PARS(ScriptName.Text));
            else if (Name == "CrossEMA") Scripts.Add(new Algorithms.CrossEMA(ScriptName.Text));
            else if (Name == "RSI") Scripts.Add(new Algorithms.RSI(ScriptName.Text));
            else if (Name == "Stochastic") Scripts.Add(new Algorithms.Stochastic(ScriptName.Text));
            else if (Name == "MACD") Scripts.Add(new Algorithms.MACD(ScriptName.Text));
            else if (Name == "ROC") Scripts.Add(new Algorithms.ROC(ScriptName.Text));
            else if (Name == "MFI") Scripts.Add(new Algorithms.MFI(ScriptName.Text));
            else if (Name == "OBV") Scripts.Add(new Algorithms.OBV(ScriptName.Text));
            else if (Name == "AD") Scripts.Add(new Algorithms.AD(ScriptName.Text));
            else if (Name == "CHO") Scripts.Add(new Algorithms.CHO(ScriptName.Text));
            else if (Name == "CMF") Scripts.Add(new Algorithms.CMF(ScriptName.Text));
            else if (Name == "DeMarker") Scripts.Add(new Algorithms.DeMarker(ScriptName.Text));
            else if (Name == "StochRSI") Scripts.Add(new Algorithms.StochRSI(ScriptName.Text));
            else if (Name == "Channel") Scripts.Add(new Algorithms.Channel(ScriptName.Text));
            else if (Name == "CCI") Scripts.Add(new Algorithms.CCI(ScriptName.Text));
            else if (Name == "CMO") Scripts.Add(new Algorithms.CMO(ScriptName.Text));
            else if (Name == "MA") Scripts.Add(new Algorithms.MA(ScriptName.Text));
            else if (Name == "SumLine") Scripts.Add(new Algorithms.SumLine(ScriptName.Text));
            ScriptsView.Items.Refresh();
        }
    }
    private void RemoveScript(object sender, RoutedEventArgs e)
    {
        if (ScriptsView.SelectedIndex > -1)
        {
            Scripts.Remove((IScript)ScriptsView.SelectedItem);
            ScriptsView.Items.Refresh();
        }
    }
    private void SaveTool(object sender, RoutedEventArgs e)
    {
        if (SelectedTool == null)
        {
            if (Tools.SingleOrDefault(x => x.Name == ToolName.Text) == null && TradedSecurity != null &&
                Tools.SingleOrDefault(x => x.MySecurity.Seccode == TradedSecurity.Seccode) == null)
            {
                Close();
                BasicSecurity = BasicSecurity != null ? AllSecurities.Single(x => x == BasicSecurity) : null;
                MainWindow.Window.SaveTool(new Tool(ToolName.Text, AllSecurities.Single(x => x == TradedSecurity), BasicSecurity, Scripts.ToArray()));
            }
        }
        else
        {
            if (TradedSecurity == null ||
                Tools.SingleOrDefault(x => x.Name == ToolName.Text) != null && SelectedTool.Name != ToolName.Text ||
                Tools.SingleOrDefault(x => x.MySecurity.Seccode == TradedSecurity.Seccode) != null &&
                SelectedTool.MySecurity.Seccode != TradedSecurity.Seccode) return;

            SelectedTool.Name = ToolName.Text;
            if (SelectedTool.MySecurity.Seccode != TradedSecurity.Seccode)
            {
                SelectedTool.MySecurity = AllSecurities.Single(x => x == TradedSecurity);
                foreach (IScript Script in SelectedTool.Scripts)
                {
                    Script.MyOrders.Clear();
                    Script.MyTrades.Clear();
                }
            }
            if (BasicSecurity == null)
            {
                SelectedTool.BasicSecurity = null;
                SelectedTool.ShowBasicSecurity = false;
            }
            else if (SelectedTool.BasicSecurity == null || SelectedTool.BasicSecurity.Seccode != BasicSecurity.Seccode)
                SelectedTool.BasicSecurity = AllSecurities.Single(x => x == BasicSecurity);

            if (SelectedTool.Scripts.Length != Scripts.Count) SelectedTool.Scripts = Scripts.ToArray();
            else
            {
                for (int i = 0; i < SelectedTool.Scripts.Length; i++)
                    if (SelectedTool.Scripts[i].Name != Scripts[i].Name) SelectedTool.Scripts[i] = Scripts[i];
            }

            Close();
            MainWindow.Window.SaveTool(SelectedTool, false);
        }
    }
}
