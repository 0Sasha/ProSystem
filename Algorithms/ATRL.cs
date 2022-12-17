using System;
using System.Linq;
using System.Windows.Controls;
using static ProSystem.Methods;
using static ProSystem.MainWindow;
using static ProSystem.TXmlConnector;

namespace ProSystem.Algorithms
{
    [Serializable]
    internal class ATRL : IScript, System.ComponentModel.INotifyPropertyChanged
    {
        #region Fields
        private PositionType Pos;

        private int Per = 10;
        private int Mul = 0;
        private int PerEx = 1;
        private double Cor = 0;
        private int TF = 60;
        private double MLNB = 1;

        private int Wait = 60;
        private double Share = 5;
        private int MinNumber = 0;
        private int MaxNumber = 2;
        private int Number = 1;
        private int BaseBal = 0;
        private DateTime TriggerPosition;

        private bool StopTr = true;
        private bool TradeSh = true;
        private bool UseBal = false;
        private bool UseNM = false;
        private bool ShowIn = true;
        private bool UseShift = false;

        [field: NonSerialized] private Border BorderState;
        [field: NonSerialized] private TextBlock MainBlockInfo;
        [field: NonSerialized] private TextBlock BlockInfo;
        [field: NonSerialized] public event System.ComponentModel.PropertyChangedEventHandler PropertyChanged;
        #endregion

        #region Properties
        public string Name { get; set; }
        public PositionType CurrentPosition { get { return Pos; } set { Pos = value; NotifyChanged(); } }
        public System.Collections.ObjectModel.ObservableCollection<Order> MyOrders { get; set; } = new();
        public System.Collections.ObjectModel.ObservableCollection<Trade> MyTrades { get; set; } = new();

        public int Period
        {
            get { return Per; }
            set { Per = value; NotifyChanged(); }
        }
        public int Mult
        {
            get { return Mul; }
            set { Mul = value; NotifyChanged(); }
        }
        public int PeriodEx
        {
            get { return PerEx; }
            set { PerEx = value; NotifyChanged(); }
        }
        public double Correction
        {
            get { return Cor; }
            set { Cor = value; NotifyChanged(); }
        }
        public int IndicatorTF
        {
            get { return TF; }
            set { TF = value; NotifyChanged(); }
        }
        public double MultLimitNB
        {
            get { return MLNB; }
            set { MLNB = value; NotifyChanged(); }
        }

        public int WaitingLimit
        {
            get { return Wait; }
            set { Wait = value; NotifyChanged(); }
        }
        public double ShareOfFunds
        {
            get { return Share; }
            set { Share = value > 15 ? 5 : value; NotifyChanged(); }
        }
        public int MinNumberOfLots
        {
            get { return MinNumber; }
            set { MinNumber = value; NotifyChanged(); }
        }
        public int MaxNumberOfLots
        {
            get { return MaxNumber; }
            set { MaxNumber = value; NotifyChanged(); }
        }
        public int NumberOfLots
        {
            get { return Number; }
            set { Number = value; NotifyChanged(); }
        }
        public int BaseBalance
        {
            get { return BaseBal; }
            set { BaseBal = value; NotifyChanged(); }
        }

        public bool StopTrading
        {
            get { return StopTr; }
            set { StopTr = value; NotifyChanged(); }
        }
        public bool TradeShare
        {
            get { return TradeSh; }
            set { TradeSh = value; NotifyChanged(); }
        }
        public bool UseBalance
        {
            get { return UseBal; }
            set { UseBal = value; NotifyChanged(); }
        }
        public bool UseNormalization
        {
            get { return UseNM; }
            set { UseNM = value; NotifyChanged(); }
        }
        public bool ShowIndicators
        {
            get { return ShowIn; }
            set { ShowIn = value; NotifyChanged(); }
        }
        public bool UseShiftBalance
        {
            get { return UseShift; }
            set { UseShift = value; NotifyChanged(); }
        }

        private void NotifyChanged() => PropertyChanged?.Invoke(this, new System.ComponentModel.PropertyChangedEventArgs(""));
        #endregion

        public ATRL(string Name) { this.Name = Name; }
        public void Initialize(Tool MyTool, TabItem TabTool)
        {
            Window.Dispatcher.Invoke(() =>
            {
                int i = Array.IndexOf(MyTool.Scripts, MyTool.Scripts.Single(x => x.Name == Name)) + 1;
                UIElementCollection UICollection = (TabTool.Content as Grid).Children.OfType<Grid>().ElementAt(i).Children;
                UICollection.Clear();

                Border Border = new()
                {
                    Height = 10,
                    BorderBrush = System.Windows.Media.Brushes.LightGray,
                    BorderThickness = new System.Windows.Thickness(1),
                    VerticalAlignment = System.Windows.VerticalAlignment.Top,
                    Background = System.Windows.Media.Brushes.Yellow
                };
                UICollection.Add(Border);
                UICollection.Add(GetTextBlock(Name, 5, 10));
                BorderState = Border;

                AddUpperControls(this, UICollection, new string[] { "Period", "Mult", "PeriodEx", "Correction", "IndicatorTF", "MultLimitNB" });
                AddMiddleControls(this, UICollection);
                AddLowerControls(this, UICollection, TradeShare, UseShiftBalance);

                TextBlock Block = GetTextBlock("MainBlockInfo", 5, 250);
                UICollection.Add(Block);
                MainBlockInfo = Block;
                TextBlock Block2 = GetTextBlock("BlockInfo", 105, 250);
                UICollection.Add(Block2);
                BlockInfo = Block2;
            });
        }
        public void Execute(Tool MyTool)
        {
            #region Risk Management
            MatchOrdersAndTrades(MyTool, MyTool.MySecurity);
            if (!CheckFullnessTool(MyTool, Name)) return;

            Security Symbol = MyTool.MySecurity;
            double[] Close = Symbol.Bars.Close;

            bool ReadyToTrade = !StopTrading;
            bool NowBidding = CheckStateSession(Symbol);
            bool NowLogging = CheckNeedLogging(Symbol, Name);

            // Вычисление требований и оптимальных объёмов позиций
            (double, double) RubReqs = GetRubReqs(Symbol, Name, ref ReadyToTrade);
            (double, double) ClearVols = GetPositionVolumes(RubReqs, ShareOfFunds);
            int Balance = GetBalance(Symbol, UseShiftBalance, BaseBalance, out int RealBalance);
            GetPositionVolumes(Name, RubReqs, TradeShare, ShareOfFunds, NumberOfLots, MinNumberOfLots, MaxNumberOfLots,
                Balance, UseBalance, ref ReadyToTrade, ref TriggerPosition, out (int, int) PosVolumes, out (int, int) OrderVolumes);

            // Фиксация и проверка активных заявок инструмента, проверка неизвестных заявок и заявок в неопределённом состоянии
            Order[] ActiveOrders = GetActiveOrders(Name);
            if ((ActiveOrders.Length > 1 || StopTrading && ActiveOrders.Length > 0) &&
                !CancelActiveOrders(Name, ref ActiveOrders) || CheckUncertainOrders(Symbol, Name)) return;

            // Фиксация активной и последней исполненной заявок, определение текущей позиции, оптимальных объёмов позиции и заявки
            Order ActiveOrder = ActiveOrders.SingleOrDefault();
            Order LastExecuted = MyOrders.ToArray().LastOrDefault(x => x.Status == "matched" && x.Note != "NM");
            CurrentPosition = DeterminePosition(CurrentPosition, LastExecuted, PosVolumes, OrderVolumes, out int OptVolCurPos, out int OptVolOrder);

            // Логирование
            if (NowLogging) WriteLogRisks(Name, Balance, CurrentPosition, UseBalance, UseShiftBalance, BaseBalance,
                StopTrading, NowBidding, ReadyToTrade, RubReqs, ClearVols, PosVolumes, OrderVolumes, MinNumberOfLots, MaxNumberOfLots);
            #endregion

            #region Calculation of Indicators
            // Сжатие баров
            Bars iBars = Bars.Compress(Symbol.Bars, IndicatorTF);
            if (iBars.Close.Length < 5) { AddInfo(Name + ": Недостаточно сжатых баров.", SendEmail: true); ReadyToTrade = false; }

            // Вычисление основных индикаторов и обновление графика
            double[] StopATR = Indicators.ATRLine(iBars.High, iBars.Low, iBars.Close, Period, Mult, PeriodEx, Correction, Symbol.Decimals);
            StopATR = Indicators.Synchronize(StopATR, iBars, Symbol.Bars);
            UpdateMainModel(MyTool, Name, new double[][] { StopATR }, MyTrades.ToArray(), ShowIndicators, NowBidding);

            // Вычисление индикаторов
            double PastStopATR = 0;
            bool[] IsGrow = new bool[Close.Length];
            int i = 1; for (; i < Close.Length; i++)
            {
                if (Math.Abs(PastStopATR - StopATR[i - 1]) > 0.00001 || PastStopATR < 0.00001)
                {
                    if (IsGrow[i - 1] && Symbol.Bars.High[i] - StopATR[i - 1] > 0.00001 || !IsGrow[i - 1] && Symbol.Bars.Low[i] - StopATR[i - 1] < -0.00001)
                    {
                        IsGrow[i] = !IsGrow[i - 1];
                        PastStopATR = StopATR[i - 1];
                    }
                    else IsGrow[i] = IsGrow[i - 1];
                }
                else IsGrow[i] = IsGrow[i - 1];
            }

            i = Close.Length - 1;
            double[] SmallATR = Indicators.ATR(Symbol.Bars.High, Symbol.Bars.Low, Symbol.Bars.Close, 150);
            double Average = Math.Round(Close[(i - 30)..i].Sum() / 30, Symbol.Decimals);
            bool NormalPrice = Math.Abs(Average - Close[i]) < SmallATR[i - 1] * 10;
            if (NowLogging) WriteLog(Name, DateTime.Now.ToString() + ": /////////////////// Indicators\nClose[i] " + Close[i] +
                "\nStopATR[i - 1] " + StopATR[i - 1] + "\nPastStopATR " + PastStopATR + "\nIsGrow[i] " + IsGrow[i] + "\nIsGrow[i - 1] " + IsGrow[i - 1] +
                "\nSmallATR[i - 1] " + SmallATR[i - 1] + "\nAverage " + Average + "\nNormalPrice " + NormalPrice);
            #endregion

            #region Updating Data
            if (BlockInfo == null) Initialize(MyTool, Window.TabsTools.Items[Array.IndexOf(Tools.ToArray(), MyTool)] as TabItem);
            UpdatePanel(Symbol, BorderState, MainBlockInfo, BlockInfo, Balance, RealBalance,
                IsGrow, ReadyToTrade, RubReqs, ClearVols, PosVolumes, OrderVolumes, Average, SmallATR[i - 1]);
            #endregion

            #region Order Management
            // Проверка условий для выхода
            if (!ReadyToTrade || !NowBidding) return;

            // Частичное исполнение или нормализация
            if (ActiveOrder != null && ActiveOrder.Quantity - ActiveOrder.Balance > 0.00001)
            {
                if (ActiveOrder.Note == null)
                {
                    if (ActiveOrder.DateTime < iBars.DateTime[^1].Date)
                    {
                        int Quantity = ActiveOrder.Quantity - ActiveOrder.Balance;
                        CancelOrder(ActiveOrder);
                        if (IsGrow[i]) SendOrder(Symbol, OrderType.Limit, true, Close[i - 1], Quantity, this, "PartExBuy", "Event");
                        else SendOrder(Symbol, OrderType.Limit, false, Close[i - 1], Quantity, this, "PartExSell", "Event");
                    }
                    return;
                }
            }
            else if (CurrentPosition == PositionType.Short && Balance > 0 || CurrentPosition == PositionType.Long && Balance < 0)
            {
                if (LastExecuted != null && LastExecuted.DateTime.AddSeconds(15) > DateTime.Now)
                {
                    AddInfo(Name + ": Текущая позиция скрипта не соответствует позиции в портфеле. Ожидание пересчёта.");
                    return;
                }
                else if (ActiveOrder != null) CancelOrder(ActiveOrder);

                AddInfo(Name + ": Текущая позиция скрипта не соответствует позиции в портфеле. Нормализация по рынку.", SendEmail: true);
                if (NormalPrice)
                {
                    SendOrder(Symbol, OrderType.Market, CurrentPosition == PositionType.Long, Close[i - 1], OptVolOrder,
                        this, "UnknowEvent", "NM");
                    System.Threading.Thread.Sleep(3000);
                }
                else AddInfo(Name + ": Цена за пределами нормального диапазона. Ожидание возвращения.");
                return;
            }
            else
            {
                Order LastCancelled = MyOrders.LastOrDefault(x => x.Status == "cancelled" && x.Note != "NM");
                if (ActiveOrder == null && LastCancelled != null && LastCancelled.Balance < LastCancelled.Quantity &&
                LastCancelled.WithdrawTime >= DateTime.Today.AddMinutes(1125) && LastCancelled.WithdrawTime < DateTime.Today.AddMinutes(1140))
                {
                    AddInfo(Name + ": Частично исполненная заявка отменена биржей во время клиринга. Нормализация.");
                    if (IsGrow[i] && LastCancelled.BuySell == "S" || !IsGrow[i] && LastCancelled.BuySell == "B")
                    {
                        int Quantity = LastCancelled.Quantity - LastCancelled.Balance;
                        if (IsGrow[i]) SendOrder(Symbol, OrderType.Limit, true, Close[i - 1], Quantity, this, "PartExBuy", "Event");
                        else SendOrder(Symbol, OrderType.Limit, false, Close[i - 1], Quantity, this, "PartExSell", "Event");
                    }
                    else
                    {
                        string Signal = LastCancelled.BuySell == "B" ? "PartExBuy" : "PartExSell";
                        if (NormalPrice) SendOrder(Symbol, OrderType.Market, LastCancelled.BuySell == "B", Close[i - 1], LastCancelled.Balance, this,
                            Signal, "Event");
                        else AddInfo(Name + ": Цена за пределами нормального диапазона. Ожидание возвращения.", SendEmail: true);
                    }
                    return;
                }
                else if (UseNormalization)
                {
                    if (ActiveOrder != null && ActiveOrder.Note == "NM")
                    {
                        if (DateTime.Now >= ActiveOrder.Time.AddSeconds(WaitingLimit))
                        {
                            if (NormalPrice)
                                ReplaceOrder(ActiveOrder, Symbol, OrderType.Market, Close[i], ActiveOrder.Balance, this, "NMAtMarket", "NM");
                            else AddInfo(Name + ": Цена за пределами нормального диапазона. Ожидание возвращения.", SendEmail: true);
                        }
                        return;
                    }
                    else if (Math.Abs(Balance) > OptVolCurPos)
                    {
                        if (ActiveOrder != null) CancelOrder(ActiveOrder);
                        if (Balance > 0)
                            SendOrder(Symbol, OrderType.Limit, false, Close[i - 1], Math.Abs(Balance) - OptVolCurPos, this, "Normalization", "NM");
                        else SendOrder(Symbol, OrderType.Limit, true, Close[i - 1], Math.Abs(Balance) - OptVolCurPos, this, "Normalization", "NM");
                        return;
                    }
                }
            }

            // Работа с активной заявкой
            if (ActiveOrder != null)
            {
                if (ActiveOrder.Note == null)
                {
                    if (Math.Abs(ActiveOrder.Price - StopATR[i - 1]) < 0.00001 && Math.Abs(ActiveOrder.Balance - OptVolOrder) <= 1)
                    {
                        if (DateTime.Now >= DateTime.Today.AddHours(23.5))
                        {
                            if (ActiveOrder.BuySell == "B")
                            {
                                double Price = Math.Max(StopATR[i] - SmallATR[i - 1] * MultLimitNB, Symbol.MinPrice);
                                ReplaceOrder(ActiveOrder, Symbol, OrderType.Limit, Price, OptVolOrder, this, "BuyAtLimitNB", "NB");
                            }
                            else
                            {
                                double Price = Math.Min(StopATR[i] + SmallATR[i - 1] * MultLimitNB, Symbol.MaxPrice);
                                ReplaceOrder(ActiveOrder, Symbol, OrderType.Limit, Price, OptVolOrder, this, "SellAtLimitNB", "NB");
                            }
                        }
                        return;
                    }
                    else CancelOrder(ActiveOrder);
                }
                else if (ActiveOrder.Note == "NB")
                {
                    if (DateTime.Now >= DateTime.Today.AddHours(23.5))
                    {
                        if (ActiveOrder.BuySell == "B" && IsGrow[i] || ActiveOrder.BuySell == "S" && !IsGrow[i])
                            ReplaceOrder(ActiveOrder, Symbol, OrderType.Limit, StopATR[i - 1], ActiveOrder.Balance, this,
                                "MissedLimit", "Event");
                        return;
                    }
                    else if (DateTime.Now >= DateTime.Today.AddHours(7.5))
                    {
                        if (ActiveOrder.BuySell == "B" && !IsGrow[i] || ActiveOrder.BuySell == "S" && IsGrow[i]) CancelOrder(ActiveOrder);
                        else
                        {
                            double Price = Symbol.LastTrade.DateTime < DateTime.Today.AddHours(7.5) ? Close[i] : Close[i - 1];
                            ReplaceOrder(ActiveOrder, Symbol, OrderType.Limit, Price, ActiveOrder.Balance, this,
                                ActiveOrder.Signal + "AtClose", "CloseNB");
                            return;
                        }
                    }
                    else return;
                }
                else
                {
                    if (DateTime.Now >= ActiveOrder.Time.AddSeconds(WaitingLimit))
                    {
                        if (NormalPrice) ReplaceOrder(ActiveOrder, Symbol, OrderType.Market, Close[i], ActiveOrder.Balance, this,
                            ActiveOrder.Signal + "AtMarket", ActiveOrder.Note);
                        else AddInfo(Name + ": Цена за пределами нормального диапазона. Ожидание возвращения.", SendEmail: true);
                    }
                    return;
                }
            }

            // Проверка условий для выхода
            if (LastExecuted != null && LastExecuted.DateTime.Date == DateTime.Today && LastExecuted.DateTime >= iBars.DateTime[^1])
            {
                if (LastExecuted.Note == null || LastExecuted.Note == "NB" && LastExecuted.DateTime < DateTime.Today.AddHours(7.5)) return;
                if (LastExecuted.Note != null )
                {
                    for (int bar = i; bar > 1; bar--)
                    {
                        if (LastExecuted.BuySell == "B" && !IsGrow[bar - 1] || LastExecuted.BuySell == "S" && IsGrow[bar - 1])
                        {
                            if (Math.Abs(StopATR[bar - 1] - StopATR[i - 1]) < 0.00001) return;
                            else break;
                        }
                    }
                }
            }
            if (OptVolOrder == 0) return;

            // Проверка на исключительные события (например, полное исполнение по цене индикатора без его пробоя)
            if (LastExecuted != null && LastExecuted.Note == null && DateTime.Now > DateTime.Today.AddHours(8) && // Почему 8 часов??
                (LastExecuted.BuySell == "B" && !IsGrow[i] || LastExecuted.BuySell == "S" && IsGrow[i]))
            {
                if (!IsGrow[i]) SendOrder(Symbol, OrderType.Limit, false, Close[i - 1], OptVolOrder, this, "SellUnknownEvent", "Event");
                else SendOrder(Symbol, OrderType.Limit, true, Close[i - 1], OptVolOrder, this, "BuyUnknownEvent", "Event");
                return;
            }

            // Проверка условий для выставления новой заявки
            bool NewOrder = false;
            if (Symbol.LastTrade.DateTime < DateTime.Today.AddHours(7.5) && DateTime.Now < DateTime.Today.AddHours(7.5))
            {
                if (CurrentPosition == PositionType.Short)
                {
                    double Price = Math.Max(StopATR[i - 1] - SmallATR[i - 1] * MultLimitNB, Symbol.MinPrice);
                    NewOrder = SendOrder(Symbol, OrderType.Limit, true, Price, OptVolOrder, this, "BuyAtLimitNB", "NB");
                }
                else
                {
                    double Price = Math.Min(StopATR[i - 1] + SmallATR[i - 1] * MultLimitNB, Symbol.MaxPrice);
                    NewOrder = SendOrder(Symbol, OrderType.Limit, false, Price, OptVolOrder, this, "SellAtLimitNB", "NB");
                }
            }
            else if (CurrentPosition == PositionType.Short && StopATR[i - 1] > Symbol.MinPrice)
                NewOrder = SendOrder(Symbol, OrderType.Limit, true, StopATR[i - 1], OptVolOrder, this, "BuyAtLimit");
            else if (CurrentPosition == PositionType.Long && StopATR[i - 1] < Symbol.MaxPrice)
                NewOrder = SendOrder(Symbol, OrderType.Limit, false, StopATR[i - 1], OptVolOrder, this, "SellAtLimit");
            if (NewOrder) WriteLogOrder(Name, i, IsGrow, new double[][] { StopATR });
            #endregion
        }
    }
}
