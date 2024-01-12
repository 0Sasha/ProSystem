using System.IO;
using System.Xml;

namespace ProSystem;

internal class TXmlDataProcessor : DataProcessor
{
    private readonly TXmlConnector Connector;
    private readonly XmlReaderSettings XS = new()
    {
        IgnoreWhitespace = true,
        ConformanceLevel = ConformanceLevel.Fragment,
        DtdProcessing = DtdProcessing.Parse
    };
    private readonly string DTForm = "dd.MM.yyyy HH:mm:ss";

    public TXmlDataProcessor(TXmlConnector connector, TradingSystem tradingSystem, AddInformation addInfo) :
        base(tradingSystem, addInfo) => Connector = connector;

    public void ProcessData(string data)
    {
        using var xr = XmlReader.Create(new StringReader(data), XS);
        xr.Read();
        if (xr.Name == "alltrades")
        {
            while (xr.Read())
            {
                if (!xr.ReadToFollowing("time")) break;
                xr.Read();
                var time = DateTime.ParseExact(xr.Value, DTForm, IC);

                if (!xr.ReadToFollowing("price")) throw new ArgumentException("No price");
                xr.Read();
                var price = double.Parse(xr.Value, IC);

                if (!xr.ReadToFollowing("quantity")) throw new ArgumentException("No quantity");
                xr.Read();
                var quantity = int.Parse(xr.Value, IC);

                if (!xr.ReadToFollowing("seccode")) throw new ArgumentException("No seccode");
                xr.Read();

                UpdateLastBar(new(xr.Value, time, price, quantity));
            }
        }
        else ProcessSections(xr, xr.Name);
    }

    private void ProcessSections(XmlReader xr, string section)
    {
        if (section == "candles") ProcessBars(xr);
        else if (section == "orders") ProcessOrders(xr);
        else if (section == "positions") ProcessPositions(xr);
        else if (section == "trades") ProcessTrades(xr);
        else if (section == "server_status") ProcessStatus(xr);
        else if (section == "mc_portfolio") ProcessPortfolio(xr);
        else if (section == "cln_sec_permissions") ProcessPermissions(xr);
        else if (section == "sec_info" || section == "sec_info_upd") ProcessSecInfo(xr);
        else if (section == "securities") ProcessSecurities(xr);
        else if (section == "client") ProcessClients(xr);
        else if (section == "markets") ProcessMarkets(xr);
        else if (section == "candlekinds") ProcessTimeFrames(xr);
        else if (section == "error" && xr.Read()) AddInfo(xr.Value);
        else if (section == "messages" && xr.ReadToFollowing("text") && xr.Read()) AddInfo(xr.Value, false);
        //else if (section is not "marketord" and not "pits" and not "boards" and not "union" 
        //    and not "overnight" and not "news_header") AddInfo("ProcessData: unknown section: " + section);
    }


    private void ProcessBars(XmlReader xr)
    {
        if (xr.GetAttribute("status") == "3")
        {
            AddInfo("ProcessBars: data is not available");
            return;
        }

        var sec = xr.GetAttribute("seccode");
        var tool = TradingSystem.Tools.ToArray()
            .SingleOrDefault(t => t.Security.Seccode == sec || t.BasicSecurity?.Seccode == sec);
        if (tool != null)
        {
            var security = tool.BasicSecurity;
            if (security == null || tool.Security.Seccode == sec) security = tool.Security;
            var tf = Connector.TimeFrames.Single(x => x.ID == xr.GetAttribute("period")).Minutes;
            ProcessBars(xr, tool, security, tf);
        }
        else
        {
            xr.Read();
            if (sec == "USD000UTSTOM") Connector.USDRUB = xr.GetDoubleAttribute("close");
            else if (sec == "EUR_RUB__TOM") Connector.EURRUB = xr.GetDoubleAttribute("close");
            else AddInfo("ProcessBars: unknown asset: " + sec);
        }
    }

    private void ProcessBars(XmlReader xr, Tool tool, Security security, int tf)
    {
        List<DateTime> dateTime = [];
        List<double> open = [], high = [],
            low = [], close = [], volume = [];

        bool filter = true;
        int startHour = 9;
        if (security.Market == "4") { }
        else if (security.Market == "1") startHour = 10;
        else if (security.Market == "15") startHour = 7;
        else filter = false;

        while (xr.Read())
        {
            if (filter && xr.HasAttributes && xr.GetDateTimeAttribute("date").Hour < startHour) continue;

            if (xr.HasAttributes)
            {
                dateTime.Add(xr.GetDateTimeAttribute("date"));
                open.Add(xr.GetDoubleAttribute("open"));
                high.Add(xr.GetDoubleAttribute("high"));
                low.Add(xr.GetDoubleAttribute("low"));
                close.Add(xr.GetDoubleAttribute("close"));
                volume.Add(xr.GetDoubleAttribute("volume"));
            }
            else if (xr.NodeType == XmlNodeType.EndElement)
            {
                if (dateTime.Count > 1)
                {
                    var newBars = new Bars([.. dateTime], [.. open],
                        [.. high], [.. low], [.. close], [.. volume], tf);
                    UpdateBars(security, newBars, tool.BaseTF);
                }
                return;
            }
        }
        AddInfo("ProcessBars: EndElement is not found");
    }

    private void ProcessOrders(XmlReader xr)
    {
        while (xr.Read())
        {
            if (!xr.HasAttributes && !xr.ReadToFollowing("order")) return;

            var trId = xr.GetIntAttribute("transactionid");
            var id = xr.GetNextLong("orderno");
            var seccode = xr.GetNextString("seccode");
            var status = xr.GetNextString("status");
            var side = xr.GetNextString("buysell");
            var newOrder = new Order(id, seccode, status, Connector.ServerTime, side) { TrID = trId };

            xr.Read();
            xr.Read();
            if (xr.Name == "time") newOrder.Time = xr.GetNextDateTime("time");
            else if (xr.Name == "accepttime") newOrder.Time = xr.GetNextDateTime("accepttime");

            newOrder.Balance = xr.GetNextDouble("balance");
            newOrder.Price = xr.GetNextDouble("price");
            newOrder.Quantity = xr.GetNextDouble("quantity");
            newOrder.Type = xr.GetNextString("condition");
            newOrder.InitType = newOrder.Type == "None" ? OrderType.Limit : OrderType.Conditional;

            var result = xr.GetNextString("result");
            if (result != string.Empty)
            {
                AddInfo(newOrder.Seccode + "/" + newOrder.TrID + ": OrderReply: " + result,
                    !result.StartsWith("{37}") && !result.StartsWith("{42}"));
            }

            var orders = TradingSystem.Orders.ToArray();
            if (orders.Where(x => x.TrID == newOrder.TrID).Count() > 1)
            {
                AddInfo("ProcessOrders: Extra orders with the same TrID are going to be deleted", notify: true);
                TradingSystem.Window.Dispatcher.Invoke(() =>
                {
                    while (orders.Where(x => x.TrID == newOrder.TrID).Count() > 1)
                    {
                        TradingSystem.Orders.Remove(orders.First(x => x.TrID == newOrder.TrID));
                        orders = [.. TradingSystem.Orders];
                    }
                });
            }

            Order? oldOrder = null;
            if (newOrder.Id != 0) oldOrder = orders.SingleOrDefault(x => x.Id == newOrder.Id);
            oldOrder ??= orders.SingleOrDefault(x => x.TrID == newOrder.TrID);

            if (oldOrder == null) TradingSystem.Window.Dispatcher.Invoke(() => TradingSystem.Orders.Add(newOrder));
            else
            {
                oldOrder.TrID = newOrder.TrID;
                oldOrder.Id = newOrder.Id;
                oldOrder.Seccode = newOrder.Seccode;
                oldOrder.Side = newOrder.Side;
                oldOrder.Time = newOrder.Time;
                oldOrder.Balance = newOrder.Balance;
                oldOrder.Price = newOrder.Price;
                oldOrder.Quantity = newOrder.Quantity;
                oldOrder.Type = newOrder.Type;
                if (oldOrder.Status != newOrder.Status)
                {
                    oldOrder.Status = newOrder.Status;
                    oldOrder.ChangeTime = newOrder.ChangeTime;
                }
            }
        }
    }

    private void ProcessPositions(XmlReader xr)
    {
        xr.Read();
        var subsection = xr.Name;
        var portfolio = TradingSystem.Portfolio;
        while (xr.Read())
        {
            if (subsection is "sec_position" or "forts_position")
            {
                if (!GoToTheValue(xr, "seccode", false)) return;

                var pos = portfolio.Positions.SingleOrDefault(x => x.Seccode == xr.Value);
                if (pos == null)
                {
                    pos = new(xr.Value)
                    {
                        ShortName = Connector.Securities.Single(x => x.Seccode == xr.Value).ShortName
                    };
                    portfolio.Positions.Add(pos);
                }

                if (subsection == "forts_position")
                {
                    if (!GoToTheValue(xr, "startnet")) continue;
                    pos.SaldoIn = int.Parse(xr.Value, IC);

                    if (!GoToTheValue(xr, "totalnet")) continue;
                    pos.Saldo = int.Parse(xr.Value, IC);

                    if (!GoToTheValue(xr, "varmargin")) continue;
                    pos.PL = double.Parse(xr.Value, IC);
                }
                else
                {
                    if (!GoToTheValue(xr, "saldoin")) continue;
                    pos.SaldoIn = int.Parse(xr.Value, IC);

                    if (!GoToTheValue(xr, "saldo")) continue;
                    pos.Saldo = int.Parse(xr.Value, IC);

                    //if (!GoToTheValue(xr, "amount")) continue;
                    //pos.Amount = double.Parse(xr.Value, IC);

                    //if (!GoToTheValue(xr, "equity")) continue;
                    //pos.Equity = double.Parse(xr.Value, IC);
                }
            }
            else if (subsection == "united_limits")
            {
                if (!GoToTheValue(xr, "equity")) continue;
                portfolio.Saldo = double.Parse(xr.Value, IC);

                if (!GoToTheValue(xr, "requirements")) continue;
                portfolio.InitReqs = double.Parse(xr.Value, IC);

                if (!GoToTheValue(xr, "free")) return;
                portfolio.Free = double.Parse(xr.Value, IC);

                if (!GoToTheValue(xr, "vm")) return;
                portfolio.VarMargin = double.Parse(xr.Value, IC);

                if (!GoToTheValue(xr, "finres")) return;
                portfolio.FinRes = double.Parse(xr.Value, IC);

                //if (!GoToTheValue(xr, "go")) return;
                //portfolio.GO = double.Parse(xr.Value, IC);
                return;
            }
            else if (subsection == "money_position")
            {
                if (!GoToTheValue(xr, "shortname", false)) return;

                var pos = portfolio.MoneyPositions.SingleOrDefault(x => x.ShortName == xr.Value);
                if (pos == null)
                {
                    pos = new(xr.Value) { ShortName = xr.Value };
                    portfolio.MoneyPositions.Add(pos);
                }

                if (!GoToTheValue(xr, "saldoin")) continue;
                pos.SaldoIn = double.Parse(xr.Value, IC);

                if (!GoToTheValue(xr, "saldo")) continue;
                pos.Saldo = double.Parse(xr.Value, IC);
            }
            else
            {
                AddInfo("Неизвестная позиция:" + subsection);
                return;
            }
        }
    }

    private void ProcessTrades(XmlReader xr)
    {
        var trades = TradingSystem.Trades;
        while (xr.Read())
        {
            if (!GoToTheValue(xr, "tradeno", false)) return;
            if (trades.SingleOrDefault(x => x.Id == long.Parse(xr.Value, IC)) != null) continue;
            var id = long.Parse(xr.Value, IC);

            if (!GoToTheValue(xr, "orderno")) continue;
            var orderId = long.Parse(xr.Value, IC);

            if (!GoToTheValue(xr, "seccode")) continue;
            var seccode = xr.Value;

            if (!GoToTheValue(xr, "buysell")) continue;
            var side = xr.Value;

            if (!GoToTheValue(xr, "time")) continue;
            var time = DateTime.ParseExact(xr.Value, DTForm, IC);

            if (!GoToTheValue(xr, "price")) continue;
            var price = double.Parse(xr.Value, IC);

            if (!GoToTheValue(xr, "quantity")) continue;
            var quantity = int.Parse(xr.Value, IC);

            var trade = new Trade(id, orderId, seccode, side, time, price, quantity, 0);
            TradingSystem.Window.Dispatcher.Invoke(() => trades.Add(trade));

            var display = TradingSystem.Settings.DisplayNewTrades && trades.Count > 1 &&
                (trades[^2].Seccode != trade.Seccode || trades[^2].Side != trade.Side ||
                trades[^2].Time < trade.Time.AddMinutes(-30));

            AddInfo("New trade: " + trade.Seccode + "/" +
                trade.Side + "/" + trade.Price + "/" + trade.Quantity, display);
        }
    }

    private void ProcessStatus(XmlReader xr)
    {
        var connected = xr.GetAttribute("connected");
        if (connected == "true")
        {
            Connector.ServerAvailable = true;
            if (xr.GetAttribute("recover") != "true")
            {
                Connector.Connection = ConnectionState.Connected;
                AddInfo("Connected", !Connector.ReconnectTime);
            }
            else
            {
                Connector.Connection = ConnectionState.Connecting;
                AddInfo("Recover connection");
            }
        }
        else if (connected == "false")
        {
            TradingSystem.ReadyToTrade = false;
            Connector.ServerAvailable = true;

            if (xr.GetAttribute("recover") != "true")
            {
                Connector.Connection = ConnectionState.Disconnected;
                AddInfo("Disconnected", !Connector.ReconnectTime);
            }
            else
            {
                Connector.Connection = ConnectionState.Connecting;
                AddInfo("Recover");
            }
        }
        else if (connected == "error")
        {
            TradingSystem.ReadyToTrade = false;
            Connector.ServerAvailable = false;
            Connector.BackupServer = !Connector.BackupServer;

            Connector.Connection = ConnectionState.Disconnected;
            xr.Read();
            AddInfo("Server error: " + xr.Value + " BackupServer: " + !Connector.BackupServer, notify: true);
        }
        else throw new ArgumentException("Unknown connected attribute");
    }

    private void ProcessPortfolio(XmlReader xr)
    {
        var portfolio = TradingSystem.Portfolio;
        portfolio.Union = xr.GetAttribute("union");

        if (!GoToTheValue(xr, "open_equity")) return;
        portfolio.SaldoIn = double.Parse(xr.Value, IC);

        if (!GoToTheValue(xr, "equity")) return;
        portfolio.Saldo = double.Parse(xr.Value, IC);

        if (!GoToTheValue(xr, "pl")) return;
        portfolio.PL = double.Parse(xr.Value, IC);

        if (!GoToTheValue(xr, "init_req")) return;
        portfolio.InitReqs = double.Parse(xr.Value, IC);

        if (!GoToTheValue(xr, "maint_req")) return;
        portfolio.MinReqs = double.Parse(xr.Value, IC);

        if (!GoToTheValue(xr, "unrealized_pnl")) return;
        portfolio.UnrealPL = double.Parse(xr.Value, IC);

        while (xr.Read())
        {
            if (!GoToTheValue(xr, "seccode", false)) return;
            var pos = portfolio.Positions.SingleOrDefault(x => x.Seccode == xr.Value);
            if (pos == null)
            {
                pos = new(xr.Value) { ShortName = Connector.Securities.Single(x => x.Seccode == xr.Value).ShortName };
                portfolio.Positions.Add(pos);
            }

            if (!GoToTheValue(xr, "open_balance")) continue;
            pos.SaldoIn = int.Parse(xr.Value, IC);

            if (!GoToTheValue(xr, "balance")) continue;
            pos.Saldo = int.Parse(xr.Value, IC);

            if (!GoToTheValue(xr, "pl")) continue;
            pos.PL = double.Parse(xr.Value, IC);
        }
    }

    private void ProcessSecInfo(XmlReader xr)
    {
        if (!GoToTheValue(xr, "seccode")) return;
        var tool = TradingSystem.Tools.SingleOrDefault(x => x.Security.Seccode == xr.Value);
        if (tool == null) return;

        var property = "";
        while (xr.Read())
        {
            if (xr.Name.Length > 0) property = xr.Name;
            else if (xr.HasValue)
            {
                if (property == "sell_deposit") tool.Security.Deposit = double.Parse(xr.Value, IC);
                else if (property == "minprice") tool.Security.MinPrice = double.Parse(xr.Value, IC);
                else if (property == "maxprice") tool.Security.MaxPrice = double.Parse(xr.Value, IC);
                else if (property == "point_cost")
                    tool.Security.TickCost = GetTickCost(double.Parse(xr.Value, IC), tool.Security);
            }
        }
    }

    private void ProcessPermissions(XmlReader xr)
    {
        if (!GoToTheValue(xr, "seccode")) return;
        var tool = TradingSystem.Tools.SingleOrDefault(x => x.Security.Seccode == xr.Value);
        if (tool == null)
        {
            AddInfo("ProcessPermissions: unknown tool: " + xr.Value);
            return;
        }

        if (!GoToTheValue(xr, "riskrate_long")) return;
        tool.Security.RiskrateLong = double.Parse(xr.Value, IC);

        if (!GoToTheValue(xr, "reserate_long")) return;
        tool.Security.RiskrateLong += double.Parse(xr.Value, IC);

        if (!GoToTheValue(xr, "riskrate_short")) return;
        tool.Security.RiskrateShort = double.Parse(xr.Value, IC);

        if (!GoToTheValue(xr, "reserate_short")) return;
        tool.Security.RiskrateShort += double.Parse(xr.Value, IC);

        Task.Run(() => UpdateRequirements(tool.Security));
    }


    private void ProcessSecurities(XmlReader xr)
    {
        while (xr.Read())
        {
            if (xr.Name != "security" && !xr.ReadToFollowing("security")) return;
            if (xr.GetAttribute("active") == "false") continue;

            var seccode = xr.GetNextString("seccode");
            while (xr.Read())
                if (xr.Name is "board" or "currency") break;

            var security = new Security(seccode)
            {
                Currency = xr.Name == "currency" ? xr.GetNextString("currency") : null,
                Board = xr.GetNextString("board"),
                ShortName = xr.GetNextString("shortname"),
                TickPrecision = xr.GetNextInt("decimals"),
                Market = xr.GetNextString("market"),
                TickSize = xr.GetNextDouble("minstep"),
                LotSize = xr.GetNextInt("lotsize")
            };

            security.TickCost = GetTickCost(xr.GetNextDouble("point_cost"), security);
            Connector.Securities.Add(security);
        }
    }

    private static double GetTickCost(double pointCost, Security security) =>
        pointCost * security.TickSize * Math.Pow(10, security.TickPrecision) / 100;

    private void ProcessClients(XmlReader xr)
    {
        string id, market;
        if (xr.GetAttribute("remove") == "false")
        {
            if (Connector.Clients.SingleOrDefault(x => x.ID == xr.GetAttribute("id")) == null)
                id = xr.GetStringAttribute("id");
            else
            {
                AddInfo("ProcessClients: client already exists");
                return;
            }
        }
        else
        {
            Connector.Clients.Remove(Connector.Clients.Single(x => x.ID == xr.GetAttribute("id")));
            return;
        }

        if (!GoToTheValue(xr, "market")) return;
        market = xr.Value;

        if (!GoToTheValue(xr, "union")) return;
        Connector.Clients.Add(new(id, market, xr.Value));
    }

    private void ProcessTimeFrames(XmlReader xr)
    {
        while (xr.Read())
        {
            if (!GoToTheValue(xr, "id", false)) return;
            var id = xr.Value;

            if (!GoToTheValue(xr, "period")) return;
            var period = int.Parse(xr.Value, IC) / 60;

            if (!GoToTheValue(xr, "name")) return;
            Connector.TimeFrames.Add(new TimeFrame(id, period, xr.Value));
        }
    }

    private void ProcessMarkets(XmlReader xr)
    {
        string id = "id";
        while (xr.Read())
        {
            if (xr.HasAttributes) id = xr.GetStringAttribute("id");
            else if (xr.HasValue) Connector.Markets.Add(new Market(id, xr.Value));
        }
    }


    private bool GoToTheValue(XmlReader xr, string element, bool inform = true)
    {
        if (!xr.ReadToFollowing(element))
        {
            if (inform) AddInfo("ProcessData: " + element + " is not found", notify: true);
            return false;
        }
        xr.Read();
        return true;
    }

    private static void UpdateRequirements(Security security)
    {
        for (int i = 0; security.Bars == null && i < 20; i++) Thread.Sleep(250);
        if (security.Bars == null) throw new ArgumentException("There is no bars");
        if (security.TickSize < 0.000001) throw new ArgumentException("TickSize is <= 0");
        if (security.TickCost < 0.000001) throw new ArgumentException("TickCost is <= 0");
        if (security.TickPrecision < -0.000001) throw new ArgumentException("TickPrecision is < 0");
        if (security.RiskrateLong < 0.000001) throw new ArgumentException("RiskrateLong is <= 0");
        if (security.RiskrateShort < 0.000001) throw new ArgumentException("RiskrateShort is <= 0");

        var lastPrice = security.LastTrade.Time > security.Bars.DateTime[^1] ?
            security.LastTrade.Price : security.Bars.Close[^1];
        var value = lastPrice * security.TickCost / security.TickSize * security.LotSize / 100;

        security.InitReqLong = Math.Round(security.RiskrateLong * value, 2);
        security.InitReqShort = Math.Round(security.RiskrateShort * value, 2);
    }
}
