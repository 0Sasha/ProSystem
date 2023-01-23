using System;
namespace ProSystem;

[Serializable]
public class ScriptResult
{
    public ScriptType Type { get; set; }
    public bool[] IsGrow { get; set; }
    public double[][] Indicators { get; set; }
    public DateTime iLastDT { get; set; }

    public int Centre { get; set; } = -1;
    public int Level { get; set; } = -1;
    public bool OnlyLimit { get; set; }

    public ScriptResult() { }
    public ScriptResult(ScriptType Type, bool[] IsGrow, double[][] Indicators, DateTime iLastDT)
    {
        this.Type = Type;
        this.IsGrow = IsGrow;
        this.Indicators = Indicators;
        this.iLastDT = iLastDT;
    }
    public ScriptResult(ScriptType Type, bool[] IsGrow, double[][] Indicators, DateTime iLastDT, bool OnlyLimit)
    {
        this.Type = Type;
        this.IsGrow = IsGrow;
        this.Indicators = Indicators;
        this.iLastDT = iLastDT;
        this.OnlyLimit = OnlyLimit;
    }
    public ScriptResult(ScriptType Type, bool[] IsGrow,
        double[][] Indicators, DateTime iLastDT, int Centre, int Level, bool OnlyLimit)
    {
        this.Type = Type;
        this.IsGrow = IsGrow;
        this.Indicators = Indicators;
        this.iLastDT = iLastDT;

        this.Centre = Centre;
        this.Level = Level;
        this.OnlyLimit = OnlyLimit;
    }
}

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
}

public class Market
{
    public string ID { get; private set; }
    public string Name { get; private set; }
    public Market(string ID, string Name) { this.ID = ID; this.Name = Name; }
}
public class TimeFrame
{
    public string ID { get; private set; }
    public int Period { get; private set; }
    public string Name { get; private set; }
    public TimeFrame(string ID, int Period, string Name) { this.ID = ID; this.Period = Period; this.Name = Name; }
}
public class ClientAccount
{
    public string ID { get; private set; }
    public string Market { get; set; }
    public string Union { get; set; }
    public ClientAccount(string ID) { this.ID = ID; }
}
