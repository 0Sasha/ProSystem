﻿using System;
using System.Threading;
using System.Windows;

namespace ProSystem;

[Serializable]
public class Security
{
    public string Seccode { get; private set; }
    public string Currency { get; set; } // Валюта номинала (не валюта расчётов)
    public string Board { get; set; }
    public string ShortName { get; set; }
    public string Market { get; set; }
    public int Decimals { get; set; } // Количество десятичных знаков в цене
    public double MinStep { get; set; } // Шаг цены
    public int LotSize { get; set; } // Размер лота

    public Trade LastTrade { get; set; } // Последняя сделка
    public DateTime LastTrDT { get; set; }
    public double InitReqLong { get; set; } // Начальные требования Long
    public double InitReqShort { get; set; } // Начальные требования Short
    public double PointCost { get; set; } // Стоимость пункта цены
    public double MinStepCost { get; set; } // Стоимость шага цены
    public string TradingStatus { get; set; } // Состояние торговой сессии по инструменту

    public double RiskrateLong { get; set; } // Единая ставка риска Long
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
    public Security(string seccode) => Seccode = seccode;
    public Security(string board, string seccode)
    {
        Board = board;
        Seccode = seccode;
    }

    public void UpdateRequirements()
    {
        if (Bars == null) throw new Exception("There is no bars to update requirements");

        LastTrade ??= new Trade()
        {
            Price = Bars.Close[^1],
            DateTime = Bars.DateTime[^1]
        };
        MinStepCost = PointCost * MinStep * Math.Pow(10, Decimals) / 100;
        var lastPrice = LastTrade.DateTime > Bars.DateTime[^1] ? LastTrade.Price : Bars.Close[^1];
        var value = lastPrice * MinStepCost / MinStep * LotSize / 100;

        InitReqLong = Math.Round((RiskrateLong + ReserateLong) * value, 2);
        InitReqShort = Math.Round((RiskrateShort + ReserateShort) * value, 2);
    }
}
