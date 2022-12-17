using System;
using System.Windows.Controls;

namespace ProSystem.Algorithms
{
    [Serializable]
    internal class CrossEMA : IScript, System.ComponentModel.INotifyPropertyChanged
    {
        #region Fields
        private Order LastEx;
        private PositionType Pos;

        private int Per = 10;
        private int Mul = 2;
        private int TF = 60;
        private bool OnlyLim = true;
        private bool IsCrEMAL = false;

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
        public bool IsCrossEMAL
        {
            get => IsCrEMAL;
            set { IsCrEMAL = value; NotifyChanged(); }
        }

        private void NotifyChanged() => PropertyChanged?.Invoke(this, new System.ComponentModel.PropertyChangedEventArgs(""));
        #endregion

        public CrossEMA(string Name) { this.Name = Name; }
        public override string ToString() => "CrossEMA";
        public void Initialize(Tool MyTool, TabItem TabTool)
        {
            bool IsOSC = false;
            string[] UpperProperties = new string[] { "Period", "Mult", "IndicatorTF" };
            string[] MiddleProperties = new string[] { "IsCrossEMAL", "OnlyLimit" };
            MyTool.InitializeScript(this, TabTool, IsOSC, UpperProperties, MiddleProperties);
        }
        public void Calculate(Security Symbol)
        {
            Bars iBars = Bars.Compress(Symbol.Bars, IndicatorTF);
            double[] ShortEMA = Indicators.EMA(iBars.Close, Period, Symbol.Decimals);
            double[] LongEMA = Indicators.EMA(iBars.Close, Period * Mult, Symbol.Decimals);
            ShortEMA = Indicators.Synchronize(ShortEMA, iBars, Symbol.Bars);
            LongEMA = Indicators.Synchronize(LongEMA, iBars, Symbol.Bars);

            bool[] IsGrow = new bool[Symbol.Bars.Close.Length];
            for (int i = 1; i < Symbol.Bars.Close.Length; i++)
            {
                if (ShortEMA[i - 1] - LongEMA[i - 1] > 0.00001) IsGrow[i] = true;
                else if (ShortEMA[i - 1] - LongEMA[i - 1] < -0.00001) IsGrow[i] = false;
                else IsGrow[i] = IsGrow[i - 1];
            }
            ScriptType Type = IsCrossEMAL ? ScriptType.LimitLine : ScriptType.Line;
            Result = new ScriptResult(Type, IsGrow, new double[][] { ShortEMA, LongEMA }, iBars.DateTime[^1], OnlyLimit);
        }
    }
}
