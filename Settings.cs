using Newtonsoft.Json;
using System.ComponentModel;

namespace ProSystem;

[Serializable]
public class Settings : INotifyPropertyChanged
{
    private int modelUpdateInterval = 5;
    private bool scheduledConnection = true;

    private int toleranceEquity = 40;
    private int tolerancePosition = 3;
    private int optShareBaseAssets = 90;
    private int toleranceBaseAssets = 5;
    private int initialLeverage = 1;

    private int maxShareInitReqsPosition = 10;
    private int maxShareInitReqsTool = 25;
    private int maxShareMinReqsPortfolio = 60;
    private int maxShareInitReqsPortfolio = 85;

    [JsonIgnore]
    [field: NonSerialized]
    private AddInformation AddInfo = (_, _, _) => { };

    [field: NonSerialized]
    public event PropertyChangedEventHandler? PropertyChanged;

    public List<string> ToolsByPriority { get; set; } = [];

    public int ModelUpdateInterval
    {
        get => modelUpdateInterval;
        set
        {
            if (value is < 1 or > 600)
            {
                modelUpdateInterval = 5;
                AddInfo("ModelUpdateInterval is 5");
            }
            else modelUpdateInterval = value;
            Notify();
        }
    }

    public bool ScheduledConnection
    {
        get => scheduledConnection;
        set
        {
            scheduledConnection = value;
            if (!scheduledConnection) AddInfo("Scheduled connection is disabled");
            Notify();
        }
    }

    public bool DeepLog { get; set; }

    public bool DisplaySentOrders { get; set; }

    public bool DisplayNewTrades { get; set; }

    public bool DisplaySpecialInfo { get; set; }

    public string Connector { get; set; } = nameof(BnbConnector);

    public string LoginConnector { get; set; } = string.Empty;

    public string Email { get; set; } = string.Empty;

    public string EmailPassword { get; set; } = string.Empty;

    public int ToleranceEquity
    {
        get => toleranceEquity;
        set
        {
            if (value is < 5 or > 300) toleranceEquity = 40;
            else toleranceEquity = value;
            if (toleranceEquity > 50) AddInfo("ToleranceEquity is more than 50%");
            Notify();
        }
    }

    public int TolerancePosition
    {
        get => tolerancePosition;
        set
        {
            if (value is < 1 or > 5) tolerancePosition = 3;
            else tolerancePosition = value;
            Notify();
        }
    }

    public int OptShareBaseAssets
    {
        get => optShareBaseAssets;
        set
        {
            if (value is < 1 or > 150)
            {
                optShareBaseAssets = 90;
                AddInfo("OptShareBaseAssets is 90%");
            }
            else optShareBaseAssets = value;
            Notify();
        }
    }

    public int ToleranceBaseAssets
    {
        get => toleranceBaseAssets;
        set
        {
            if (value is < 1 or > 50)
            {
                toleranceBaseAssets = 5;
                AddInfo("ToleranceBaseAssets is 5");
            }
            else toleranceBaseAssets = value;
            Notify();
        }
    }

    public int MaxShareInitReqsPosition
    {
        get => maxShareInitReqsPosition;
        set
        {
            if (value is < 1 or > 75)
            {
                maxShareInitReqsPosition = 15;
                AddInfo("MaxShareInitReqsPosition is 15%");
            }
            else maxShareInitReqsPosition = value;
            if (maxShareInitReqsPosition > 15) AddInfo("MaxShareInitReqsPosition is more than 15%.");
            Notify();
        }
    }

    public int MaxShareInitReqsTool
    {
        get => maxShareInitReqsTool;
        set
        {
            if (value is < 1 or > 150)
            {
                maxShareInitReqsTool = 25;
                AddInfo("MaxShareInitReqsTool is 25%");
            }
            else maxShareInitReqsTool = value;
            if (maxShareInitReqsTool > 35) AddInfo("MaxShareInitReqsTool is more than 35%.");
            Notify();
        }
    }

    public int MaxShareMinReqsPortfolio
    {
        get => maxShareMinReqsPortfolio;
        set
        {
            if (value is < 10 or > 95)
            {
                maxShareMinReqsPortfolio = 60;
                AddInfo("MaxShareMinReqsPortfolio is 60%");
            }
            else maxShareMinReqsPortfolio = value;
            if (maxShareMinReqsPortfolio > 70) AddInfo("MaxShareMinReqsPortfolio is more than 70%.");
            Notify();
        }
    }

    public int MaxShareInitReqsPortfolio
    {
        get => maxShareInitReqsPortfolio;
        set
        {
            if (value is < 10 or > 200)
            {
                maxShareInitReqsPortfolio = 85;
                AddInfo("MaxShareInitReqsPortfolio is 85%");
            }
            else maxShareInitReqsPortfolio = value;
            if (maxShareInitReqsPortfolio > 95) AddInfo("MaxShareInitReqsPortfolio is more than 95%.");
            Notify();
        }
    }

    public int InitialLeverage
    {
        get => initialLeverage;
        set
        {
            if (value is < 1 or > 3)
            {
                initialLeverage = 1;
                AddInfo("InitialLeverage is 1");
            }
            else initialLeverage = value;
            Notify();
        }
    }

    private void Notify(string? propertyName = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));

    public void Prepare(IList<Tool> tools, AddInformation addInfo)
    {
        AddInfo = addInfo;
        if (tools.Count != ToolsByPriority.Count || tools.Where(x => !ToolsByPriority.Contains(x.Name)).Any())
        {
            ToolsByPriority = tools.Select(t => t.Name).ToList();
            AddInfo("ToolsByPriority are by default.");
        }
    }
}
