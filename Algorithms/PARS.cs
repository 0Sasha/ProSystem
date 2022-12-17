using System;
using System.Windows.Controls;

namespace ProSystem.Algorithms
{
    [Serializable]
    internal class PARS : IScript, System.ComponentModel.INotifyPropertyChanged
    {
        #region Fields
        private Order LastEx;
        private PositionType Pos;

        private double CA = 0.02;
        private double MCA = 0.2;
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

        public double CoefAccel
        {
            get { return CA; }
            set { CA = value; NotifyChanged(); }
        }
        public double MaxCoef
        {
            get { return MCA; }
            set { MCA = value; NotifyChanged(); }
        }
        public int IndicatorTF
        {
            get { return TF; }
            set { TF = value; NotifyChanged(); }
        }

        private void NotifyChanged() => PropertyChanged?.Invoke(this, new System.ComponentModel.PropertyChangedEventArgs(""));
        #endregion

        public PARS(string Name) { this.Name = Name; }
        public override string ToString() => "PARS";
        public void Initialize(Tool MyTool, TabItem TabTool)
        {
            bool IsOSC = false;
            string[] UpperProperties = new string[] { "CoefAccel", "MaxCoef", "IndicatorTF" };
            MyTool.InitializeScript(this, TabTool, IsOSC, UpperProperties);
        }
        public void Calculate(Security Symbol)
        {
            Bars iBars = Bars.Compress(Symbol.Bars, IndicatorTF);
            double[] PARStop = Indicators.PARLine(iBars.High, iBars.Low, CoefAccel, MaxCoef, Symbol.Decimals);
            PARStop = Indicators.Synchronize(PARStop, iBars, Symbol.Bars);

            // Вычисление индикаторов
            double PastPARStop = 0;
            bool[] IsGrow = new bool[Symbol.Bars.Close.Length];
            for (int i = 1; i < Symbol.Bars.Close.Length; i++)
            {
                if (Math.Abs(PastPARStop - PARStop[i - 1]) > 0.00001 || PastPARStop < 0.00001)
                {
                    if (!IsGrow[i - 1] && Symbol.Bars.High[i] - PARStop[i - 1] > 0.00001 || IsGrow[i - 1] && Symbol.Bars.Low[i] - PARStop[i - 1] < -0.00001)
                    {
                        IsGrow[i] = !IsGrow[i - 1];
                        PastPARStop = PARStop[i - 1];
                    }
                    else IsGrow[i] = IsGrow[i - 1];
                }
                else IsGrow[i] = IsGrow[i - 1];
            }
            Result = new ScriptResult(ScriptType.StopLine, IsGrow, new double[][] { PARStop }, iBars.DateTime[^1]);
        }
    }
}
