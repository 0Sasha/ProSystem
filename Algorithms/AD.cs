using System;
using System.Windows.Controls;

namespace ProSystem.Algorithms
{
    [Serializable]
    internal class AD : IScript, System.ComponentModel.INotifyPropertyChanged
    {
        #region Fields
        private Order LastEx;
        private PositionType Pos;

        private int Per = 20;
        private int TF = 60;
        private bool IsTr = true;
        private bool OnlyLim = true;
        private bool UseCh = true;
        private bool ChIsBn = false;

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
        public bool UseChannel
        {
            get => UseCh;
            set { UseCh = value; NotifyChanged(); }
        }
        public bool ChIsBands
        {
            get => ChIsBn;
            set { ChIsBn = value; NotifyChanged(); }
        }

        private void NotifyChanged() => PropertyChanged?.Invoke(this, new System.ComponentModel.PropertyChangedEventArgs(""));
        #endregion

        public AD(string Name) { this.Name = Name; }
        public override string ToString() => "AD";
        public void Initialize(Tool MyTool, TabItem TabTool)
        {
            bool IsOSC = true;
            string[] UpperProperties = new string[] { "Period", "IndicatorTF" };
            string[] MiddleProperties = new string[] { "IsTrend", "OnlyLimit", "UseChannel", "ChIsBands" };
            MyTool.InitializeScript(this, TabTool, IsOSC, UpperProperties, MiddleProperties);
        }
        public void Calculate(Security Symbol)
        {
            Bars iBars = Bars.Compress(Symbol.Bars, IndicatorTF);
            double[] Upper = null, Lower = null, MA = null;
            double[] AD = Indicators.AD(iBars.High, iBars.Low, iBars.Close, iBars.Volume);

            if (UseChannel)
            {
                var Lines = ChIsBands ? Indicators.BBands(AD, Period, 1.5) : Indicators.Extremes(AD, AD, Period);
                Upper = Indicators.Synchronize(Lines.Item1, iBars, Symbol.Bars);
                Lower = Indicators.Synchronize(Lines.Item2, iBars, Symbol.Bars);
            }
            else MA = Indicators.Synchronize(Indicators.EMA(AD, Period), iBars, Symbol.Bars);
            AD = Indicators.Synchronize(AD, iBars, Symbol.Bars);

            bool[] IsGrow = new bool[Symbol.Bars.Close.Length];
            for (int i = 2; i < Symbol.Bars.Close.Length; i++)
            {
                if (UseChannel)
                {
                    if (AD[i - 1] - Upper[i - 2] > 0.00001) IsGrow[i] = IsTrend;
                    else if (AD[i - 1] - Lower[i - 2] < -0.00001) IsGrow[i] = !IsTrend;
                    else IsGrow[i] = IsGrow[i - 1];
                }
                else
                {
                    if (AD[i - 1] - MA[i - 1] > 0.00001) IsGrow[i] = IsTrend;
                    else if (AD[i - 1] - MA[i - 1] < -0.00001) IsGrow[i] = !IsTrend;
                    else IsGrow[i] = IsGrow[i - 1];
                }
            }
            Result = new ScriptResult(ScriptType.OSC, IsGrow, new double[][] { AD, Upper, Lower, MA }, iBars.DateTime[^1], OnlyLimit);
        }
    }
}
