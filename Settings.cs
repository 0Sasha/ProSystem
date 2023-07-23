using System;
using System.Linq;
using System.ComponentModel;
using System.Collections.Generic;
using static ProSystem.MainWindow;

namespace ProSystem;

[Serializable]
public class Settings : INotifyPropertyChanged
{
    private int UpdInt = 5;
    private int RecInt = 30;

    private int ReqTM = 15;
    private int SesTM = 180;
    private bool SchedCon = true;

    private int TolEq = 40;
    private int TolPos = 3;
    private int OptShBasAssets = 90;
    private int TolBasAssets = 5;

    private int MaxShInRePos = 10;
    private int MaxShInReTool = 25;
    private int MaxShMinRePort = 60;
    private int MaxShInRePort = 85;

    public List<string> ToolsByPriority { get; set; } = new(); // Приоритетность инструментов
    public int ModelUpdateInterval
    {
        get => UpdInt;
        set
        {
            if (value is < 1 or > 600)
            {
                AddInfo("ModelUpdateInterval должен быть от 1 до 600.");
                if (UpdInt is < 1 or > 600) UpdInt = 5;
            }
            else UpdInt = value;
            Notify();
        }
    } // Интервал обновления моделей
    public int RecalcInterval
    {
        get => RecInt;
        set
        {
            if (value is < 5 or > 120)
            {
                AddInfo("RecalcInterval должен быть от 5 до 120.");
                if (RecInt is < 5 or > 120) RecInt = 30;
            }
            else RecInt = value;
            Notify();
        }
    } // Интервал пересчёта скриптов
    public bool ScheduledConnection
    {
        get => SchedCon;
        set
        {
            SchedCon = value;
            if (!SchedCon) AddInfo("Подключение по расписанию отключено.");
            Notify();
        }
    } // Подключение по расписанию

    public bool DisplayMessages { get; set; } // Отображение сообщений в информационной панели
    public bool DisplaySentOrders { get; set; } // Отображение успешно отправленных заявок в информационной панели
    public bool DisplayNewTrades { get; set; } // Отображение новых сделок в информационной панели
    public bool DisplaySpecialInfo { get; set; } // Отображение особой информации в информационной панели

    public string LoginConnector { get; set; } // Логин для подключения к серверу
    public short LogLevelConnector { get; set; } = 2; // Уровень логирования коннектора
    public int RequestTM
    {
        get => ReqTM;
        set
        {
            if (value is < 5 or > 30)
            {
                AddInfo("RequestTM должен быть от 5 до 30.");
                if (ReqTM is < 5 or > 30) ReqTM = 15;
            }
            else ReqTM = value;
            Notify();
        }
    } // Таймаут на выполнение запроса (20 по умолчанию)
    public int SessionTM
    {
        get => SesTM;
        set
        {
            if (value is < 40 or > 300)
            {
                AddInfo("SessionTM должен быть от 40 до 300.");
                if (SesTM is < 40 or > 300) SesTM = 180;
            }
            else SesTM = value;
            Notify();
        }
    } // Таймаут на переподключение к серверу без повторной закачки данных (120 по умолчанию)

    public string Email { get; set; } // Email для уведомлений
    public string EmailPassword { get; set; }

    public int ToleranceEquity
    {
        get => TolEq;
        set
        {
            if (value is < 5 or > 300)
            {
                AddInfo("ToleranceEquity должно быть от 5% до 300%.");
                if (TolEq is < 5 or > 300) TolEq = 40;
            }
            else TolEq = value;
            if (TolEq > 50) AddInfo("ToleranceEquity более 50% от среднего значения.");
            Notify();
        }
    } // Допустимое отклонение стоимости портфеля в % от среднего значения
    public int TolerancePosition
    {
        get => TolPos;
        set
        {
            if (value is < 1 or > 5)
            {
                AddInfo("TolerancePosition должно быть от 1x до 5x.");
                if (TolPos is < 1 or > 5) TolPos = 3;
            }
            else TolPos = value;
            Notify();
        }
    } // Допустимое отклонение объёма текущей позиции в X от рассчитанного объёма

    public int OptShareBaseAssets
    {
        get => OptShBasAssets;
        set
        {
            if (value is < 1 or > 150)
            {
                AddInfo("OptShareBaseBalances должна быть от 1% до 150%.");
                if (OptShBasAssets is < 1 or > 150) OptShBasAssets = 90;
            }
            else OptShBasAssets = value;
            Notify();
        }
    } // Оптимальная доля базовых активов (балансов) в портфеле
    public int ToleranceBaseAssets
    {
        get => TolBasAssets;
        set
        {
            if (value is < 1 or > 150) AddInfo("ToleranceBaseBalances должно быть от 1% до 150%.");
            else TolBasAssets = value;
            Notify();
        }
    } // Допустимое отклонение доли базовых активов (балансов) от оптимального значения

    public int MaxShareInitReqsPosition
    {
        get => MaxShInRePos;
        set
        {
            if (value is < 1 or > 50)
            {
                AddInfo("MaxShareInitReqsPosition должна быть от 1% до 50%.");
                if (MaxShInRePos is < 1 or > 50) MaxShInRePos = 15;
            }
            else MaxShInRePos = value;
            if (MaxShInRePos > 15) AddInfo("MaxShareInitReqsPosition более 15%.");
            Notify();
        }
    } // Максимальная доля начальных требований позиции (без учёта смещения баланса)
    public int MaxShareInitReqsTool
    {
        get => MaxShInReTool;
        set
        {
            if (value is < 1 or > 150)
            {
                AddInfo("MaxShareInitReqsTool должна быть от 1% до 150%.");
                if (MaxShInReTool is < 1 or > 150) MaxShInReTool = 25;
            }
            else MaxShInReTool = value;
            if (MaxShInReTool > 35) AddInfo("MaxShareInitReqsTool более 35%.");
            Notify();
        }
    } // Максимальная доля начальных требований инструмента (с учётом смещения баланса)

    public int MaxShareMinReqsPortfolio
    {
        get => MaxShMinRePort;
        set
        {
            if (value is < 10 or > 95)
            {
                AddInfo("MaxShareMinReqsPortfolio должна быть от 10% до 95%.");
                if (MaxShMinRePort is < 10 or > 95) MaxShMinRePort = 60;
            }
            else MaxShMinRePort = value;
            if (MaxShMinRePort > 70) AddInfo("MaxShareMinReqsPortfolio более 70%.");
            Notify();
        }
    } // Максимальная доля минимальных требований портфеля
    public int MaxShareInitReqsPortfolio
    {
        get => MaxShInRePort;
        set
        {
            if (value is < 10 or > 200)
            {
                AddInfo("MaxShareInitReqsPortfolio должна быть от 10% до 200%.");
                if (MaxShInRePort is < 10 or > 200) MaxShInRePort = 85;
            }
            else MaxShInRePort = value;
            if (MaxShInRePort > 90) AddInfo("MaxShareInitReqsPortfolio более 90%.");
            Notify();
        }
    } // Максимальная доля начальных требований портфеля

    public int ShelfLifeTrades { get; set; } = 60; // Срок хранения всех сделок (в днях)
    public int ShelfLifeOrdersScripts { get; set; } = 90; // Срок хранения заявок скриптов (в днях)
    public int ShelfLifeTradesScripts { get; set; } = 180; // Срок хранения сделок скриптов (в днях)

    [field: NonSerialized] public event PropertyChangedEventHandler PropertyChanged;

    private void Notify(string propertyName = "") =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));

    public Settings() { }

    public void Check(IEnumerable<Tool> tools)
    {
        ModelUpdateInterval = UpdInt;
        RecalcInterval = RecInt;
        RequestTM = ReqTM;
        SessionTM = SesTM;

        ToleranceEquity = TolEq;
        TolerancePosition = TolPos;
        OptShareBaseAssets = OptShBasAssets;
        ToleranceBaseAssets = TolBasAssets;

        MaxShareInitReqsPosition = MaxShInRePos;
        MaxShareInitReqsTool = MaxShInReTool;
        MaxShareMinReqsPortfolio = MaxShMinRePort;
        MaxShareInitReqsPortfolio = MaxShInRePort;

        if (tools.Count() != ToolsByPriority.Count || tools.Where(x => !ToolsByPriority.Contains(x.Name)).Any())
        {
            ToolsByPriority.Clear();
            foreach (Tool tool in tools) ToolsByPriority.Add(tool.Name);
            AddInfo("ToolsByPriority по умолчанию.");
        }
    }
}
