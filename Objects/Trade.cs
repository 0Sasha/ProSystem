using Newtonsoft.Json;

namespace ProSystem;

[Serializable]
public class Trade
{
    public long Id { get; set; }
    public long OrderId { get; set; }
    public string Seccode { get; set; }
    public string? Side { get; set; }
    public DateTime Time { get; set; }
    public double Price { get; set; }
    public double Quantity { get; set; }
    public double Commission { get; set; }

    public string? OrderSender { get; set; }
    public string? OrderSignal { get; set; }
    public string? OrderNote { get; set; }

    public Trade(string seccode, DateTime dateTime, double price, double quantity)
    {
        Seccode = seccode;
        Time = dateTime;
        Price = price;
        Quantity = quantity;
    }

    [JsonConstructor]
    public Trade(long id, long orderId, string seccode, string side,
        DateTime time, double price, double quantity, double commission)
    {
        Id = id;
        OrderId = orderId;
        Seccode = seccode;
        Side = side;
        Time = time;
        Price = price;
        Quantity = quantity;
        Commission = commission;
    }

    public Trade GetCopy() => (Trade)MemberwiseClone();
}
