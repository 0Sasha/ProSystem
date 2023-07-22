using System;

namespace ProSystem;

[Serializable]
public class Trade
{
    public long TradeNo { get; private set; } // Номер сделки на бирже
    public long OrderNo { get; set; } // Номер заявки на бирже
    public string Seccode { get; set; } // Код инструмента
    public string BuySell { get; set; } // B - покупка, S - продажа
    public DateTime DateTime { get; set; } // Дата и время
    public double Price { get; set; } // Цена
    public int Quantity { get; set; } // Количество

    public string SenderOrder { get; set; } // Отправитель заявки (название алгоритма)
    public string SignalOrder { get; set; } // Цель заявки
    public string NoteOrder { get; set; } // Примечание заявки

    public Trade() { }
    public Trade(long TradeNo) { this.TradeNo = TradeNo; }
    public Trade(DateTime DateTime) { this.DateTime = DateTime; }
    public Trade GetCopy() => (Trade)MemberwiseClone();
}