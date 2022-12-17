using System;
using System.Windows.Controls;

namespace ProSystem.Algorithms
{
    [Serializable]
    internal class MACD : IScript, System.ComponentModel.INotifyPropertyChanged
    {
        #region Fields
        private Order LastEx;
        private PositionType Pos;

        private int Per = 10;
        private int Mul = 2;
        private int TF = 60;
        private bool OnlyLim = true;
        private bool IsTr = true;

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
        public bool IsTrend
        {
            get => IsTr;
            set { IsTr = value; NotifyChanged(); }
        }

        private void NotifyChanged() => PropertyChanged?.Invoke(this, new System.ComponentModel.PropertyChangedEventArgs(""));
        #endregion

        public MACD(string Name) { this.Name = Name; }
        public override string ToString() => "MACD";
        public void Initialize(Tool MyTool, TabItem TabTool)
        {
            bool IsOSC = true;
            string[] UpperProperties = new string[] { "Period", "Mult", "IndicatorTF" };
            string[] MiddleProperties = new string[] { "IsTrend", "OnlyLimit" };
            MyTool.InitializeScript(this, TabTool, IsOSC, UpperProperties, MiddleProperties);
        }
        public void Calculate(Security Symbol)
        {
            Bars iBars = Bars.Compress(Symbol.Bars, IndicatorTF);
            double[] MACDLine = Indicators.MACD(iBars.Close, Period, Period * Mult);
            double[] SignalLine = Indicators.EMA(MACDLine, (int)(Period * 0.75));
            MACDLine = Indicators.Synchronize(MACDLine, iBars, Symbol.Bars);
            SignalLine = Indicators.Synchronize(SignalLine, iBars, Symbol.Bars);

            bool[] IsGrow = new bool[Symbol.Bars.Close.Length];
            for (int i = 1; i < Symbol.Bars.Close.Length; i++)
            {
                if (MACDLine[i - 1] - SignalLine[i - 1] > 0.00001) IsGrow[i] = IsTrend;
                else if (MACDLine[i - 1] - SignalLine[i - 1] < -0.00001) IsGrow[i] = !IsTrend;
                else IsGrow[i] = IsGrow[i - 1];
            }
            Result = new ScriptResult(ScriptType.OSC, IsGrow, new double[][] { MACDLine, SignalLine }, iBars.DateTime[^1], OnlyLimit);
        }
    }
}
