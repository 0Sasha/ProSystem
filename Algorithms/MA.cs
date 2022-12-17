using System;
using System.Windows.Controls;

namespace ProSystem.Algorithms
{
    [Serializable]
    internal class MA : IScript, System.ComponentModel.INotifyPropertyChanged
    {
        #region Fields
        private Order LastEx;
        private PositionType Pos;

        private int Per = 20;
        private int TF = 60;
        private bool IsTr = true;
        private bool OnlyLim = true;
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
        public NameMA NameMA
        {
            get => NaM;
            set { NaM = value; NotifyChanged(); }
        }

        private void NotifyChanged() => PropertyChanged?.Invoke(this, new System.ComponentModel.PropertyChangedEventArgs(""));
        #endregion

        public MA(string Name) { this.Name = Name; }
        public override string ToString() => "MA";
        public void Initialize(Tool MyTool, TabItem TabTool)
        {
            bool IsOSC = false;
            string[] UpperProperties = new string[] { "Period", "IndicatorTF" };
            string[] MiddleProperties = new string[] { "IsTrend", "OnlyLimit" };
            MyTool.InitializeScript(this, TabTool, IsOSC, UpperProperties, MiddleProperties,
                "NameMA", new NameMA[] { NameMA.SMA, NameMA.EMA, NameMA.DEMA, NameMA.KAMA, NameMA.Median });
        }
        public void Calculate(Security Symbol)
        {
            Bars iBars = Bars.Compress(Symbol.Bars, IndicatorTF);
            double[] MA;
            if (NameMA == NameMA.SMA) MA = Indicators.SMA(iBars.Close, Period);
            else if (NameMA == NameMA.EMA) MA = Indicators.EMA(iBars.Close, Period);
            else if (NameMA == NameMA.DEMA) MA = Indicators.DEMA(iBars.Close, Period);
            else if (NameMA == NameMA.KAMA) MA = Indicators.KAMA(iBars.Close, Period);
            else if (NameMA == NameMA.Median) MA = Indicators.Median(iBars.Close, Period);
            else throw new Exception("Непредвиденный тип MA");
            MA = Indicators.Synchronize(MA, iBars, Symbol.Bars);

            bool[] IsGrow = new bool[Symbol.Bars.Close.Length];
            for (int i = 2; i < Symbol.Bars.Close.Length; i++)
            {
                if (MA[i - 1] - MA[i - 2] > 0.00001) IsGrow[i] = IsTrend;
                else if (MA[i - 1] - MA[i - 2] < -0.00001) IsGrow[i] = !IsTrend;
                else IsGrow[i] = IsGrow[i - 1];
            }
            Result = new ScriptResult(ScriptType.Line, IsGrow, new double[][] { MA }, iBars.DateTime[^1], OnlyLimit);
        }
    }
}
