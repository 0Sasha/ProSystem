using System;
using System.Windows.Controls;

namespace ProSystem.Algorithms
{
    [Serializable]
    internal class ATRS : IScript, System.ComponentModel.INotifyPropertyChanged
    {
        #region Fields
        private Order LastEx;
        private PositionType Pos;

        private int Per = 10;
        private int Mul = 0;
        private int PerEx = 1;
        private double Cor = 0;
        private int TF = 60;

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
        public int PeriodEx
        {
            get => PerEx;
            set { PerEx = value; NotifyChanged(); }
        }
        public double Correction
        {
            get => Cor;
            set { Cor = value; NotifyChanged(); }
        }
        public int IndicatorTF
        {
            get => TF;
            set { TF = value; NotifyChanged(); }
        }

        private void NotifyChanged() => PropertyChanged?.Invoke(this, new System.ComponentModel.PropertyChangedEventArgs(""));
        #endregion

        public ATRS(string Name) { this.Name = Name; }
        public override string ToString() => "ATRS";
        public void Initialize(Tool MyTool, TabItem TabTool)
        {
            bool IsOSC = false;
            string[] UpperProperties = new string[] { "Period", "Mult", "PeriodEx", "Correction", "IndicatorTF" };
            MyTool.InitializeScript(this, TabTool, IsOSC, UpperProperties);
        }
        public void Calculate(Security Symbol)
        {
            Bars iBars = Bars.Compress(Symbol.Bars, IndicatorTF);
            double[] StopATR = Indicators.ATRLine(iBars.High, iBars.Low, iBars.Close, Period, Mult, PeriodEx, Correction, Symbol.Decimals);
            StopATR = Indicators.Synchronize(StopATR, iBars, Symbol.Bars);

            double PastStopATR = 0;
            bool[] IsGrow = new bool[Symbol.Bars.Close.Length];
            for (int i = 1; i < Symbol.Bars.Close.Length; i++)
            {
                if (Math.Abs(PastStopATR - StopATR[i - 1]) > 0.00001 || PastStopATR < 0.00001)
                {
                    if (!IsGrow[i - 1] && Symbol.Bars.High[i] - StopATR[i - 1] > 0.00001 || IsGrow[i - 1] && Symbol.Bars.Low[i] - StopATR[i - 1] < -0.00001)
                    {
                        IsGrow[i] = !IsGrow[i - 1];
                        PastStopATR = StopATR[i - 1];
                    }
                    else IsGrow[i] = IsGrow[i - 1];
                }
                else IsGrow[i] = IsGrow[i - 1];
            }
            Result = new ScriptResult(ScriptType.StopLine, IsGrow, new double[][] { StopATR }, iBars.DateTime[^1]);
        }
    }
}
