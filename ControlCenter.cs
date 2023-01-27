using System;
using System.Linq;
using System.Threading;
using System.Windows.Controls;
using System.Collections.ObjectModel;
using static ProSystem.MainWindow;
using static ProSystem.TXmlConnector;
namespace ProSystem;

public interface IScript
{
    string Name { get; set; }
    Order ActiveOrder { get; set; }
    Order LastExecuted { get; set; }
    PositionType CurrentPosition { get; set; }
    ScriptResult Result { get; set; }
    TextBlock BlockInfo { get; set; }

    ObservableCollection<Order> MyOrders { get; set; }
    ObservableCollection<Trade> MyTrades { get; set; }

    void Initialize(Tool MyTool, TabItem TabTool);
    void Calculate(Security Symbol);
}
public partial class Tool
{
    #region Fields
    private int Wait = 60;
    private double Share = 5;
    private int MinNumber = 0;
    private int MaxNumber = 2;
    private int Number = 1;
    private int BaseBal = 0;
    private DateTime TriggerPosition;

    private bool StopTr = true;
    private bool TradeSh = true;
    private bool UseNM = false;
    private bool UseShift = false;

    [field: NonSerialized] private Border BordSt;
    [field: NonSerialized] private TextBlock BlInfo;
    [field: NonSerialized] private TextBlock MainBlInfo;
    #endregion

    #region Properties
    public int WaitingLimit
    {
        get => Wait;
        set { Wait = value; NotifyChanged(); }
    }
    public double ShareOfFunds
    {
        get => Share;
        set { Share = value > 15 ? 5 : value; NotifyChanged(); }
    }
    public int MinNumberOfLots
    {
        get => MinNumber;
        set { MinNumber = value; NotifyChanged(); }
    }
    public int MaxNumberOfLots
    {
        get => MaxNumber;
        set { MaxNumber = value; NotifyChanged(); }
    }
    public int NumberOfLots
    {
        get => Number;
        set { Number = value; NotifyChanged(); }
    }
    public int BaseBalance
    {
        get => BaseBal;
        set { BaseBal = value; NotifyChanged(); }
    }

    public bool StopTrading
    {
        get => StopTr;
        set
        {
            StopTr = value;
            if (Active) BrushState = StopTr ? System.Windows.Media.Brushes.Yellow : System.Windows.Media.Brushes.Green;
            NotifyChanged();
        }
    }
    public bool TradeShare
    {
        get => TradeSh;
        set
        {
            TradeSh = value;
            Window.UpdateControlGrid(this);
            NotifyChanged();
        }
    }
    public bool UseNormalization
    {
        get => UseNM;
        set { UseNM = value; NotifyChanged(); }
    }
    public bool UseShiftBalance
    {
        get => UseShift;
        set
        {
            UseShift = value;
            Window.UpdateControlGrid(this);
            NotifyChanged();
        }
    }

    public Border BorderState
    {
        get => BordSt;
        set { BordSt = value; NotifyChanged(); }
    }
    public TextBlock BlockInfo
    {
        get => BlInfo;
        set { BlInfo = value; NotifyChanged(); }
    }
    public TextBlock MainBlockInfo
    {
        get => MainBlInfo;
        set { MainBlInfo = value; NotifyChanged(); }
    }
    #endregion

    #region Tool methods
    public void Calculate(double WaitingAfterLastRecalc = 3)
    {
        // Блокировка или ожидание освобождения блокировки
        while (Interlocked.Exchange(ref UsingMethod, 1) != 0)
        {
            AddInfo(Name + ": Calculate: Метод используется другим потоком", false);
            Thread.Sleep(500);
        }

        // Пересчёт или ожидание потенциальных данных с сервера
        while (DateTime.Now.AddSeconds(-WaitingAfterLastRecalc) < TimeLastRecalc)
        {
            AddInfo(Name + ": Calculate: Ожидание потенциальных данных с сервера", false);
            Thread.Sleep(250);
        }
        try
        {
            Calculate();
            AddInfo("Calculate: Выполнены скрипты инструмента: " + Name, false);
        }
        catch (Exception e)
        {
            AddInfo(Name + ": Calculate: Исключение: " + e.Message, notify: true);
            AddInfo("Трассировка стека: " + e.StackTrace);
            if (e.InnerException != null)
            {
                AddInfo("Внутреннее исключение: " + e.InnerException.Message);
                AddInfo("Трассировка стека внутреннего исключения: " + e.InnerException.StackTrace);
            }
        }

        // Обновление времени последнего пересчёта и следующего
        TimeLastRecalc = DateTime.Now;
        TimeNextRecalc = DateTime.Now.AddSeconds(MySettings.RecalcInterval / 2);

        // Освобождение блокировки
        Interlocked.Exchange(ref UsingMethod, 0);
    }
    private void Calculate()
    {
        if (!CheckTool()) return;
        Security Symbol = MySecurity;
        Security BasicSymbol = BasicSecurity ?? MySecurity;
        double[] Close = Symbol.Bars.Close;

        bool ReadyToTrade = !StopTrading;
        bool NowLogging = CheckNeedLogging();
        bool NowBidding = CheckStateSession();

        // Вычисление универсальных индикаторов
        double[] SmallATR = Indicators.ATR(Symbol.Bars.High, Symbol.Bars.Low, Symbol.Bars.Close, 150);
        double Average = Math.Round(Close[(Close.Length - 1 - 30)..(Close.Length - 1)].Average(), Symbol.Decimals);
        bool NormalPrice = Math.Abs(Average - Close[^1]) < SmallATR[^2] * 10;

        // Ожидание определённости заявок и позиции
        WaitCertainty();

        // Проверка портфеля, вычисление рублёвых требований и оптимальных объёмов позиций
        CheckPortfolio(ref ReadyToTrade);
        var RubReqs = GetAndCheckRubReqs(ref ReadyToTrade);
        (int Long, int Short) PosVolumes = GetPositionVolumes(RubReqs, out (double, double) ClearVolumes);

        // Получение текущей позиции и вычисление объёмов заявок
        int Balance = GetAndCheckBalance(PosVolumes, ref ReadyToTrade, ref TriggerPosition, out int RealBalance);
        (int Long, int Short) OrderVolumes = Scripts.Length == 1 || Balance == 0 ?
            (Math.Abs(Balance) + PosVolumes.Long, Math.Abs(Balance) + PosVolumes.Short) : (Math.Abs(Balance), Math.Abs(Balance));

        // Обновление информации на контрольной панели и логирование риск-параметров
        UpdateControlPanel(Balance, RealBalance, NowBidding, ReadyToTrade,
            RubReqs, ClearVolumes, PosVolumes, OrderVolumes, Average, SmallATR[^2]);
        if (NowLogging) WriteLogRisks(Balance, RealBalance, StopTrading,
            NowBidding, ReadyToTrade, RubReqs, ClearVolumes, PosVolumes, OrderVolumes);

        // Идентификация заявок, проверка соответствия общей позиции позициям скриптов и нормализация общей позиции
        IdentifyOrdersAndTrades();
        if (!StopTrading)
        {
            if (!CancelUnknownsOrders()) return;
            if (ReadyToTrade)
            {
                foreach (IScript MyScript in Scripts) MyScript.UpdateOrdersAndPosition();
                if (!CheckPositionMatching(Balance, PosVolumes, NowBidding, NormalPrice)) return;
                NormalizePosition(Balance, PosVolumes, ClearVolumes, NowBidding);
            }
        }
        else if (!CancelActiveOrders()) return;

        // Работа со скриптами
        foreach (IScript MyScript in Scripts)
        {
            // Обновление заявок и позиции скрипта, вычисление индикаторов на основе базисного актива
            if (!MyScript.UpdateOrdersAndPosition()) continue;
            MyScript.Calculate(BasicSymbol);

            // Обновление моделей, информационной панели скрипта и логирование
            UpdateModelsAndPanel(MyScript);
            if (NowLogging) WriteLogScript(MyScript);

            // Выравнивание данных и проверка условий для выхода
            if (BasicSymbol != Symbol && !AlignData(MyScript)) continue;
            if (!ReadyToTrade || MyScript.Result.Type != ScriptType.StopLine && !NowBidding) continue;

            // Работа с заявками
            if (MyScript.Result.Type is ScriptType.OSC or ScriptType.Line) ProcessOrders(MyScript, OrderVolumes, NormalPrice);
            else if (MyScript.Result.Type is ScriptType.LimitLine) ProcessOrdersLimitLine(MyScript, OrderVolumes);
            else if (MyScript.Result.Type is ScriptType.StopLine)
                ProcessOrdersStopLine(MyScript, OrderVolumes, NormalPrice, NowBidding, SmallATR);
            else AddInfo(MyScript.Name + ": Неизвестный тип скрипта: " + MyScript.Result.Type);
        }
    }

    private void IdentifyOrdersAndTrades()
    {
        Order[] UnknownsOrders = Orders.ToArray().Where(x => x.Sender == null && x.Seccode == MySecurity.Seccode).ToArray();
        foreach (Order UnknowOrder in UnknownsOrders)
        {
            foreach (IScript MyScript in Scripts)
            {
                int i = Array.FindIndex(MyScript.MyOrders.ToArray(), x => x.TrID == UnknowOrder.TrID);
                if (i > -1)
                {
                    if (UnknowOrder.Status == MyScript.MyOrders[i].Status) UnknowOrder.DateTime = MyScript.MyOrders[i].DateTime;
                    UnknowOrder.Sender = MyScript.MyOrders[i].Sender;
                    UnknowOrder.Signal = MyScript.MyOrders[i].Signal;
                    UnknowOrder.Note = MyScript.MyOrders[i].Note;
                    Window.Dispatcher.Invoke(() => MyScript.MyOrders[i] = UnknowOrder);
                    break;
                }
            }

            if (UnknowOrder.Sender == null)
            {
                int i = Array.FindIndex(SystemOrders.ToArray(), x => x.TrID == UnknowOrder.TrID);
                if (i > -1)
                {
                    UnknowOrder.Sender = SystemOrders[i].Sender;
                    UnknowOrder.Signal = SystemOrders[i].Signal;
                    UnknowOrder.Note = SystemOrders[i].Note;
                    Window.Dispatcher.Invoke(() => SystemOrders[i] = UnknowOrder);
                }
            }
        }

        Trade[] UnknownsTrades = Trades.ToArray().Where(x => x.SenderOrder == null && x.Seccode == MySecurity.Seccode).ToArray();
        foreach (Trade UnknowTrade in UnknownsTrades)
        {
            foreach (IScript MyScript in Scripts)
            {
                int i = Array.FindIndex(MyScript.MyOrders.ToArray(), x => x.OrderNo == UnknowTrade.OrderNo);
                if (i > -1)
                {
                    UnknowTrade.SenderOrder = MyScript.MyOrders[i].Sender;
                    UnknowTrade.SignalOrder = MyScript.MyOrders[i].Signal;
                    UnknowTrade.NoteOrder = MyScript.MyOrders[i].Note;
                    Window.Dispatcher.Invoke(() => MyScript.MyTrades.Add(UnknowTrade));
                    break;
                }
            }

            if (UnknowTrade.SenderOrder == null)
            {
                int i = Array.FindIndex(SystemOrders.ToArray(), x => x.OrderNo == UnknowTrade.OrderNo);
                if (i > -1)
                {
                    UnknowTrade.SenderOrder = SystemOrders[i].Sender;
                    UnknowTrade.SignalOrder = SystemOrders[i].Signal;
                    UnknowTrade.NoteOrder = SystemOrders[i].Note;
                    Window.Dispatcher.Invoke(() => SystemTrades.Add(UnknowTrade));
                }
            }
        }
    }
    private bool CheckTool()
    {
        if (MySecurity.LastTrade == null || MySecurity.LastTrade.DateTime < DateTime.Now.AddDays(-5))
        {
            AddInfo(Name + ": Последняя сделка не актуальна или её не существует. Подписка на сделки и выход.", notify: true);
            SubUnsub(true, MySecurity.Board, MySecurity.Seccode, true, false, false);
            return false;
        }
        else if (MySecurity.Bars == null || BasicSecurity != null && BasicSecurity.Bars == null)
        {
            AddInfo(Name + ": Базовых баров не существует. Запрос баров и выход.", notify: true);
            RequestBars(this);
            return false;
        }
        else if (BasicSecurity == null && MySecurity.Bars.Close.Length < 200 ||
            BasicSecurity != null && (MySecurity.Bars.Close.Length < 200 || BasicSecurity.Bars.Close.Length < 200))
        {
            string Counts = MySecurity.Bars.Close.Length.ToString();
            if (BasicSecurity != null) Counts += "/" + BasicSecurity.Bars.Close.Length;
            AddInfo(Name + ": Недостаточно базовых баров: " + Counts + " Запрос баров и выход.", notify: true);
            RequestBars(this);
            return false;
        }
        else if (Scripts.Length > 2)
        {
            AddInfo(Name + ": Непредвиденное количество скриптов: " + Scripts.Length, notify: true);
            return false;
        }
        return true;
    }
    private bool CheckStateSession()
    {
        if (DateTime.Now > DateTime.Today.AddMinutes(839).AddSeconds(55) && DateTime.Now < DateTime.Today.AddMinutes(845)) { }
        else if (DateTime.Now > DateTime.Today.AddMinutes(1124).AddSeconds(55) && DateTime.Now < DateTime.Today.AddMinutes(1145))
        {
            if (MySecurity.LastTrade.DateTime > DateTime.Today.AddMinutes(1125)) return true;
        }
        else if (DateTime.Now < DateTime.Today.AddHours(1)) { }
        else if (MySecurity.LastTrade.DateTime.AddHours(1) > DateTime.Now) return true;
        return false;
    }
    private bool CheckNeedLogging()
    {
        if (DateTime.Now.Second >= 30 &&
            (DateTime.Now.Minute == 0 || DateTime.Now.Minute == 29 || DateTime.Now.Minute == 30 || DateTime.Now.Minute == 59))
        {
            try
            {
                string Path = "Logs/LogsTools/" + Name + ".txt";
                if (!System.IO.File.Exists(Path)) System.IO.File.Create(Path).Close();

                string Data = DateTime.Now.ToString(IC) + ": /////////////////// RECOUNT SCRIPTS" +
                    "\nLastTrade " + MySecurity.LastTrade.Price.ToString(IC) +
                    "\nDateLastTrade " + MySecurity.LastTrade.DateTime.ToString(IC) + "\n";

                if (MySecurity.Bars != null)
                    Data += "OHLCV[^1] " + MySecurity.Bars.DateTime[^1] + "/" +
                        MySecurity.Bars.Open[^1] + "/" + MySecurity.Bars.High[^1] + "/" +
                        MySecurity.Bars.Low[^1] + "/" + MySecurity.Bars.Close[^1] + "/" + MySecurity.Bars.Volume[^1] + "\n";

                if (BasicSecurity != null && BasicSecurity.Bars != null)
                    Data += "BasicOHLCV[^1] " + BasicSecurity.Bars.DateTime[^1] + "/" +
                        BasicSecurity.Bars.Open[^1] + "/" + BasicSecurity.Bars.High[^1] + "/" +
                        BasicSecurity.Bars.Low[^1] + "/" + BasicSecurity.Bars.Close[^1] + "/" + BasicSecurity.Bars.Volume[^1] + "\n";

                System.IO.File.AppendAllText(Path, Data);
                return true;
            }
            catch (Exception e) { AddInfo(Name + ": Исключение логирования: " + e.Message); }
        }
        return false;
    }
    private void WaitCertainty()
    {
        Order[] Undefined =
            Orders.ToArray().Where(x => x.Seccode == MySecurity.Seccode && (x.Status is "forwarding" or "inactive")).ToArray();
        if (Undefined.Length > 0)
        {
            AddInfo(Name + ": Неопределённый статус заявки: " + Undefined[0].Status);

            Thread.Sleep(500);
            if (!Undefined.Where(x => x.Status is "forwarding" or "inactive").Any()) return;

            Thread.Sleep(1000);
            if (!Undefined.Where(x => x.Status is "forwarding" or "inactive").Any()) return;

            Thread.Sleep(1500);
            if (!Undefined.Where(x => x.Status is "forwarding" or "inactive").Any()) return;

            AddInfo(Name + ": Не удалось вовремя получить определённый статус заявки.");
        }

        Trade LastTrade = Trades.ToArray().LastOrDefault(x => x.Seccode == MySecurity.Seccode);
        if (LastTrade != null && LastTrade.DateTime.AddSeconds(2) > DateTime.Now) Thread.Sleep(1500);
    }
    private static void CheckPortfolio(ref bool ReadyToTrade)
    {
        if (DateTime.Now > DateTime.Today.AddMinutes(840) && DateTime.Now < DateTime.Today.AddMinutes(845)) return;
        if (!Portfolio.CheckEquity(MySettings.ToleranceEquity)) ReadyToTrade = false;
    }

    private (double, double) GetAndCheckRubReqs(ref bool ReadyToTrade)
    {
        (double, double) RubReqs = (0, 0);
        if (MySecurity.Market != "7") RubReqs = (MySecurity.InitReqLong, MySecurity.InitReqShort);
        else
        {
            if (USDRUB < 0.1 || EURRUB < 0.1)
            {
                AddInfo(Name + ": Запрос USDRUB и EURRUB", notify: true);
                GetHistoryData("CETS", "USD000UTSTOM", "1", 1);
                GetHistoryData("CETS", "EUR_RUB__TOM", "1", 1);
                ReadyToTrade = false;
            }
            else if (MySecurity.Currency == "USD") RubReqs = (MySecurity.InitReqLong * USDRUB, MySecurity.InitReqShort * USDRUB);
            else if (MySecurity.Currency == "EUR") RubReqs = (MySecurity.InitReqLong * EURRUB, MySecurity.InitReqShort * EURRUB);
            else
            {
                AddInfo(Name + ": Неизвестная валюта: " + MySecurity.Currency, notify: true);
                ReadyToTrade = false;
            }
        }

        if (RubReqs.Item1 < 10 || RubReqs.Item2 < 10 || MySecurity.SellDeposit < 10 || RubReqs.Item1 < MySecurity.SellDeposit / 2)
        {
            AddInfo(Name + ": Требования за пределами нормы: " +
                RubReqs.Item1 + "/" + RubReqs.Item2 + " SellDep: " + MySecurity.SellDeposit, true, true);
            GetSecurityInfo(MySecurity.Market, MySecurity.Seccode);
            GetClnSecPermissions(MySecurity.Board, MySecurity.Seccode, MySecurity.Market);
            ReadyToTrade = false;
        }
        return RubReqs;
    }
    private (int, int) GetPositionVolumes((double, double) RubReqs, out (double, double) ClearVolumes)
    {
        int LongVolume, ShortVolume;
        double MaxShare = Portfolio.Saldo / 100 * MySettings.MaxShareInitReqsPosition;

        if (TradeShare)
        {
            double OptShare = Portfolio.Saldo / 100 * ShareOfFunds;
            if (OptShare > MaxShare)
            {
                AddInfo(Name + ": ShareOfFunds превышает допустимый объём риска: " +
                    MySettings.MaxShareInitReqsPosition.ToString(IC) + "%", MySettings.DisplaySpecialInfo, true);
                OptShare = MaxShare;
            }

            LongVolume = (int)Math.Floor(OptShare / RubReqs.Item1);
            if (LongVolume > MaxNumberOfLots) LongVolume = MaxNumberOfLots;
            if (LongVolume < MinNumberOfLots) LongVolume = MinNumberOfLots;

            ShortVolume = (int)Math.Floor(OptShare / RubReqs.Item2);
            if (ShortVolume > MaxNumberOfLots) ShortVolume = MaxNumberOfLots;
            if (ShortVolume < MinNumberOfLots) ShortVolume = MinNumberOfLots;

            if (LongVolume * RubReqs.Item1 > MaxShare)
            {
                AddInfo(Name + ": LongVolume превышает допустимый объём риска: " +
                    MySettings.MaxShareInitReqsPosition.ToString(IC) + "%", MySettings.DisplaySpecialInfo, true);
                LongVolume = (int)Math.Floor(OptShare / RubReqs.Item1);
            }
            if (ShortVolume * RubReqs.Item2 > MaxShare)
            {
                AddInfo(Name + ": ShortVolume превышает допустимый объём риска: " +
                    MySettings.MaxShareInitReqsPosition.ToString(IC) + "%", MySettings.DisplaySpecialInfo, true);
                ShortVolume = (int)Math.Floor(OptShare / RubReqs.Item2);
            }
        }
        else if (NumberOfLots * Math.Max(RubReqs.Item1, RubReqs.Item2) > MaxShare)
        {
            AddInfo(Name + ": NumberOfLots превышает допустимый объём риска.", notify: true);
            LongVolume = (int)Math.Floor(MaxShare / RubReqs.Item1);
            ShortVolume = (int)Math.Floor(MaxShare / RubReqs.Item2);
        }
        else
        {
            LongVolume = NumberOfLots;
            ShortVolume = NumberOfLots;
        }

        ClearVolumes = (Math.Round(Portfolio.Saldo * 0.01 * ShareOfFunds / RubReqs.Item1, 2),
            Math.Round(Portfolio.Saldo * 0.01 * ShareOfFunds / RubReqs.Item2, 2));
        return (LongVolume, ShortVolume);
    }
    private int GetAndCheckBalance((int, int) PosVolumes, ref bool ReadyToTrade, ref DateTime TriggerPosition, out int RealBalance)
    {
        int Balance = 0;
        Position MyPosition = Portfolio.Positions.ToArray().SingleOrDefault(x => x.Seccode == MySecurity.Seccode);
        if (MyPosition != null) Balance = (int)MyPosition.Saldo;

        RealBalance = Balance;
        if (UseShiftBalance) Balance -= BaseBalance;

        if (Math.Abs(Balance) > Math.Max(Math.Max(PosVolumes.Item1, PosVolumes.Item2), 1) * MySettings.TolerancePosition)
        {
            if (DateTime.Today.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday)
            {
                ReadyToTrade = false;
                if (TriggerPosition == DateTime.MinValue)
                {
                    AddInfo(Name + ": Объём текущей позиции за пределами допустимого отклонения. Ожидание.", notify: true);
                    TriggerPosition = DateTime.Now.AddHours(12);
                }
            }
            else
            {
                AddInfo(Name + ": Объём текущей позиции за пределами допустимого отклонения. Ожидание.", notify: true);
                if (TriggerPosition == DateTime.MinValue)
                {
                    TriggerPosition = DateTime.Now.AddHours(4);
                    ReadyToTrade = false;
                }
                else if (DateTime.Now < TriggerPosition) ReadyToTrade = false;
                else AddInfo(Name + ": Объём текущей позиции всё ещё за пределами допустимого отклонения, но торговля разрешена.");
            }
        }
        else if (TriggerPosition != DateTime.MinValue) TriggerPosition = DateTime.MinValue;
        return Balance;
    }
    private void NormalizePosition(int Balance, (int, int) PosVolumes, (double, double) ClearVolumes, bool NowBidding)
    {
        Order[] ActiveOrders = Orders.ToArray()
            .Where(x => x.Sender == "System" && x.Seccode == MySecurity.Seccode && (x.Status is "active" or "watching")).ToArray();

        if (ActiveOrders.Length == 0 && !UseNormalization || !NowBidding) return;
        if (ActiveOrders.Length > 1)
        {
            AddInfo(Name + ": Отмена нескольких активных заявок System: " + ActiveOrders.Length);
            foreach (Order MyOrder in ActiveOrders) CancelOrder(MyOrder);
            return;
        }

        bool NeedToNormalizeUp =
            Balance > 0 && Balance + Math.Ceiling(Balance * 0.04) + 0.2 < ClearVolumes.Item1 ||
            Balance < 0 && -Balance + Math.Ceiling(-Balance * 0.04) + 0.2 < ClearVolumes.Item2;

        Order ActiveOrder = ActiveOrders.SingleOrDefault();
        if (ActiveOrder != null)
        {
            if ((ActiveOrder.BuySell == "B") == Balance < 0 && (Balance > PosVolumes.Item1 || -Balance > PosVolumes.Item2))
            {
                foreach (IScript MyScript in Scripts)
                    if (MyScript.ActiveOrder != null && Math.Abs(MyScript.ActiveOrder.Price - MySecurity.Bars.Close[^2]) < 0.00001)
                    {
                        AddInfo(Name +
                            ": Отмена заявки для нормализации, скрипт уже выставил заявку с ценой закрытия прошлого бара.");
                        CancelOrder(ActiveOrder);
                        return;
                    }

                int Volume = Balance > PosVolumes.Item1 ? Balance - PosVolumes.Item1 : -Balance - PosVolumes.Item2;
                if (Math.Abs(ActiveOrder.Price - MySecurity.Bars.Close[^2]) > 0.00001 &&
                    DateTime.Now.Minute != 0 && DateTime.Now.Minute != 30 || ActiveOrder.Balance != Volume)
                    ReplaceOrder(ActiveOrder, MySecurity, OrderType.Limit,
                        MySecurity.Bars.Close[^2], Volume, "Normalization", null, "NM");
            }
            else if ((ActiveOrder.BuySell == "B") == Balance > 0 && NeedToNormalizeUp)
            {
                foreach (IScript MyScript in Scripts)
                    if (MyScript.ActiveOrder != null && Math.Abs(MyScript.ActiveOrder.Price - MySecurity.Bars.Close[^2]) < 0.00001)
                    {
                        AddInfo(Name +
                            ": Отмена заявки для нормализации, скрипт уже выставил заявку с ценой закрытия прошлого бара.");
                        CancelOrder(ActiveOrder);
                        return;
                    }

                int Volume = Balance > 0 ? PosVolumes.Item1 - Balance : PosVolumes.Item2 + Balance;
                if (Math.Abs(ActiveOrder.Price - MySecurity.Bars.Close[^2]) > 0.00001 &&
                    DateTime.Now.Minute != 0 && DateTime.Now.Minute != 30 || ActiveOrder.Balance != Volume)
                    ReplaceOrder(ActiveOrder, MySecurity,
                        OrderType.Limit, MySecurity.Bars.Close[^2], Volume, "NormalizationUp", null, "NM");
            }
            else CancelOrder(ActiveOrder);
        }
        else if (Balance > PosVolumes.Item1 || -Balance > PosVolumes.Item2)
        {
            foreach (IScript MyScript in Scripts)
                if (MyScript.ActiveOrder != null && Math.Abs(MyScript.ActiveOrder.Price - MySecurity.Bars.Close[^2]) < 0.00001)
                {
                    AddInfo(Name + ": Требуется нормализация, но скрипт уже выставил заявку с ценой закрытия прошлого бара.",
                        MySettings.DisplaySpecialInfo);
                    return;
                }

            int Volume = Balance > PosVolumes.Item1 ? Balance - PosVolumes.Item1 : -Balance - PosVolumes.Item2;
            SendOrder(MySecurity, OrderType.Limit, Balance < 0, MySecurity.Bars.Close[^2], Volume, "Normalization", null, "NM");
            WriteLogNM(Balance, PosVolumes);
        }
        else if (NeedToNormalizeUp)
        {
            foreach (IScript MyScript in Scripts)
                if (MyScript.ActiveOrder != null &&
                    (MyScript.ActiveOrder.Quantity - MyScript.ActiveOrder.Balance > 0.00001 || MyScript.ActiveOrder.Note == "PartEx" ||
                    Math.Abs(MyScript.ActiveOrder.Price - MySecurity.Bars.Close[^2]) < 0.00001)) return;

            foreach (IScript MyScript in Scripts)
            {
                Order LastExecuted = MyScript.MyOrders.LastOrDefault(x => x.Status == "matched");
                if (LastExecuted != null && LastExecuted.DateTime.AddDays(4) > DateTime.Now)
                {
                    int Volume = Balance > 0 ? PosVolumes.Item1 - Balance : PosVolumes.Item2 + Balance;
                    SendOrder(MySecurity, OrderType.Limit,
                        Balance > 0, MySecurity.Bars.Close[^2], Volume, "NormalizationUp", null, "NM");
                    WriteLogNM(Balance, PosVolumes);
                    return;
                }
            }
        }
    }
    private bool CheckPositionMatching(int Balance, (int, int) PosVolumes, bool NowBidding, bool NormalPrice)
    {
        PositionType Long = PositionType.Long;
        PositionType Short = PositionType.Short;
        PositionType Neutral = PositionType.Neutral;

        if (Scripts.Length == 1)
        {
            // Проверка частичного исполнения заявки
            if (Scripts[0].ActiveOrder != null && (Scripts[0].ActiveOrder.Quantity - Scripts[0].ActiveOrder.Balance > 0.00001 ||
                Scripts[0].ActiveOrder.Note == "PartEx")) return true;

            // Проверка соответствия позиций
            PositionType CurPosition = Scripts[0].CurrentPosition;
            if (CurPosition == Neutral && Balance != 0 ||
                CurPosition == Long && Balance <= 0 || CurPosition == Short && Balance >= 0)
            {
                if (!NowBidding || !NormalPrice)
                {
                    AddInfo(Name + ": Несоответствие позиции, но торги не ведутся или цена за пределами нормы.",
                        MySettings.DisplaySpecialInfo, true);
                    return true;
                }
                AddInfo(Name + ": Позиция скрипта не соответствует позиции в портфеле. Нормализация по рынку.", notify: true);

                Order[] ActiveOrders =
                    Orders.ToArray().Where(x => x.Seccode == MySecurity.Seccode && (x.Status is "active" or "watching")).ToArray();
                foreach (Order MyOrder in ActiveOrders) CancelOrder(MyOrder);

                int VolumeOrder;
                bool IsBuy = CurPosition == Neutral ? Balance < 0 : CurPosition == Long;
                if (CurPosition == Long) VolumeOrder = Math.Abs(Balance) + PosVolumes.Item1;
                else if (CurPosition == Short) VolumeOrder = Math.Abs(Balance) + PosVolumes.Item2;
                else VolumeOrder = Math.Abs(Balance);

                SendOrder(MySecurity, OrderType.Market,
                    IsBuy, MySecurity.Bars.Close[^2], VolumeOrder, "BringingIntoLine", null, "NM");
                return false;
            }
        }
        else if (Scripts.Length == 2)
        {
            // Проверка частичного исполнения заявок
            foreach (IScript MyScript in Scripts)
                if (MyScript.ActiveOrder != null &&
                    (MyScript.ActiveOrder.Quantity - MyScript.ActiveOrder.Balance > 0.00001 ||
                    MyScript.ActiveOrder.Note == "PartEx")) return true;

            // Проверка соответствия позиций
            PositionType CurPosition1 = Scripts[0].CurrentPosition;
            PositionType CurPosition2 = Scripts[1].CurrentPosition;
            if (CurPosition1 == CurPosition2)
            {
                if (CurPosition1 == Neutral && Balance != 0 ||
                    CurPosition1 == Long && Balance <= 0 || CurPosition1 == Short && Balance >= 0)
                {
                    if (!NowBidding || !NormalPrice)
                    {
                        AddInfo(Name + ": Несоответствие позиции, но торги не ведутся или цена за пределами нормы.",
                            MySettings.DisplaySpecialInfo, true);
                        return true;
                    }
                    AddInfo(Name +
                        ": Текущие позиции скриптов не соответствуют позиции в портфеле. Нормализация по рынку.", notify: true);

                    Order[] ActiveOrders = Orders.ToArray()
                        .Where(x => x.Seccode == MySecurity.Seccode && (x.Status is "active" or "watching")).ToArray();
                    foreach (Order MyOrder in ActiveOrders) CancelOrder(MyOrder);

                    int VolumeOrder;
                    bool IsBuy = CurPosition1 == Neutral ? Balance < 0 : CurPosition1 == Long;
                    if (CurPosition1 == Long) VolumeOrder = Math.Abs(Balance) + PosVolumes.Item1;
                    else if (CurPosition1 == Short) VolumeOrder = Math.Abs(Balance) + PosVolumes.Item2;
                    else VolumeOrder = Math.Abs(Balance);

                    SendOrder(MySecurity, OrderType.Market,
                        IsBuy, MySecurity.Bars.Close[^2], VolumeOrder, "BringingIntoLine", null, "NM");
                    return false;
                }
            }
            else if (CurPosition1 == Long && CurPosition2 == Short || CurPosition1 == Short && CurPosition2 == Long)
            {
                if (Balance != 0)
                {
                    if (!NowBidding || !NormalPrice)
                    {
                        AddInfo(Name + ": Несоответствие позиции, но торги не ведутся или цена за пределами нормы.",
                            MySettings.DisplaySpecialInfo, true);
                        return true;
                    }
                    AddInfo(Name +
                        ": Текущие позиции скриптов не соответствуют позиции в портфеле. Нормализация по рынку.", notify: true);

                    Order[] ActiveOrders = Orders.ToArray()
                        .Where(x => x.Seccode == MySecurity.Seccode && (x.Status is "active" or "watching")).ToArray();
                    foreach (Order MyOrder in ActiveOrders) CancelOrder(MyOrder);

                    SendOrder(MySecurity, OrderType.Market,
                        Balance < 0, MySecurity.Bars.Close[^2], Math.Abs(Balance), "BringingIntoLine", null, "NM");
                    return false;
                }
            }
            else // Одна из позиций Neutral
            {
                PositionType CurPos = CurPosition1 == Neutral ? CurPosition2 : CurPosition1;
                if (CurPos == Long && Balance <= 0 || CurPos == Short && Balance >= 0)
                {
                    if (!NowBidding || !NormalPrice)
                    {
                        AddInfo(Name + ": Несоответствие позиции, но торги не ведутся или цена за пределами нормы.",
                            MySettings.DisplaySpecialInfo, true);
                        return true;
                    }
                    AddInfo(Name +
                        ": Текущие позиции скриптов не соответствуют позиции в портфеле. Нормализация по рынку.", notify: true);

                    Order[] ActiveOrders = Orders.ToArray()
                        .Where(x => x.Seccode == MySecurity.Seccode && (x.Status is "active" or "watching")).ToArray();
                    foreach (Order MyOrder in ActiveOrders) CancelOrder(MyOrder);

                    int VolumeOrder = CurPos == Long ? Math.Abs(Balance) + PosVolumes.Item1 : Math.Abs(Balance) + PosVolumes.Item2;
                    SendOrder(MySecurity, OrderType.Market,
                        CurPos == Long, MySecurity.Bars.Close[^2], VolumeOrder, "BringingIntoLine", null, "NM");
                    return false;
                }
            }
        }
        return true;
    }

    private bool CancelActiveOrders()
    {
        Order[] ActiveOrders = Orders.ToArray()
            .Where(x => x.Seccode == MySecurity.Seccode && (x.Status is "active" or "watching")).ToArray();
        if (ActiveOrders.Length == 0) return true;

        AddInfo(Name + ": Отмена всех активных заявок: " + ActiveOrders.Length);
        foreach (Order MyOrder in ActiveOrders) CancelOrder(MyOrder);

        Thread.Sleep(500);
        if (!ActiveOrders.Where(x => x.Status is "active" or "watching").Any()) return true;

        Thread.Sleep(1000);
        if (!ActiveOrders.Where(x => x.Status is "active" or "watching").Any()) return true;

        Thread.Sleep(1500);
        if (!ActiveOrders.Where(x => x.Status is "active" or "watching").Any()) return true;

        Thread.Sleep(2000);
        if (!ActiveOrders.Where(x => x.Status is "active" or "watching").Any()) return true;

        AddInfo(Name + ": Не удалось вовремя отменить все активные заявки");
        return false;
    }
    private bool CancelUnknownsOrders()
    {
        Order[] Unknowns = Orders.ToArray()
            .Where(x => x.Sender == null && x.Seccode == MySecurity.Seccode && (x.Status is "active" or "watching")).ToArray();
        if (Unknowns.Length == 0) return true;

        AddInfo(Name + ": Отмена неизвестных активных заявок: " + Unknowns.Length);
        foreach (Order MyOrder in Unknowns) CancelOrder(MyOrder);

        Thread.Sleep(500);
        if (!Unknowns.Where(x => x.Status is "active" or "watching").Any()) return true;

        Thread.Sleep(1000);
        if (!Unknowns.Where(x => x.Status is "active" or "watching").Any()) return true;

        Thread.Sleep(1500);
        if (!Unknowns.Where(x => x.Status is "active" or "watching").Any()) return true;

        Thread.Sleep(2000);
        if (!Unknowns.Where(x => x.Status is "active" or "watching").Any()) return true;

        AddInfo(Name + ": Не удалось вовремя отменить неизвестные активные заявки");
        return false;
    }

    private void UpdateControlPanel(int Balance, int RealBalance, bool NowBidding, bool ReadyToTrade, (double, double) RubReqs,
        (double, double) ClearVols, (int, int) PosVolumes, (int, int) OrderVolumes, double Average, double SmallATR)
    {
        (MainModel.Series[0] as OxyPlot.Series.CandleStickSeries).DecreasingColor =
            NowBidding && (!ShowBasicSecurity || ShowBasicSecurity && BasicSecurity.LastTrade.DateTime.AddHours(2) > DateTime.Now) ?
            Theme.RedBar : Theme.FadedBar;
        MainModel.InvalidatePlot(false);

        Window.Dispatcher.Invoke(() =>
        {
            BorderState.Background = ReadyToTrade ? System.Windows.Media.Brushes.LightGreen : System.Windows.Media.Brushes.Yellow;

            MainBlockInfo.Text = "\nReq " + Math.Round(RubReqs.Item1) + "/" + Math.Round(RubReqs.Item2) +
            "\nVols " + PosVolumes.Item1 + "/" + PosVolumes.Item2 + "\nOrderVols " + OrderVolumes.Item1 + "/" + OrderVolumes.Item2 +
            "\nLastTr " + MySecurity.LastTrade.DateTime.TimeOfDay.ToString();

            BlockInfo.Text = "\nBal/Real " + Balance + "/" + RealBalance + "\nClearV " + ClearVols.Item1 + "/" + ClearVols.Item2 +
            "\nSMA " + Average + "\n10ATR " + Math.Round(SmallATR * 10, MySecurity.Decimals);
        });
    }
    private void WriteLogRisks(int Balance, int RealBalance, bool StopTrading, bool NowBidding, bool ReadyToTrade,
        (double, double) RubReqs, (double, double) ClearVols, (double, double) PosVolumes, (double, double) OrderVolumes)
    {
        try
        {
            System.IO.File.AppendAllText("Logs/LogsTools/" + Name + ".txt", DateTime.Now + ": /////////////////// Risks" +
                "\nBalance " + Balance + "\nRealBalance " + RealBalance +
                "\nUseShiftBalance " + UseShiftBalance + "\nBaseBalance " + BaseBalance +
                "\nStopTrading " + StopTrading + "\nNowBidding " + NowBidding + "\nReadyToTrade " + ReadyToTrade +
                "\nPortfolio.Saldo " + Portfolio.Saldo + "\nShareOfFunds " + ShareOfFunds +
                "\nRubReqs " + RubReqs.Item1 + "/" + RubReqs.Item2 + "\nClearVols " + ClearVols.Item1 + "/" + ClearVols.Item2 +
                "\nPosVols " + PosVolumes.Item1 + "/" + PosVolumes.Item2 +
                "\nOrderVol " + OrderVolumes.Item1 + "/" + OrderVolumes.Item2 +
                "\nMinLots " + MinNumberOfLots + "\nMaxLots " + MaxNumberOfLots + "\n");
        }
        catch (Exception e) { AddInfo(Name + ": Исключение логирования рисков: " + e.Message); }
    }
    private void WriteLogNM(int Balance, (int, int) PosVolumes)
    {
        try
        {
            System.IO.File.AppendAllText("Logs/LogsTools/" + Name + ".txt", DateTime.Now + ": /////////////////// NM" +
                "\nBalance " + Balance + "\nUseShiftBalance " + UseShiftBalance + "\nBaseBalance " + BaseBalance +
                "\nReserateLong " + MySecurity.ReserateLong + "\nReserateShort " + MySecurity.ReserateShort +
                "\nInitReqLong " + MySecurity.InitReqLong + "\nInitReqShort " + MySecurity.InitReqShort +
                "\nPortfolio.Saldo " + Portfolio.Saldo + "\nShareOfFunds " + ShareOfFunds +
                "\nPosVols " + PosVolumes.Item1 + "/" + PosVolumes.Item2 +
                "\nMinLots " + MinNumberOfLots + "\nMaxLots " + MaxNumberOfLots + "\n");
        }
        catch (Exception e) { AddInfo(Name + ": Исключение логирования NM: " + e.Message); }
    }
    #endregion

    #region Script methods
    private void ProcessOrders(IScript MyScript, (int, int) OrderVolumes, bool NormalPrice)
    {
        Security Symbol = MySecurity;
        double PrevClose =
            BasicSecurity?.Bars.DateTime[^1] > MySecurity.Bars.DateTime[^1] ? Symbol.Bars.Close[^1] : Symbol.Bars.Close[^2];
        int VolumeOrder = MyScript.CurrentPosition == PositionType.Long ? OrderVolumes.Item2 : OrderVolumes.Item1;

        bool[] IsGrow = MyScript.Result.IsGrow;
        Order ActiveOrder = MyScript.ActiveOrder;
        Order LastExecuted = MyScript.LastExecuted;

        // Работа с активной заявкой
        if (ActiveOrder != null)
        {
            if (MyScript.Result.OnlyLimit)
            {
                if (ActiveOrder.Quantity - ActiveOrder.Balance > 0.00001 || ActiveOrder.Note == "PartEx")
                {
                    if (Math.Abs(ActiveOrder.Price - PrevClose) > 0.00001)
                        ReplaceOrder(ActiveOrder, Symbol, OrderType.Limit,
                            PrevClose, ActiveOrder.Balance, ActiveOrder.Signal, MyScript, "PartEx");
                }
                else if ((ActiveOrder.BuySell == "B") != IsGrow[^1] ||
                    (MyScript.CurrentPosition == PositionType.Short) != IsGrow[^1] || VolumeOrder == 0) CancelOrder(ActiveOrder);
                else if (Math.Abs(ActiveOrder.Price - PrevClose) > 0.00001 || Math.Abs(ActiveOrder.Balance - VolumeOrder) > 1)
                    ReplaceOrder(ActiveOrder, Symbol, OrderType.Limit,
                        PrevClose, VolumeOrder, ActiveOrder.Signal, MyScript, ActiveOrder.Note);
            }
            else if (DateTime.Now >= ActiveOrder.Time.AddMinutes(5))
            {
                if (NormalPrice) ReplaceOrder(ActiveOrder, Symbol, OrderType.Market, PrevClose,
                    ActiveOrder.Balance, ActiveOrder.Signal + "AtMarket", MyScript, ActiveOrder.Note);
                else AddInfo(MyScript.Name + ": Цена за пределами нормального диапазона. Ожидание возвращения.", notify: true);
            }
            else if ((ActiveOrder.BuySell == "B") != IsGrow[^1] || VolumeOrder == 0) CancelOrder(ActiveOrder);
            return;
        }

        // Проверка условий для выхода
        if (LastExecuted != null && LastExecuted.DateTime >= MyScript.Result.iLastDT || VolumeOrder == 0 ||
            BasicSecurity == null && Symbol.Bars.DateTime[^1].Date != Symbol.Bars.DateTime[^2].Date ||
            BasicSecurity != null && BasicSecurity.Bars.DateTime[^1].Date != BasicSecurity.Bars.DateTime[^2].Date) return;

        // Проверка условий для выставления новой заявки
        if (MyScript.CurrentPosition == PositionType.Short && IsGrow[^1] ||
            MyScript.CurrentPosition == PositionType.Long && !IsGrow[^1])
        {
            SendOrder(Symbol, OrderType.Limit,
                IsGrow[^1], PrevClose, VolumeOrder, IsGrow[^1] ? "BuyAtLimit" : "SellAtLimit", MyScript);
            WriteLogScript(MyScript);
        }
    }
    private void ProcessOrdersLimitLine(IScript MyScript, (int, int) OrderVolumes)
    {
        Security Symbol = MySecurity;
        double PriceOrder = BasicSecurity?.Bars.DateTime[^1] > MySecurity.Bars.DateTime[^1] ?
            MyScript.Result.Indicators[0][^1] : MyScript.Result.Indicators[0][^2];
        int VolumeOrder = MyScript.CurrentPosition == PositionType.Long ? OrderVolumes.Item2 : OrderVolumes.Item1;

        bool[] IsGrow = MyScript.Result.IsGrow;
        Order ActiveOrder = MyScript.ActiveOrder;
        Order LastExecuted = MyScript.LastExecuted;

        // Работа с активной заявкой // Нужно сделать уверенную замену через ReplaceOrder
        if (ActiveOrder != null)
        {
            if (ActiveOrder.Quantity - ActiveOrder.Balance > 0.00001)
            {
                if (Math.Abs(ActiveOrder.Price - PriceOrder) > 0.00001)
                {
                    int Quantity = ActiveOrder.Quantity - ActiveOrder.Balance; // Нужно сделать уверенную замену через ReplaceOrder
                    if (CancelOrder(ActiveOrder))
                        SendOrder(Symbol, OrderType.Market,
                            ActiveOrder.BuySell == "S", PriceOrder, Quantity, "CancelPartEx", MyScript, "NM");
                }
            }
            else if (VolumeOrder == 0) CancelOrder(ActiveOrder);
            else if (Math.Abs(ActiveOrder.Price - PriceOrder) > 0.00001 || Math.Abs(ActiveOrder.Balance - VolumeOrder) > 1)
            {
                if (PriceOrder - Symbol.MinPrice > -0.00001 && PriceOrder - Symbol.MaxPrice < 0.00001)
                    ReplaceOrder(ActiveOrder, Symbol, OrderType.Limit,
                        PriceOrder, VolumeOrder, ActiveOrder.Signal, MyScript, ActiveOrder.Note);
                else CancelOrder(ActiveOrder);
            }
            return;
        }

        // Проверка условий для выхода
        if (LastExecuted != null && LastExecuted.DateTime >= MyScript.Result.iLastDT || VolumeOrder == 0 ||
            BasicSecurity == null && Symbol.Bars.DateTime[^1].Date != Symbol.Bars.DateTime[^2].Date ||
            BasicSecurity != null && BasicSecurity.Bars.DateTime[^1].Date != BasicSecurity.Bars.DateTime[^2].Date) return;

        // Проверка условий для выставления новой заявки
        if ((MyScript.CurrentPosition == PositionType.Short && IsGrow[^1] ||
            MyScript.CurrentPosition == PositionType.Long && !IsGrow[^1]) &&
            PriceOrder - Symbol.MinPrice > -0.00001 && PriceOrder - Symbol.MaxPrice < 0.00001)
        {
            SendOrder(Symbol, OrderType.Limit,
                IsGrow[^1], PriceOrder, VolumeOrder, IsGrow[^1] ? "BuyAtLimit" : "SellAtLimit", MyScript);
            WriteLogScript(MyScript);
        }
    }
    private void ProcessOrdersStopLine(IScript MyScript, (int, int) OrderVolumes, bool NormalPrice, bool NowBidding, double[] SmallATR)
    {
        Security Symbol = MySecurity;
        double PrevClose = Symbol.Bars.Close[^2];
        double PrevStopLine = MyScript.Result.Indicators[0][^2];
        int VolumeOrder = MyScript.CurrentPosition == PositionType.Long ? OrderVolumes.Item2 : OrderVolumes.Item1;
        if (BasicSecurity?.Bars.DateTime[^1] > MySecurity.Bars.DateTime[^1])
        {
            PrevClose = Symbol.Bars.Close[^1];
            PrevStopLine = MyScript.Result.Indicators[0][^1];
        }

        bool[] IsGrow = MyScript.Result.IsGrow;
        Order ActiveOrder = MyScript.ActiveOrder;
        Order LastExecuted = MyScript.LastExecuted;
        PositionType CurPosition = MyScript.CurrentPosition;

        // Работа с активной заявкой
        if (ActiveOrder != null)
        {
            if (ActiveOrder.Condition != "None")
            {
                double Price = ActiveOrder.BuySell == "B" ? PrevStopLine + Symbol.MinStep : PrevStopLine - Symbol.MinStep;
                if (ActiveOrder.Status == "active")
                {
                    if (DateTime.Now > ActiveOrder.Time.AddSeconds(WaitingLimit) && NowBidding)
                    {
                        if (NormalPrice)
                            ReplaceOrder(ActiveOrder, Symbol, OrderType.Market,
                                PrevClose, ActiveOrder.Balance, ActiveOrder.Signal + "AtMarket", MyScript);
                        else AddInfo(MyScript.Name +
                            ": Цена за пределами нормального диапазона. Ожидание возвращения.", notify: true);
                    }
                }
                else if (VolumeOrder == 0 || DateTime.Now < DateTime.Today.AddHours(7.5) && ActiveOrder.Note == null ||
                    (ActiveOrder.BuySell == "B") == IsGrow[^1]) CancelOrder(ActiveOrder);
                else if (Math.Abs(ActiveOrder.Price - Price) > 0.00001 || ActiveOrder.Balance - VolumeOrder > 1)
                    ReplaceOrder(ActiveOrder, Symbol, OrderType.Conditional, Price, VolumeOrder, ActiveOrder.Signal, MyScript);
            }
            else if (NowBidding)
            {
                if (ActiveOrder.Note == "NB")
                {
                    if (DateTime.Now > DateTime.Today.AddHours(7.5) && Symbol.LastTrade.DateTime > DateTime.Today.AddHours(7.5) &&
                        DateTime.Now < DateTime.Today.AddHours(9.5) ||
                        DateTime.Now > DateTime.Today.AddHours(10.5) && Symbol.LastTrade.DateTime > DateTime.Today.AddHours(10.5))
                    {
                        if (NormalPrice) ReplaceOrder(ActiveOrder, Symbol, OrderType.Limit, PrevClose,
                            ActiveOrder.Balance, ActiveOrder.Signal + "AtClose", MyScript, "CloseNB");
                        else AddInfo(MyScript.Name +
                            ": Цена за пределами нормального диапазона. Ожидание возвращения.", notify: true);
                    }
                }
                else if (DateTime.Now >= ActiveOrder.Time.AddSeconds(WaitingLimit))
                {
                    if (NormalPrice) ReplaceOrder(ActiveOrder, Symbol, OrderType.Market, PrevClose,
                        ActiveOrder.Balance, ActiveOrder.Signal + "AtMarket", MyScript, ActiveOrder.Note);
                    else AddInfo(MyScript.Name +
                        ": Цена за пределами нормального диапазона. Ожидание возвращения.", notify: true);
                }
            }
            return;
        }

        // Проверка условий для выхода
        if (!NowBidding || VolumeOrder == 0) return;
        if (LastExecuted != null && LastExecuted.DateTime.Date == DateTime.Today && LastExecuted.DateTime >= MyScript.Result.iLastDT)
        {
            if (LastExecuted.Note == null ||
                LastExecuted.Note == "NB" && LastExecuted.DateTime < DateTime.Today.AddHours(7.5)) return;

            for (int bar = MyScript.Result.Indicators[0].Length - 1; bar > 1; bar--)
            {
                if (LastExecuted.BuySell == "B" && !IsGrow[bar - 1] || LastExecuted.BuySell == "S" && IsGrow[bar - 1])
                {
                    if (Math.Abs(MyScript.Result.Indicators[0][bar - 1] - MyScript.Result.Indicators[0][^2]) < 0.00001) return;
                    else break;
                }
            }
        }

        // Проверка условий для выставления новой заявки
        if (Symbol.LastTrade.DateTime < DateTime.Today.AddHours(7.5) && DateTime.Now < DateTime.Today.AddHours(7.5))
        {
            if (CurPosition == PositionType.Short && IsGrow[^1] || CurPosition == PositionType.Long && !IsGrow[^1])
            {
                double Price = IsGrow[^1] ? PrevStopLine - SmallATR[^2] : PrevStopLine + SmallATR[^2];
                SendOrder(Symbol, OrderType.Limit,
                    IsGrow[^1], Price, VolumeOrder, IsGrow[^1] ? "BuyAtLimitNB" : "SellAtLimitNB", MyScript, "NB");
                WriteLogScript(MyScript);
            }
        }
        else if (CurPosition == PositionType.Short && !IsGrow[^1] || CurPosition == PositionType.Long && IsGrow[^1])
        {
            double Price = IsGrow[^1] ? PrevStopLine - Symbol.MinStep : PrevStopLine + Symbol.MinStep;
            SendOrder(Symbol, OrderType.Conditional,
                !IsGrow[^1], Price, VolumeOrder, !IsGrow[^1] ? "BuyAtStop" : "SellAtStop", MyScript);
            WriteLogScript(MyScript);
        }
        else
        {
            AddInfo(MyScript.Name + ": Текущая позиция скрипта не соответствует IsGrow.", notify: true);
            if (Symbol.LastTrade.DateTime < DateTime.Today.AddHours(10.5) && DateTime.Now < DateTime.Today.AddHours(10.5))
            {
                double Price = IsGrow[^1] ? PrevStopLine - SmallATR[^2] : PrevStopLine + SmallATR[^2];
                SendOrder(Symbol, OrderType.Limit,
                    IsGrow[^1], Price, VolumeOrder, IsGrow[^1] ? "BuyAtLimitNB" : "SellAtLimitNB", MyScript, "NB");
            }
            else
            {
                double Price = IsGrow[^1] ? PrevStopLine - Symbol.MinStep : PrevStopLine + Symbol.MinStep;
                SendOrder(Symbol, OrderType.Limit, IsGrow[^1], Price, VolumeOrder, "UnknowEvent", MyScript);
            }
            WriteLogScript(MyScript);
        }
    }

    public void UpdateModelsAndPanel(IScript MyScript)
    {
        string SelectedScript = null;
        Window.Dispatcher.Invoke(() =>
        {
            Grid MyGrid =
            (((Window.TabsTools.Items[Tools.IndexOf(this)] as TabItem).Content as Grid).Children[1] as Grid).Children[0] as Grid;
            SelectedScript = MyGrid.Children.OfType<ComboBox>().Last().Text;

            MyScript.BlockInfo.Text = "IsGrow[i] " + MyScript.Result.IsGrow[^1] +
            "     IsGrow[i-1] " + MyScript.Result.IsGrow[^2] + "\nType " + MyScript.Result.Type;
        });

        foreach (OxyPlot.Annotations.Annotation MyAnn in MainModel.Annotations.ToArray())
            if (MyAnn.ToolTip == MyScript.Name || MyAnn.ToolTip is "System" or null) MainModel.Annotations.Remove(MyAnn);

        if (MyScript.Result.Type != ScriptType.OSC)
            foreach (OxyPlot.Series.Series MySeries in MainModel.Series.ToArray())
                if (MySeries.Title == MyScript.Name) MainModel.Series.Remove(MySeries);

        if (SelectedScript == MyScript.Name || SelectedScript == "AllScripts")
        {
            if (MyScript.Result.Type == ScriptType.OSC) UpdateMiniModel(MyScript);
            else foreach (double[] Indicator in MyScript.Result.Indicators)
                    MainModel.Series.Add(MakeLineSeries(Indicator, MyScript.Name));

            if (!ShowBasicSecurity)
            {
                Trade[] MyTrades = MyScript.MyTrades.ToArray()
                    .Concat(SystemTrades.ToArray().Where(x => x.Seccode == MySecurity.Seccode)).ToArray();
                Order[] MyOrders =
                    Orders.ToArray().Where(x => x.Seccode == MySecurity.Seccode && x.Status is "active" or "watching").ToArray();
                if (MyTrades.Length > 0 || MyOrders.Length > 0) AddAnnotations(MyTrades, MyOrders);
            }
        }
    }
    private bool AlignData(IScript MyScript)
    {
        int InitialLength = MySecurity.Bars.Close.Length;
        int y = MyScript.Result.IsGrow.Length - InitialLength;
        if (y > 0)
        {
            MyScript.Result.IsGrow = MyScript.Result.IsGrow[y..];
            for (int i = 0; i < MyScript.Result.Indicators.Length; i++)
                if (MyScript.Result.Indicators[i] != null) MyScript.Result.Indicators[i] = MyScript.Result.Indicators[i][y..];
        }
        else if (y < 0)
        {
            if (MyScript.Result.IsGrow.Length > 500)
            {
                AddInfo(Name + ": Количество торговых баров больше базисных: " +
                    InitialLength + "/" + MyScript.Result.IsGrow.Length + " Обрезка.", notify: true);
                y = MySecurity.SourceBars.Close.Length - BasicSecurity.SourceBars.Close.Length + 20;
                MySecurity.SourceBars.TrimBars(y);
                y = InitialLength - MyScript.Result.IsGrow.Length;
                MySecurity.Bars.TrimBars(y);
            }
            else AddInfo(Name + ": Количество торговых баров больше базисных: " +
                InitialLength + "/" + MyScript.Result.IsGrow.Length, notify: true);
            return false;
        }
        return true;
    }
    private void WriteLogScript(IScript MyScript)
    {
        try
        {
            string Data = DateTime.Now + ": /////////////////// Script: " + MyScript.Name + "\nType " + MyScript.Result.Type +
                "\nCurPosition " + MyScript.CurrentPosition + "\nIsGrow[^1] " + MyScript.Result.IsGrow[^1] +
                "\nIsGrow[^2] " + MyScript.Result.IsGrow[^2] + "\nOnlyLimit " + MyScript.Result.OnlyLimit +
                "\nCentre " + MyScript.Result.Centre + "\nLevel " + MyScript.Result.Level;

            for (int i = 0; i < MyScript.Result.Indicators.Length; i++)
                if (MyScript.Result.Indicators[i] != null)
                    Data += "\nIndicator" + (i + 1) + "[^2] " + MyScript.Result.Indicators[i][^2];

            System.IO.File.AppendAllText("Logs/LogsTools/" + Name + ".txt", Data + "\n");
        }
        catch (Exception e) { AddInfo(Name + ": Исключение логирования скрипта: " + e.Message); }
    }
    #endregion
}
internal static class Extensions
{
    public static bool UpdateOrdersAndPosition(this IScript MyScript)
    {
        MyScript.LastExecuted = MyScript.MyOrders.ToArray().LastOrDefault(x => x.Status == "matched" && x.Note != "NM");
        Order[] ActiveOrders =
            Orders.ToArray().Where(x => x.Sender == MyScript.Name && (x.Status is "active" or "watching")).ToArray();

        if (ActiveOrders.Length <= 1) MyScript.ActiveOrder = ActiveOrders.SingleOrDefault();
        else
        {
            MyScript.ActiveOrder = null;
            AddInfo(MyScript.Name + ": Отмена активных заявок скрипта: " + ActiveOrders.Length);
            foreach (Order MyOrder in ActiveOrders) CancelOrder(MyOrder);

            Thread.Sleep(500);
            if (!ActiveOrders.Where(x => x.Status is "active" or "watching").Any()) return true;

            Thread.Sleep(1000);
            if (!ActiveOrders.Where(x => x.Status is "active" or "watching").Any()) return true;

            Thread.Sleep(1500);
            if (!ActiveOrders.Where(x => x.Status is "active" or "watching").Any()) return true;

            AddInfo(MyScript.Name + ": Не удалось вовремя отменить активные заявки.");
            return false;
        }
        return true;
    }
}
