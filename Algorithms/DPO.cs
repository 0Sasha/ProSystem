using System;
using System.Windows.Controls;

namespace ProSystem.Algorithms
{
    [Serializable]
    internal class DPO : IScript, System.ComponentModel.INotifyPropertyChanged
    {
        #region Fields
        private Order LastEx;
        private PositionType Pos;

        private int Per = 5;
        private int PerEx = 30;
        private int TF = 60;
        private bool IsTr = true;
        private bool OnlyLim = true;
        private bool UseCh = true;

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
        public int PeriodEx
        {
            get => PerEx;
            set { PerEx = value; NotifyChanged(); }
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
        public bool UseChannel
        {
            get => UseCh;
            set { UseCh = value; NotifyChanged(); }
        }

        private void NotifyChanged() => PropertyChanged?.Invoke(this, new System.ComponentModel.PropertyChangedEventArgs(""));
        #endregion

        public DPO(string Name) { this.Name = Name; }
        public override string ToString() => "DPO";
        public void Initialize(Tool MyTool, TabItem TabTool)
        {
            bool IsOSC = true;
            string[] UpperProperties = new string[] { "Period", "PeriodEx", "IndicatorTF" };
            string[] MiddleProperties = new string[] { "IsTrend", "OnlyLimit", "UseChannel" };
            MyTool.InitializeScript(this, TabTool, IsOSC, UpperProperties, MiddleProperties);
        }
        public void Calculate(Security Symbol)
        {
            Bars iBars = Bars.Compress(Symbol.Bars, IndicatorTF);
            bool OneLevel = PeriodEx < 1;
            double[] Upper = null, Lower = null, SignalLine = null;
            double[] DPO = Indicators.DPO(iBars.Close, Period);
            if (!OneLevel)
            {
                if (UseChannel)
                {
                    var Lines = Indicators.BBands(DPO, PeriodEx, 1.5);
                    Upper = Indicators.Synchronize(Lines.Item1, iBars, Symbol.Bars);
                    Lower = Indicators.Synchronize(Lines.Item2, iBars, Symbol.Bars);
                }
                else SignalLine = Indicators.Synchronize(Indicators.EMA(DPO, PeriodEx), iBars, Symbol.Bars);
            }
            DPO = Indicators.Synchronize(DPO, iBars, Symbol.Bars);

            bool[] IsGrow = new bool[Symbol.Bars.Close.Length];
            for (int i = 2; i < Symbol.Bars.Close.Length; i++)
            {
                if (OneLevel)
                {
                    if (DPO[i - 1] > 0.00001) IsGrow[i] = IsTrend;
                    else if (DPO[i - 1] < -0.00001) IsGrow[i] = !IsTrend;
                    else IsGrow[i] = IsGrow[i - 1];
                }
                else if (UseChannel)
                {
                    if (DPO[i - 1] - Upper[i - 2] > 0.00001) IsGrow[i] = IsTrend;
                    else if (DPO[i - 1] - Lower[i - 2] < -0.00001) IsGrow[i] = !IsTrend;
                    else IsGrow[i] = IsGrow[i - 1];
                }
                else
                {
                    if (DPO[i - 1] - SignalLine[i - 1] > 0.00001) IsGrow[i] = IsTrend;
                    else if (DPO[i - 1] - SignalLine[i - 1] < -0.00001) IsGrow[i] = !IsTrend;
                    else IsGrow[i] = IsGrow[i - 1];
                }
            }

            if (OneLevel) Result = new ScriptResult(ScriptType.OSC, IsGrow, new double[][] { DPO }, iBars.DateTime[^1], 0, 0, OnlyLimit);
            else if (UseChannel) Result = new ScriptResult(ScriptType.OSC, IsGrow, new double[][] { DPO, Upper, Lower }, iBars.DateTime[^1], OnlyLimit);
            else Result = new ScriptResult(ScriptType.OSC, IsGrow, new double[][] { DPO, SignalLine }, iBars.DateTime[^1], OnlyLimit);
        }
    }
}
