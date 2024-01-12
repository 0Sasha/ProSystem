using Newtonsoft.Json;

namespace ProSystem;

[Serializable]
public class Order
{
    public long Id { get; set; }
    public int TrID { get; set; }
    public string Seccode { get; set; }
    public string? Type { get; set; }
    public OrderType InitType { get; set; }
    public string Status { get; set; }
    public DateTime ChangeTime { get; set; }
    public string Side { get; set; }
    public DateTime Time { get; set; }
    public double Price { get; set; }
    public double Balance { get; set; }
    public double Quantity { get; set; }

    public string? Sender { get; set; }
    public string? Signal { get; set; }
    public string? Note { get; set; }

    [JsonConstructor]
    public Order(long id, string seccode, string status, DateTime changeTime, string side)
    {
        Id = id;
        Seccode = seccode;
        Status = status;
        ChangeTime = changeTime;
        Side = side;
    }
}
