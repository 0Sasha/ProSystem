using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using System.Runtime.Serialization;
using static ProSystem.MainWindow;

namespace ProSystem;

public enum NameMA { SMA, EMA, WMA, VMA, SMMA, DEMA, TEMA, KAMA, LR, Median }
public enum ScriptType
{
    OSC, StopLine, LimitLine, Line
}
public enum ConnectionState
{
    Connected, Connecting, Disconnected, Disconnecting
}
public enum PositionType
{
    Neutral, Long, Short
}
public enum OrderType
{
    Limit, Conditional, Market
}

[Serializable] public class ScriptResult
{
    public ScriptType Type { get; set; }
    public bool[] IsGrow { get; set; }
    public double[][] Indicators { get; set; }
    public DateTime iLastDT { get; set; }

    public int Centre { get; set; } = -1;
    public int Level { get; set; } = -1;
    public bool OnlyLimit { get; set; }

    public ScriptResult() { }
    public ScriptResult(ScriptType Type, bool[] IsGrow, double[][] Indicators, DateTime iLastDT)
    {
        this.Type = Type;
        this.IsGrow = IsGrow;
        this.Indicators = Indicators;
        this.iLastDT = iLastDT;
    }
    public ScriptResult(ScriptType Type, bool[] IsGrow, double[][] Indicators, DateTime iLastDT, bool OnlyLimit)
    {
        this.Type = Type;
        this.IsGrow = IsGrow;
        this.Indicators = Indicators;
        this.iLastDT = iLastDT;
        this.OnlyLimit = OnlyLimit;
    }
    public ScriptResult(ScriptType Type, bool[] IsGrow, double[][] Indicators, DateTime iLastDT, int Centre, int Level, bool OnlyLimit)
    {
        this.Type = Type;
        this.IsGrow = IsGrow;
        this.Indicators = Indicators;
        this.iLastDT = iLastDT;

        this.Centre = Centre;
        this.Level = Level;
        this.OnlyLimit = OnlyLimit;
    }
}
[Serializable] public class Order
{
    private string State;

    public int TrID { get; set; } // Идентификатор транзакции сервера Transaq
    public long OrderNo { get; set; } // Биржевой номер заявки
    public string Seccode { get; set; } // Код инструмента
    public string Status
    {
        get => State;
        set { if (State != value) { State = value; DateTime = DateTime.Now; } }
    } // Статус заявки
    public DateTime DateTime { get; set; } // Дата последнего изменения статуса заявки
    public string BuySell { get; set; } // B - покупка, S - продажа
    public DateTime Time { get; set; } // Время регистрации заявки биржей
    public DateTime AcceptTime { get; set; } // Время регистрации заявки сервером Transaq (только для условных заявок)
    public double Price { get; set; } // Цена
    public int Balance { get; set; } // Неудовлетворенный остаток объема заявки в лотах (контрактах)
    public int Quantity { get; set; } // Количество
    public DateTime WithdrawTime { get; set; } // Время снятия заявки (0 для активных)

    public string Condition { get; set; } // Условие
    public double ConditionValue { get; set; } // Цена для условной заявки либо обеспеченность в процентах
    public DateTime ValidAfter { get; set; } // С какого момента времени действительна
    public DateTime ValidBefore { get; set; } // До какого момента действительна

    public string Sender { get; set; } // Отправитель заявки (название алгоритма)
    public string Signal { get; set; } // Цель заявки
    public string Note { get; set; } // Примечание заявки

    public Order() { }
    public Order(int TrID) { this.TrID = TrID; }
    public Order(int TrID, string Sender, string Signal, string Note)
    {
        this.TrID = TrID;
        this.Sender = Sender;
        this.Signal = Signal;
        this.Note = Note;
    }
}
[Serializable] public class Trade
{
    public long TradeNo { get; private set; } // Номер сделки на бирже
    public long OrderNo { get; set; } // Номер заявки на бирже
    public string Seccode { get; set; } // Код инструмента
    public string BuySell { get; set; } // B - покупка, S - продажа
    public DateTime DateTime { get; set; } // Дата и время
    public double Price { get; set; } // Цена
    public int Quantity { get; set; } // Количество

    public string SenderOrder { get; set; } // Отправитель заявки (название алгоритма)
    public string SignalOrder { get; set; } // Цель заявки
    public string NoteOrder { get; set; } // Примечание заявки

    public Trade() { }
    public Trade(long TradeNo) { this.TradeNo = TradeNo; }
    public Trade(DateTime DateTime) { this.DateTime = DateTime; }
}
[Serializable] public class Bars : ISerializable
{
    public DateTime[] DateTime { get; set; }
    public double[] Open { get; set; }
    public double[] High { get; set; }
    public double[] Low { get; set; }
    public double[] Close { get; set; }
    public double[] Volume { get; set; }
    public int TF { get; private set; }

    public Bars() { }
    public Bars(int TF) { this.TF = TF; }
    public Bars(DateTime[] DateTime, double[] Open, double[] High, double[] Low, double[] Close, double[] Volume, int TF)
    {
        this.DateTime = DateTime;
        this.Open = Open; this.High = High;
        this.Low = Low; this.Close = Close;
        this.Volume = Volume; this.TF = TF;
    }

    public virtual void GetObjectData(SerializationInfo info, StreamingContext context)
    {
        info.AddValue("<DateTime>k__BackingField", DateTime);
        info.AddValue("<Open>k__BackingField", Open);
        info.AddValue("<High>k__BackingField", High);
        info.AddValue("<Low>k__BackingField", Low);
        info.AddValue("<Close>k__BackingField", Close);
        info.AddValue("<Volume>k__BackingField", Volume);
        info.AddValue("<TF>k__BackingField", TF);
    }
    protected Bars(SerializationInfo info, StreamingContext context)
    {
        Type T = Array.Empty<double>().GetType();
        DateTime = (DateTime[])info.GetValue("<DateTime>k__BackingField", Array.Empty<DateTime>().GetType());
        Open = (double[])info.GetValue("<Open>k__BackingField", T);
        High = (double[])info.GetValue("<High>k__BackingField", T);
        Low = (double[])info.GetValue("<Low>k__BackingField", T);
        Close = (double[])info.GetValue("<Close>k__BackingField", T);

        TF = info.GetInt32("<TF>k__BackingField");
        try { Volume = (double[])info.GetValue("<Volume>k__BackingField", T); }
        catch
        {
            int[] IntVolume = (int[])info.GetValue("<Volume>k__BackingField", Array.Empty<int>().GetType());
            Volume = IntVolume.Select(x => (double)x).ToArray();
        }
    }

    public Bars GetCopy() => (Bars)MemberwiseClone();
    public void TrimBars(int FirstBar)
    {
        if (FirstBar > -1 && FirstBar < DateTime.Length)
        {
            DateTime = DateTime[FirstBar..];
            Open = Open[FirstBar..];
            High = High[FirstBar..];
            Low = Low[FirstBar..];
            Close = Close[FirstBar..];
            Volume = Volume[FirstBar..];
        }
        else AddInfo("TrimBars: обрезка баров невозможна.");
    }
    public static Bars Compress(Bars SourceBars, int TF)
    {
        // Проверка соответствия таймфреймов
        if (TF == SourceBars.TF) return SourceBars;
        if (TF % SourceBars.TF != 0)
        {
            AddInfo("Compress: Сжатие в заданный ТФ невозможно. Возвращение исходных баров.");
            return SourceBars;
        }

        // Инициализация коллекций
        List<DateTime> bDateTime = new();
        List<double> bOpen = new();
        List<double> bHigh = new();
        List<double> bLow = new();
        List<double> bClose = new();
        List<double> bVolume = new();

        // Создание массива временных меток заданного таймфрейма
        int CountMarks = (int)Math.Ceiling(1440D / TF);
        TimeSpan TimeSpanTF = TimeSpan.FromMinutes(TF);
        TimeSpan[] TimeMarks = new TimeSpan[CountMarks];
        TimeMarks[0] = TimeSpan.Zero.Add(TimeSpanTF);
        for (int i = 1; i < CountMarks; i++) TimeMarks[i] = TimeMarks[i - 1].Add(TimeSpanTF);

        // Получение зеркальной копии и её проверка
        Bars Bars = SourceBars.GetCopy();
        if (Bars.DateTime.Length != Bars.Open.Length || Bars.DateTime.Length != Bars.High.Length ||
            Bars.DateTime.Length != Bars.Low.Length || Bars.DateTime.Length != Bars.Close.Length || Bars.DateTime.Length != Bars.Volume.Length)
        {
            AddInfo("Compress: Несоответствие массивов DT/O/H/L/C/V: " + Bars.DateTime.Length + "/" +
                Bars.Open.Length + "/" + Bars.High.Length + "/" + Bars.Low.Length + "/" + Bars.Close.Length + "/" + Bars.Volume.Length);
            System.Threading.Thread.Sleep(2000);

            Bars = SourceBars.GetCopy();
            AddInfo("Compress: Длины массивов DT/O/H/L/C/V: " + Bars.DateTime.Length + "/" +
                Bars.Open.Length + "/" + Bars.High.Length + "/" + Bars.Low.Length + "/" + Bars.Close.Length + "/" + Bars.Volume.Length);
        }

        // Сжатие в заданный таймфрейм
        double Max, Min, Sum;
        int StartBigBar = 0, EndBigBar = 0;
        TimeSpan EndTimeBigBar = TimeMarks.First(x => x > Bars.DateTime[0].TimeOfDay);
        for (int i = 0, j; i < Bars.DateTime.Length; i++)
        {
            if (i + 1 == Bars.DateTime.Length || Bars.DateTime[i].Date != Bars.DateTime[i + 1].Date) // Последний бар дня/истории
            {
                if (Bars.DateTime[i].TimeOfDay >= EndTimeBigBar) // Формирование дополнительного большого бара перед последним большим баром
                {
                    for (j = StartBigBar + 1, Min = Bars.Low[StartBigBar], Max = Bars.High[StartBigBar],
                        Sum = Bars.Volume[StartBigBar]; j < i; j++)
                    {
                        if (Bars.High[j] > Max) Max = Bars.High[j];
                        if (Bars.Low[j] < Min) Min = Bars.Low[j];
                        Sum += Bars.Volume[j];
                    }

                    bDateTime.Add(Bars.DateTime[StartBigBar].Date.Add(EndTimeBigBar.Add(-TimeSpanTF)));
                    bOpen.Add(Bars.Open[StartBigBar]);
                    bHigh.Add(Max);
                    bLow.Add(Min);
                    bClose.Add(Bars.Close[i - 1]);
                    bVolume.Add(Sum);

                    StartBigBar = i;
                    EndTimeBigBar = TimeMarks.First(x => x > Bars.DateTime[i].TimeOfDay);
                }
                for (j = StartBigBar + 1, Min = Bars.Low[StartBigBar], Max = Bars.High[StartBigBar],
                    Sum = Bars.Volume[StartBigBar]; j < i + 1; j++)
                {
                    if (Bars.High[j] > Max) Max = Bars.High[j];
                    if (Bars.Low[j] < Min) Min = Bars.Low[j];
                    Sum += Bars.Volume[j];
                }

                bDateTime.Add(Bars.DateTime[StartBigBar].Date.Add(EndTimeBigBar.Add(-TimeSpanTF)));
                bOpen.Add(Bars.Open[StartBigBar]);
                bHigh.Add(Max);
                bLow.Add(Min);
                bClose.Add(Bars.Close[i]);
                bVolume.Add(Sum);

                StartBigBar = i + 1;
                EndTimeBigBar = i + 1 < Bars.Close.Length ? TimeMarks.First(x => x > Bars.DateTime[i + 1].TimeOfDay) : TimeMarks[0];
            }
            else if (Bars.DateTime[i].TimeOfDay >= EndTimeBigBar) // Открылся следующий большой бар, сжатие предыдущего
            {
                EndBigBar = i - 1;
                for (j = StartBigBar + 1, Min = Bars.Low[StartBigBar], Max = Bars.High[StartBigBar],
                    Sum = Bars.Volume[StartBigBar]; j < EndBigBar + 1; j++)
                {
                    if (Bars.High[j] > Max) Max = Bars.High[j];
                    if (Bars.Low[j] < Min) Min = Bars.Low[j];
                    Sum += Bars.Volume[j];
                }

                bDateTime.Add(Bars.DateTime[StartBigBar].Date.Add(EndTimeBigBar.Add(-TimeSpanTF)));
                bOpen.Add(Bars.Open[StartBigBar]);
                bHigh.Add(Max);
                bLow.Add(Min);
                bClose.Add(Bars.Close[EndBigBar]);
                bVolume.Add(Sum);

                StartBigBar = i;
                EndTimeBigBar = TimeMarks.First(x => x > Bars.DateTime[i].TimeOfDay);
            }
        }
        return new Bars(bDateTime.ToArray(), bOpen.ToArray(), bHigh.ToArray(), bLow.ToArray(), bClose.ToArray(), bVolume.ToArray(), TF);
    }
    public static void WriteBars(Bars Bars, string Name)
    {
        var Cu = System.Globalization.CultureInfo.InvariantCulture;
        string[] Data = new string[Bars.Close.Length];
        for (int i = 0; i < Bars.Close.Length; i++)
            Data[i] = Bars.DateTime[i].ToString("yyyyMMdd,HH:mm") + "," + Bars.Open[i].ToString(Cu) + "," +
                Bars.High[i].ToString(Cu) + "," + Bars.Low[i].ToString(Cu) + "," + Bars.Close[i].ToString(Cu) + "," + Bars.Volume[i].ToString(Cu);
        File.WriteAllLines("Data/" + Name + ".csv", Data);
    }
}
[Serializable] public class Security
{
    private Trade LastTr;
    private DateTime LastTrDT;

    public string Seccode { get; private set; }
    public string Currency { get; set; } // Валюта номинала (не валюта расчётов)
    public string Board { get; set; }
    public string ShortName { get; set; }
    public string Market { get; set; }
    public int Decimals { get; set; } // Количество десятичных знаков в цене
    public double MinStep { get; set; } // Шаг цены
    public int LotSize { get; set; } // Размер лота

    public Trade LastTrade
    {
        get => LastTr;
        set
        {
            LastTr = value;
            if (LastTr.DateTime < Bars.DateTime[^1].AddMinutes(Bars.TF))
            {
                Bars.Close[^1] = LastTr.Price;
                if (LastTr.Price > Bars.High[^1]) Bars.High[^1] = LastTr.Price;
                else if (LastTr.Price < Bars.Low[^1]) Bars.Low[^1] = LastTr.Price;
                Bars.Volume[^1] += LastTr.Quantity;
            }
            else if (DateTime.Now > LastTrDT) // Открытие нового бара
            {
                LastTrDT = DateTime.Now.AddSeconds(10);
                Tool MyTool = Tools.Single(x => x.MySecurity.Seccode == Seccode || x.BasicSecurity != null && x.BasicSecurity.Seccode == Seccode);
                MyTool.TimeNextRecalc = DateTime.Now.AddSeconds(30);

                // Создание нового бара
                if (DateTime.Now.Date == Bars.DateTime[^1].Date)
                    Bars.DateTime = Bars.DateTime.Concat(new DateTime[] { Bars.DateTime[^1].AddMinutes(Bars.TF) }).ToArray();
                else Bars.DateTime = Bars.DateTime.Concat(new DateTime[] { DateTime.Now.Date.AddHours(DateTime.Now.Hour) }).ToArray();
                Bars.Open = Bars.Open.Concat(new double[] { LastTr.Price }).ToArray();
                Bars.High = Bars.High.Concat(new double[] { LastTr.Price }).ToArray();
                Bars.Low = Bars.Low.Concat(new double[] { LastTr.Price }).ToArray();
                Bars.Close = Bars.Close.Concat(new double[] { LastTr.Price }).ToArray();
                Bars.Volume = Bars.Volume.Concat(new double[] { LastTr.Quantity }).ToArray();

                // Пересчёт скриптов и запрос оригинальных баров
                System.Threading.Tasks.Task.Run(async () =>
                {
                    await System.Threading.Tasks.Task.Delay(250);
                    Order LastExecuted = Orders.ToArray().LastOrDefault(x => x.Seccode == MyTool.MySecurity.Seccode && x.Status == "matched");
                    if (LastExecuted != null && LastExecuted.DateTime.AddSeconds(3) > DateTime.Now)
                    {
                        AddInfo(MyTool.Name + ": Заявка исполнилась одновременно с открытием бара. Ожидание.", false);
                        await System.Threading.Tasks.Task.Delay(2000);
                    }
                    else if (MyTool.MySecurity.Seccode == Seccode)
                    {
                        Order[] Active = Orders.ToArray().Where(x => x.Seccode == Seccode && (x.Status is "active" or "watching")).ToArray();
                        if (Active.Any(x => Math.Abs(x.Price - LastTr.Price) < 0.00001))
                        {
                            AddInfo(MyTool.Name + ": Цена активной заявки равна цене открытия бара. Ожидание.", false);
                            await System.Threading.Tasks.Task.Delay(2000);
                        }
                    }

                    MyTool.Calculate(1.5);
                    MyTool.MainModel.InvalidatePlot(true);
                    RequestBars(MyTool);
                });
            }
        }
    } // Последняя сделка
    public double InitReqLong { get; set; } // Начальные требования Long
    public double InitReqShort { get; set; } // Начальные требования Short
    public double PointCost { get; set; } // Стоимость пункта цены
    public double MinStepCost { get; set; } // Стоимость шага цены
    public string TradingStatus { get; set; } // Состояние торговой сессии по инструменту

    public double RiskrateLong { get; set; } // Единая ставка риска Long
    public double ReserateLong { get; set; } // Единая ставка резерва Long
    public double RiskrateShort { get; set; } // Единая ставка риска Short
    public double ReserateShort { get; set; } // Единая ставка резерва Short

    public double MinPrice { get; set; } // Минимальная цена (FORTS)
    public double MaxPrice { get; set; } // Максимальная цена (FORTS)
    public double BuyDeposit { get; set; } // ГО покупателя (FORTS)
    public double SellDeposit { get; set; } // ГО продавца (FORTS)

    public Bars Bars { get; set; } // Сжатые бары с базовым ТФ
    public Bars SourceBars { get; set; } // Бары с исходным ТФ, полученные с сервера

    public Security() { }
    public Security(string Seccode) { this.Seccode = Seccode; }

    public void UpdateRequirements()
    {
        MinStepCost = PointCost * MinStep * Math.Pow(10, Decimals) / 100;
        if (Bars == null) System.Threading.Thread.Sleep(5000);
        if (Bars != null)
        {
            if (LastTrade == null) LastTrade = new Trade()
            {
                Price = Bars.Close[^1],
                DateTime = Bars.DateTime[^1]
            };
            double LastPrice = LastTrade.DateTime > Bars.DateTime[^1] ? LastTrade.Price : Bars.Close[^1];
            double Value = LastPrice * MinStepCost / MinStep * LotSize / 100;

            InitReqLong = Math.Round((RiskrateLong + ReserateLong) * Value, 2);
            InitReqShort = Math.Round((RiskrateShort + ReserateShort) * Value, 2);
        }
        else AddInfo("Не удалось обновить требования, потому что нет баров.");
    }
}
[Serializable] public class Settings : System.ComponentModel.INotifyPropertyChanged
{
    private int UpdInt = 5;
    private int RecInt = 30;

    private int ReqTM = 15;
    private int SesTM = 180;
    private bool SchedCon = true;

    private int TolEq = 40;
    private int TolPos = 3;

    private int MaxShInRePos = 10;
    private int MaxShInReTool = 25;
    private int MaxShMinRePort = 60;
    private int MaxShInRePort = 85;

    public List<string> ToolsByPriority { get; set; } // Приоритетность инструментов
    public int ModelUpdateInterval
    {
        get => UpdInt;
        set
        {
            if (value is < 1 or > 600)
            {
                AddInfo("ModelUpdateInterval должен быть от 1 до 600.");
                if (UpdInt is < 1 or > 600) UpdInt = 5;
                NotifyChanged();
                return;
            }
            UpdInt = value;
            NotifyChanged();
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
                NotifyChanged();
                return;
            }
            RecInt = value;
            if (RecInt > 60) AddInfo("RecalcInterval более 60 секнуд.");
            NotifyChanged();
        }
    } // Интервал пересчёта скриптов
    public bool ScheduledConnection
    {
        get => SchedCon;
        set
        {
            SchedCon = value;
            if (!SchedCon) AddInfo("Подключение по расписанию отключено.");
            NotifyChanged();
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
                NotifyChanged();
                return;
            }
            ReqTM = value;
            NotifyChanged();
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
                NotifyChanged();
                return;
            }
            SesTM = value;
            NotifyChanged();
        }
    } // Таймаут на переподключение к серверу без повторной закачки данных (120 по умолчанию)

    public string Email { get; set; } // Email для уведомлений
    public string EmailPassword { get; set; }

    public Dictionary<DateTime, int> Equity { get; set; } = new();
    public (DateTime, int) LastValueEquity
    {
        set
        {
            Equity[value.Item1] = value.Item2;
            NotifyChanged();
        }
    }
    public int AverageValueEquity
    {
        get
        {
            if (Equity == null || Equity.Count == 0) return 500000;
            if (Equity.Count > 2) return (int)Equity.TakeLast(3).Select(x => x.Value).Average();
            return Equity.Last().Value;
        }
    }

    public int ToleranceEquity
    {
        get => TolEq;
        set
        {
            if (value is < 1 or > 300)
            {
                AddInfo("ToleranceEquity должно быть от 1% до 300%.");
                if (TolEq is < 1 or > 300) TolEq = 40;
                NotifyChanged();
                return;
            }
            TolEq = value;
            if (TolEq > 50) AddInfo("ToleranceEquity более 50% от среднего значения.");
            NotifyChanged();
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
                NotifyChanged();
                return;
            }
            TolPos = value;
            if (TolPos > 3) AddInfo("TolerancePosition более 3x.");
            NotifyChanged();
        }
    } // Допустимое отклонение объёма текущей позиции в X от рассчитанного объёма

    public int MaxShareInitReqsPosition
    {
        get => MaxShInRePos;
        set
        {
            if (value is < 1 or > 50)
            {
                AddInfo("MaxShareInitReqsPosition должна быть от 1% до 50%.");
                if (MaxShInRePos is < 1 or > 50) MaxShInRePos = 15;
                NotifyChanged();
                return;
            }
            MaxShInRePos = value;
            if (MaxShInRePos > 15) AddInfo("MaxShareInitReqsPosition более 15%.");
            NotifyChanged();
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
                NotifyChanged();
                return;
            }
            MaxShInReTool = value;
            if (MaxShInReTool > 35) AddInfo("MaxShareInitReqsTool более 35%.");
            NotifyChanged();
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
                NotifyChanged();
                return;
            }
            MaxShMinRePort = value;
            if (MaxShMinRePort > 60) AddInfo("MaxShareMinReqsPortfolio более 60%.");
            NotifyChanged();
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
                NotifyChanged();
                return;
            }
            MaxShInRePort = value;
            if (MaxShInRePort > 90) AddInfo("MaxShareInitReqsPortfolio более 90%.");
            NotifyChanged();
        }
    } // Максимальная доля начальных требований портфеля

    public int ShelfLifeTrades { get; set; } = 60; // Срок хранения всех сделок (в днях)
    public int ShelfLifeOrdersScripts { get; set; } = 90; // Срок хранения заявок скриптов (в днях)
    public int ShelfLifeTradesScripts { get; set; } = 180; // Срок хранения сделок скриптов (в днях)

    [field: NonSerialized] public event System.ComponentModel.PropertyChangedEventHandler PropertyChanged;
    private void NotifyChanged() => PropertyChanged?.Invoke(this, new System.ComponentModel.PropertyChangedEventArgs(null));
    public Settings() { }
}

public class Position
{
    public string Market { get; set; }
    public string Seccode { get; set; }
    public string ShortName { get; set; }
    public double SaldoIn { get; set; }
    public double Saldo { get; set; }
    public double PL { get; set; } // Прибыль/убыток
    public double Amount { get; set; } // Текущая оценка стоимости позиции в валюте инструмента (за исключением FORTS)
    public double Equity { get; set; } // Текущая оценка стоимости позиции в рублях (за исключением FORTS)
    public Position() { }
    public Position(string Seccode) { this.Seccode = Seccode; }
}
public class UnitedPortfolio
{
    public string Union { get; set; } // Код юниона
    public double SaldoIn { get; set; } // Входящая оценка стоимости единого портфеля
    public double Saldo { get; set; } // Текущая оценка стоимости единого портфеля
    public double PL { get; set; } // Прибыль/убыток общий
    public double InitReqs { get; set; } // Начальные требования
    public double MinReqs { get; set; } // Минимальные требования
    public double Free { get; set; } // Свободные средства
    public double UnrealPL { get; set; } // Нереализованная прибыль/убыток
    public double GO { get; set; } // Размер требуемого ГО FORTS
    public double VarMargin { get; set; } // Вариационная маржа FORTS
    public double FinRes { get; set; } // Финансовый результат последнего клиринга FORTS
    public UnitedPortfolio() { }
}

public class Market
{
    public string ID { get; private set; }
    public string Name { get; private set; }
    public Market(string ID, string Name) { this.ID = ID; this.Name = Name; }
}
public class TimeFrame
{
    public string ID { get; private set; }
    public int Period { get; private set; }
    public string Name { get; private set; }
    public TimeFrame(string ID, int Period, string Name) { this.ID = ID; this.Period = Period; this.Name = Name; }
}
public class ClientAccount
{
    public string ID { get; private set; }
    public string Market { get; set; }
    public string Union { get; set; }
    public ClientAccount(string ID) { this.ID = ID; }
}

public static class Logger
{
    private static StreamWriter Writer;
    public static void StartLogging()
    {
        if (!Directory.Exists("Logs")) Directory.CreateDirectory("Logs");
        string Path = "Logs/" + DateTime.Today.ToShortDateString() + ".txt";

        Writer = new StreamWriter(Path, true, System.Text.Encoding.UTF8);
        WriteLogSystem("Start logging");
        Writer.Flush();
    }
    public static void WriteLogSystem(string Data)
    {
        Writer.WriteLine(DateTime.Now.ToString("dd.MM.yy HH:mm:ss.ffff", IC) + " " + Data);
        Writer.Flush();
    }
    public static void StopLogging()
    {
        WriteLogSystem("Stop logging");
        Writer.Close();
    }
}
