using System;
using System.Windows.Controls;

namespace ProSystem.Algorithms
{
    [Serializable]
    internal class ROC : IScript, System.ComponentModel.INotifyPropertyChanged
    {
        #region Fields
        private Order LastEx;
        private PositionType Pos;

        private int Per = 20;
        private int Lev = 20;
        private int TF = 60;
        private bool IsTr = true;
        private bool OnlyLim = true;

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
        public int Level
        {
            get => Lev;
            set { Lev = value; NotifyChanged(); }
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

        public ROC(string Name) { this.Name = Name; }
        public override string ToString() => "ROC";
        public void Initialize(Tool MyTool, TabItem TabTool)
        {
            bool IsOSC = true;
            string[] UpperProperties = new string[] { "Period", "Level", "IndicatorTF" };
            string[] MiddleProperties = new string[] { "IsTrend", "OnlyLimit" };
            MyTool.InitializeScript(this, TabTool, IsOSC, UpperProperties, MiddleProperties);
        }
        public void Calculate(Security Symbol)
        {
            Bars iBars = Bars.Compress(Symbol.Bars, IndicatorTF);
            double[] ROC = Indicators.ROC(iBars.Close, Period);
            ROC = Indicators.Synchronize(ROC, iBars, Symbol.Bars);

            bool[] IsGrow = new bool[Symbol.Bars.Close.Length];
            for (int i = 1; i < Symbol.Bars.Close.Length; i++)
            {
                if (ROC[i - 1] - Level > 0.00001) IsGrow[i] = IsTrend;
                else if (ROC[i - 1] - -Level < -0.00001) IsGrow[i] = !IsTrend;
                else IsGrow[i] = IsGrow[i - 1];
            }
            Result = new ScriptResult(ScriptType.OSC, IsGrow, new double[][] { ROC }, iBars.DateTime[^1], 0, Level, OnlyLimit);
        }
    }
}
