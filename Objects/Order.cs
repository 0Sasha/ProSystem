using System;

namespace ProSystem;

[Serializable]
public class Order
{
    private string State;

    public int TrID { get; set; } // Идентификатор транзакции сервера Transaq
    public long OrderNo { get; set; } // Биржевой номер заявки
    public string Seccode { get; set; } // Код инструмента
    public string Status
    {
        get => State;
        set { if (State != value) { State = value; DateTime = DateTime.Now; } }
    } // Статус заявки
    public DateTime DateTime { get; set; } // Дата последнего изменения статуса заявки
    public string BuySell { get; set; } // B - покупка, S - продажа
    public DateTime Time { get; set; } // Время регистрации заявки биржей
    public DateTime AcceptTime { get; set; } // Время регистрации заявки сервером Transaq (только для условных заявок)
    public double Price { get; set; } // Цена
    public int Balance { get; set; } // Неудовлетворенный остаток объема заявки в лотах (контрактах)
    public int Quantity { get; set; } // Количество
    public DateTime WithdrawTime { get; set; } // Время снятия заявки (0 для активных)

    public string Condition { get; set; } // Условие
    public double ConditionValue { get; set; } // Цена для условной заявки либо обеспеченность в процентах
    public DateTime ValidAfter { get; set; } // С какого момента времени действительна
    public DateTime ValidBefore { get; set; } // До какого момента действительна

    public string Sender { get; set; } // Отправитель заявки (название алгоритма)
    public string Signal { get; set; } // Цель заявки
    public string Note { get; set; } // Примечание заявки

    public Order() { }
    public Order(int TrID) { this.TrID = TrID; }
    public Order(int TrID, string Sender, string Signal, string Note)
    {
        this.TrID = TrID;
        this.Sender = Sender;
        this.Signal = Signal;
        this.Note = Note;
    }
}
