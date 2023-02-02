using System;
using System.Linq;
using System.Threading.Tasks;
using static ProSystem.MainWindow;
namespace ProSystem;

[Serializable]
public class Security
{
    private Trade lastTr;
    private DateTime lastTrDT;
    private double riskLong;

    public string Seccode { get; private set; }
    public string Currency { get; set; } // Валюта номинала (не валюта расчётов)
    public string Board { get; set; }
    public string ShortName { get; set; }
    public string Market { get; set; }
    public int Decimals { get; set; } // Количество десятичных знаков в цене
    public double MinStep { get; set; } // Шаг цены
    public int LotSize { get; set; } // Размер лота

    public Trade LastTrade
    {
        get => lastTr;
        set
        {
            lastTr = value;
            if (lastTr.DateTime < Bars.DateTime[^1].AddMinutes(Bars.TF))
            {
                Bars.Close[^1] = lastTr.Price;
                if (lastTr.Price > Bars.High[^1]) Bars.High[^1] = lastTr.Price;
                else if (lastTr.Price < Bars.Low[^1]) Bars.Low[^1] = lastTr.Price;
                Bars.Volume[^1] += lastTr.Quantity;
            }
            else if (DateTime.Now > lastTrDT) // Открытие нового бара
            {
                lastTrDT = DateTime.Now.AddSeconds(10);
                Tool MyTool = Tools
                    .Single(x => x.MySecurity.Seccode == Seccode || x.BasicSecurity != null && x.BasicSecurity.Seccode == Seccode);
                MyTool.TimeNextRecalc = DateTime.Now.AddSeconds(30);

                // Создание нового бара
                if (DateTime.Now.Date == Bars.DateTime[^1].Date) Bars.DateTime =
                        Bars.DateTime.Concat(new DateTime[] { Bars.DateTime[^1].AddMinutes(Bars.TF) }).ToArray();
                else Bars.DateTime =
                        Bars.DateTime.Concat(new DateTime[] { DateTime.Now.Date.AddHours(DateTime.Now.Hour) }).ToArray();
                Bars.Open = Bars.Open.Concat(new double[] { lastTr.Price }).ToArray();
                Bars.High = Bars.High.Concat(new double[] { lastTr.Price }).ToArray();
                Bars.Low = Bars.Low.Concat(new double[] { lastTr.Price }).ToArray();
                Bars.Close = Bars.Close.Concat(new double[] { lastTr.Price }).ToArray();
                Bars.Volume = Bars.Volume.Concat(new double[] { lastTr.Quantity }).ToArray();

                // Пересчёт скриптов и запрос оригинальных баров
                Task.Run(async () =>
                {
                    await Task.Delay(250);
                    Order LastExecuted = 
                    Orders.ToArray().LastOrDefault(x => x.Seccode == MyTool.MySecurity.Seccode && x.Status == "matched");

                    if (LastExecuted != null && LastExecuted.DateTime.AddSeconds(3) > DateTime.Now)
                    {
                        AddInfo(MyTool.Name + ": Заявка исполнилась одновременно с открытием бара. Ожидание.", false);
                        await Task.Delay(2000);
                    }
                    else if (MyTool.MySecurity.Seccode == Seccode)
                    {
                        Order[] Active =
                        Orders.ToArray().Where(x => x.Seccode == Seccode && (x.Status is "active" or "watching")).ToArray();
                        if (Active.Any(x => Math.Abs(x.Price - lastTr.Price) < 0.00001))
                        {
                            AddInfo(MyTool.Name + ": Цена активной заявки равна цене открытия бара. Ожидание.", false);
                            await Task.Delay(2000);
                        }
                    }

                    MyTool.Calculate(1.5);
                    MyTool.MainModel.InvalidatePlot(true);
                    MyTool.RequestBars();
                });
            }
        }
    } // Последняя сделка
    public double InitReqLong { get; set; } // Начальные требования Long
    public double InitReqShort { get; set; } // Начальные требования Short
    public double PointCost { get; set; } // Стоимость пункта цены
    public double MinStepCost { get; set; } // Стоимость шага цены
    public string TradingStatus { get; set; } // Состояние торговой сессии по инструменту

    public double RiskrateLong
    {
        get => riskLong;
        set
        {
            riskLong = value;
            Task.Run(async () =>
            {
                await Task.Delay(10000);
                UpdateRequirements();
            });
        }
    } // Единая ставка риска Long
    public double ReserateLong { get; set; } // Единая ставка резерва Long
    public double RiskrateShort { get; set; } // Единая ставка риска Short
    public double ReserateShort { get; set; } // Единая ставка резерва Short

    public double MinPrice { get; set; } // Минимальная цена (FORTS)
    public double MaxPrice { get; set; } // Максимальная цена (FORTS)
    public double BuyDeposit { get; set; } // ГО покупателя (FORTS)
    public double SellDeposit { get; set; } // ГО продавца (FORTS)

    public Bars Bars { get; set; } // Сжатые бары с базовым ТФ
    public Bars SourceBars { get; set; } // Бары с исходным ТФ, полученные с сервера

    public Security() { }
    public Security(string Seccode) { this.Seccode = Seccode; }

    private async void UpdateRequirements()
    {
        MinStepCost = PointCost * MinStep * Math.Pow(10, Decimals) / 100;
        if (Bars == null) await Task.Delay(5000);
        if (Bars != null)
        {
            LastTrade ??= new Trade()
            {
                Price = Bars.Close[^1],
                DateTime = Bars.DateTime[^1]
            };
            double LastPrice = LastTrade.DateTime > Bars.DateTime[^1] ? LastTrade.Price : Bars.Close[^1];
            double Value = LastPrice * MinStepCost / MinStep * LotSize / 100;

            InitReqLong = Math.Round((RiskrateLong + ReserateLong) * Value, 2);
            InitReqShort = Math.Round((RiskrateShort + ReserateShort) * Value, 2);
        }
        else AddInfo("Не удалось обновить требования, потому что нет баров.");
    }
}
