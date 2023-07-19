using System;
using System.Windows.Controls;
using System.Windows.Shapes;

namespace ProSystem.Algorithms
{
    [Serializable]
    internal class CrossMA : IScript, System.ComponentModel.INotifyPropertyChanged
    {
        #region Fields
        private Order LastEx;
        private PositionType Pos;

        private int Per = 10;
        private int Mul = 2;
        private int TF = 60;
        private bool OnlyLim = true;
        private bool IsCrMALim = false;
        private NameMA NaM = NameMA.SMA;

        [field: NonSerialized] private TextBlock BlInfo;
        [field: NonSerialized] public event System.ComponentModel.PropertyChangedEventHandler PropertyChanged;
        #endregion

        #region Properties
        public string Name { get; set; }
        public Order ActiveOrder { get; set; }
        public Order LastExecuted
        {
            get => LastEx;
            set
            {
                LastEx = value;
                if (LastEx != null) CurrentPosition = LastEx.BuySell == "B" ? PositionType.Long : PositionType.Short;
            }
        }
        public PositionType CurrentPosition
        {
            get => Pos;
            set { Pos = value; NotifyChanged(); }
        }
        public ScriptResult Result { get; set; }
        public TextBlock BlockInfo
        {
            get => BlInfo;
            set { BlInfo = value; NotifyChanged(); }
        }
        public System.Collections.ObjectModel.ObservableCollection<Order> MyOrders { get; set; } = new();
        public System.Collections.ObjectModel.ObservableCollection<Trade> MyTrades { get; set; } = new();

        public int Period
        {
            get => Per;
            set { Per = value; NotifyChanged(); }
        }
        public int Mult
        {
            get => Mul;
            set { Mul = value; NotifyChanged(); }
        }
        public int IndicatorTF
        {
            get => TF;
            set { TF = value; NotifyChanged(); }
        }
        public bool OnlyLimit
        {
            get => OnlyLim;
            set { OnlyLim = value; NotifyChanged(); }
        }
        public bool IsCrossMALim
        {
            get => IsCrMALim;
            set { IsCrMALim = value; NotifyChanged(); }
        }
        public NameMA NameMA
        {
            get => NaM;
            set { NaM = value; NotifyChanged(); }
        }

        private void NotifyChanged() => PropertyChanged?.Invoke(this, new System.ComponentModel.PropertyChangedEventArgs(""));
        #endregion

        public CrossMA(string Name) { this.Name = Name; }
        public override string ToString() => "CrossMA";
        public void Initialize(Tool MyTool, TabItem TabTool)
        {
            bool IsOSC = false;
            string[] UpperProperties = new string[] { "Period", "Mult", "IndicatorTF" };
            string[] MiddleProperties = new string[] { "IsCrossMALim", "OnlyLimit" };
            MyTool.InitializeScript(this, TabTool, IsOSC, UpperProperties, MiddleProperties,
                "NameMA", new NameMA[] { NameMA.SMA, NameMA.EMA, NameMA.SMMA, NameMA.DEMA, NameMA.KAMA });
        }
        public void Calculate(Security Symbol)
        {
            Bars iBars = Bars.Compress(Symbol.Bars, IndicatorTF);
            double[] ShortMA, LongMA;
            if (NameMA == NameMA.SMA)
            {
                ShortMA = Indicators.SMA(iBars.Close, Period, Symbol.Decimals);
                LongMA = Indicators.SMA(iBars.Close, Period * Mult, Symbol.Decimals);
            }
            else if (NameMA == NameMA.EMA)
            {
                ShortMA = Indicators.EMA(iBars.Close, Period, Symbol.Decimals);
                LongMA = Indicators.EMA(iBars.Close, Period * Mult, Symbol.Decimals);
            }
            else if (NameMA == NameMA.SMMA)
            {
                ShortMA = Indicators.SMMA(iBars.Close, Period, Symbol.Decimals);
                LongMA = Indicators.SMMA(iBars.Close, Period * Mult, Symbol.Decimals);
            }
            else if (NameMA == NameMA.DEMA)
            {
                ShortMA = Indicators.DEMA(iBars.Close, Period, Symbol.Decimals);
                LongMA = Indicators.DEMA(iBars.Close, Period * Mult, Symbol.Decimals);
            }
            else if (NameMA == NameMA.KAMA)
            {
                ShortMA = Indicators.KAMA(iBars.Close, Period, Symbol.Decimals);
                LongMA = Indicators.KAMA(iBars.Close, Period * Mult, Symbol.Decimals);
            }
            else throw new Exception("Непредвиденный тип MA");
            ShortMA = Indicators.Synchronize(ShortMA, iBars, Symbol.Bars);
            LongMA = Indicators.Synchronize(LongMA, iBars, Symbol.Bars);

            bool[] IsGrow = new bool[Symbol.Bars.Close.Length];
            for (int i = 1; i < Symbol.Bars.Close.Length; i++)
            {
                if (ShortMA[i - 1] - LongMA[i - 1] > 0.00001) IsGrow[i] = true;
                else if (ShortMA[i - 1] - LongMA[i - 1] < -0.00001) IsGrow[i] = false;
                else IsGrow[i] = IsGrow[i - 1];
            }
            ScriptType Type = IsCrossMALim ? ScriptType.LimitLine : ScriptType.Line;
            Result = new ScriptResult(Type, IsGrow, new double[][] { ShortMA, LongMA }, iBars.DateTime[^1], OnlyLimit);
        }
    }
}
