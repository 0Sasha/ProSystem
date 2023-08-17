using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Xml;

namespace ProSystem;

internal class TXmlDataProcessor
{
    private readonly TXmlConnector Connector;
    private readonly TradingSystem TradingSystem;
    private readonly AddInformation AddInfo;

    private readonly XmlReaderSettings XS = new()
    {
        IgnoreWhitespace = true,
        ConformanceLevel = ConformanceLevel.Fragment,
        DtdProcessing = DtdProcessing.Parse
    };
    private readonly string DTForm = "dd.MM.yyyy HH:mm:ss";
    private readonly StringComparison SC = StringComparison.Ordinal;
    private readonly CultureInfo IC = CultureInfo.InvariantCulture;

    private static bool Scheduled { get => (int)DateTime.Now.TimeOfDay.TotalMinutes is 50 or 400; }

    public TXmlDataProcessor(TXmlConnector connector, TradingSystem tradingSystem, AddInformation addInfo)
    {
        Connector = connector ?? throw new ArgumentNullException(nameof(connector));
        TradingSystem = tradingSystem ?? throw new ArgumentNullException(nameof(tradingSystem));
        AddInfo = addInfo ?? throw new ArgumentNullException(nameof(addInfo));
    }

    public void ProcessData(string data)
    {
        using XmlReader xr = XmlReader.Create(new StringReader(data), XS);
        xr.Read();
        if (xr.Name == "alltrades")
        {
            while (xr.Read())
            {
                if (!xr.ReadToFollowing("time")) break;
                xr.Read();
                var trade = new Trade(DateTime.ParseExact(xr.Value, DTForm, IC));

                if (!xr.ReadToFollowing("price"))
                {
                    AddInfo("alltrades: no price");
                    continue;
                }
                xr.Read();
                trade.Price = double.Parse(xr.Value, IC);

                if (!xr.ReadToFollowing("quantity"))
                {
                    AddInfo("alltrades: no quantity");
                    continue;
                }
                xr.Read();
                trade.Quantity = int.Parse(xr.Value, IC);

                if (!xr.ReadToFollowing("seccode"))
                {
                    AddInfo("alltrades: no seccode");
                    continue;
                }
                xr.Read();
                trade.Seccode = xr.Value;

                var tool = TradingSystem.Tools
                    .Single(x => x.Security.Seccode == trade.Seccode || x.BasicSecurity?.Seccode == trade.Seccode);
                TradingSystem.ToolManager.UpdateLastTrade(tool, trade);
            }
        }
        else ProcessSections(xr, xr.Name);
        xr.Close();
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
        else if (section == "messages" && xr.ReadToFollowing("text") && xr.Read())
            AddInfo(xr.Value, TradingSystem.Settings.DisplayMessages);
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
            var security = tool.Security.Seccode == sec ? tool.Security : tool.BasicSecurity;
            var tf = Connector.TimeFrames.Single(x => x.ID == xr.GetAttribute("period")).Period / 60;
            ProcessBars(xr, tool, security, tf);
        }
        else
        {
            xr.Read();
            if (sec == "USD000UTSTOM") Connector.USDRUB = double.Parse(xr.GetAttribute("close"), IC);
            else if (sec == "EUR_RUB__TOM") Connector.EURRUB = double.Parse(xr.GetAttribute("close"), IC);
            else AddInfo("ProcessBars: unknown asset: " + sec);
        }
    }

    private void ProcessBars(XmlReader xr, Tool tool, Security security, int tf)
    {
        if (security.SourceBars == null || security.SourceBars.TF != tf) security.SourceBars = new Bars(tf);

        List<DateTime> dateTime = new();
        List<double> open = new(), high = new(),
            low = new(), close = new(), volume = new();

        bool filter = true;
        int startHour = 9;
        if (security.Market == "4") { }
        else if (security.Market == "1") startHour = 10;
        else if (security.Market == "15") startHour = 7;
        else filter = false;

        while (xr.Read())
        {
            if (filter && xr.HasAttributes &&
                DateTime.ParseExact(xr.GetAttribute("date"), DTForm, IC).Hour < startHour) continue;

            if (xr.HasAttributes)
            {
                dateTime.Add(DateTime.ParseExact(xr.GetAttribute("date"), DTForm, IC));
                open.Add(double.Parse(xr.GetAttribute("open"), IC));
                high.Add(double.Parse(xr.GetAttribute("high"), IC));
                low.Add(double.Parse(xr.GetAttribute("low"), IC));
                close.Add(double.Parse(xr.GetAttribute("close"), IC));
                volume.Add(double.Parse(xr.GetAttribute("volume"), IC));
            }
            else if (xr.NodeType == XmlNodeType.EndElement)
            {
                if (dateTime.Count < 2) return;
                if (security.SourceBars.DateTime == null)
                {
                    security.SourceBars = new Bars(tf)
                    {
                        DateTime = dateTime.ToArray(),
                        Open = open.ToArray(),
                        High = high.ToArray(),
                        Low = low.ToArray(),
                        Close = close.ToArray(),
                        Volume = volume.ToArray()
                    };
                }
                else if (dateTime[^1] >= security.SourceBars.DateTime[^1]) // Полученные данные свежее исходных
                {
                    // Поиск первого общего бара
                    int y = Array.FindIndex(security.SourceBars.DateTime, x => x == dateTime[0]);
                    if (y == -1) y = Array.FindIndex(security.SourceBars.DateTime, x => x == dateTime[1]);

                    if (y > -1) // Есть общие бары
                    {
                        security.SourceBars.DateTime = security.SourceBars.DateTime[..y].Concat(dateTime).ToArray();
                        security.SourceBars.Open = security.SourceBars.Open[..y].Concat(open).ToArray();
                        security.SourceBars.High = security.SourceBars.High[..y].Concat(high).ToArray();
                        security.SourceBars.Low = security.SourceBars.Low[..y].Concat(low).ToArray();
                        security.SourceBars.Close = security.SourceBars.Close[..y].Concat(close).ToArray();
                        security.SourceBars.Volume = security.SourceBars.Volume[..y].Concat(volume).ToArray();
                    }
                    else security.SourceBars = new Bars(tf) // Отсутствует общий бар
                    {
                        DateTime = dateTime.ToArray(),
                        Open = open.ToArray(),
                        High = high.ToArray(),
                        Low = low.ToArray(),
                        Close = close.ToArray(),
                        Volume = volume.ToArray()
                    };
                }
                else if (dateTime[^1] < security.SourceBars.DateTime[0]) // Полученные данные глубже исходных
                {
                    if (dateTime[^1].AddDays(5) < security.SourceBars.DateTime[0])
                    {
                        AddInfo("ProcessBars: received bars are too deep: " + security.ShortName);
                        return;
                    }
                    security.SourceBars.DateTime = dateTime.Concat(security.SourceBars.DateTime).ToArray();
                    security.SourceBars.Open = open.Concat(security.SourceBars.Open).ToArray();
                    security.SourceBars.High = high.Concat(security.SourceBars.High).ToArray();
                    security.SourceBars.Low = low.Concat(security.SourceBars.Low).ToArray();
                    security.SourceBars.Close = close.Concat(security.SourceBars.Close).ToArray();
                    security.SourceBars.Volume = volume.Concat(security.SourceBars.Volume).ToArray();
                }
                else if (dateTime[0] < security.SourceBars.DateTime[0])
                {
                    int count = dateTime.FindIndex(d => d == security.SourceBars.DateTime[0]);
                    security.SourceBars.DateTime =
                        dateTime.Take(count).Concat(security.SourceBars.DateTime).ToArray();
                    security.SourceBars.Open =
                        open.Take(count).Concat(security.SourceBars.Open).ToArray();
                    security.SourceBars.High =
                        high.Take(count).Concat(security.SourceBars.High).ToArray();
                    security.SourceBars.Low =
                        low.Take(count).Concat(security.SourceBars.Low).ToArray();
                    security.SourceBars.Close =
                        close.Take(count).Concat(security.SourceBars.Close).ToArray();
                    security.SourceBars.Volume =
                        volume.Take(count).Concat(security.SourceBars.Volume).ToArray();
                }
                else return;

                Task.Run(() => TradingSystem.ToolManager.UpdateBars(tool, security == tool.BasicSecurity));
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

            // Первичная идентификация
            int trID = int.Parse(xr.GetAttribute("transactionid"), IC);
            var orders = TradingSystem.Orders.ToArray();
            if (orders.Where(x => x.TrID == trID).Count() > 1)
            {
                AddInfo("ProcessOrders: Найдено несколько заявок с одинаковым TrID. Удаление лишних.", notify: true);
                TradingSystem.Window.Dispatcher.Invoke(() =>
                {
                    while (orders.Where(x => x.TrID == trID).Count() > 1)
                    {
                        TradingSystem.Orders.Remove(orders.First(x => x.TrID == trID));
                        orders = TradingSystem.Orders.ToArray();
                    }
                });
            }

            var order = orders.SingleOrDefault(x => x.TrID == trID); // Проверка наличия заявки с данным TrID
            order ??= new Order(trID); // Создание новой заявки с данным TrID

            // Вторичная идентификация
            if (!GoToTheValue(xr, "orderno")) continue;
            if (xr.Value == "0") order.OrderNo = 0;
            else if (orders.SingleOrDefault(x => x.OrderNo == long.Parse(xr.Value, IC)) == null)
                order.OrderNo = long.Parse(xr.Value, IC);
            else // Заявка с данным биржевым номером уже есть в коллекции, обновление TrID и её фиксация
            {
                orders.Single(x => x.OrderNo == long.Parse(xr.Value, IC)).TrID = trID;
                order = orders.Single(x => x.TrID == trID); // Фиксация существующей заявки
            }

            if (!GoToTheValue(xr, "seccode")) continue;
            order.Seccode = xr.Value;

            if (!GoToTheValue(xr, "status")) continue;
            order.Status = xr.Value;

            if (!GoToTheValue(xr, "buysell")) continue;
            order.BuySell = xr.Value;

            xr.Read();
            xr.Read();
            if (xr.Name == "time")
            {
                xr.Read();
                order.Time = DateTime.ParseExact(xr.Value, DTForm, IC);
                xr.Read();
                xr.Read();
                if (xr.Name == "accepttime")
                {
                    xr.Read();
                    order.AcceptTime = DateTime.ParseExact(xr.Value, DTForm, IC);
                }
            }
            else if (xr.Name == "accepttime")
            {
                xr.Read();
                order.AcceptTime = DateTime.ParseExact(xr.Value, DTForm, IC);
            }
            else if (order.Status == "active" && order.OrderNo == 0) { }

            if (!GoToTheValue(xr, "balance")) continue;
            order.Balance = int.Parse(xr.Value, IC);

            if (!GoToTheValue(xr, "price")) continue;
            order.Price = double.Parse(xr.Value, IC);

            if (!GoToTheValue(xr, "quantity")) continue;
            order.Quantity = int.Parse(xr.Value, IC);

            if (!GoToTheValue(xr, "withdrawtime")) continue;
            if (xr.Value != "0") order.WithdrawTime = DateTime.ParseExact(xr.Value, DTForm, IC);

            if (!GoToTheValue(xr, "condition")) continue;
            order.Condition = xr.Value;
            if (order.Condition != "None")
            {
                if (!GoToTheValue(xr, "conditionvalue")) continue;
                order.ConditionValue = double.Parse(xr.Value, IC);

                if (!GoToTheValue(xr, "validafter")) continue;
                if (xr.Value != "0") order.ValidAfter = DateTime.ParseExact(xr.Value, DTForm, IC);

                if (!GoToTheValue(xr, "validbefore")) continue;
                if (xr.Value != "" && xr.Value != "0") order.ValidBefore = DateTime.ParseExact(xr.Value, DTForm, IC);
            }

            if (!GoToTheValue(xr, "result")) continue;
            if (xr.HasValue)
            {
                if (xr.Value.StartsWith("{37}", SC) || xr.Value.StartsWith("{42}", SC))
                    AddInfo(order.Seccode + "/" + order.TrID + ": OrderReply: " + xr.Value, false);
                else AddInfo(order.Seccode + "/" + order.TrID + ": OrderReply: " + xr.Value);
            }

            int i = Array.FindIndex(TradingSystem.Orders.ToArray(), x => x.TrID == order.TrID);
            TradingSystem.Window.Dispatcher.Invoke(() =>
            {
                if (i > -1) TradingSystem.Orders[i] = order;
                else TradingSystem.Orders.Add(order);
            });
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
                    pos = CreatePosition(xr.Value);
                    portfolio.Positions.Add(pos);
                }

                if (!GoToTheValue(xr, "market")) continue;
                pos.Market = xr.Value;

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

                    if (!GoToTheValue(xr, "amount")) continue;
                    pos.Amount = double.Parse(xr.Value, IC);

                    if (!GoToTheValue(xr, "equity")) continue;
                    pos.Equity = double.Parse(xr.Value, IC);
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

                if (!GoToTheValue(xr, "go")) return;
                portfolio.GO = double.Parse(xr.Value, IC);
                return;
            }
            else if (subsection == "money_position")
            {
                if (!GoToTheValue(xr, "shortname", false)) return;

                var pos = portfolio.MoneyPositions.SingleOrDefault(x => x.ShortName == xr.Value);
                if (pos == null)
                {
                    pos = new() { ShortName = xr.Value };
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
            if (trades.SingleOrDefault(x => x.TradeNo == long.Parse(xr.Value, IC)) != null) continue;
            var trade = new Trade(long.Parse(xr.Value, IC));

            if (!GoToTheValue(xr, "orderno")) continue;
            trade.OrderNo = long.Parse(xr.Value, IC);

            if (!GoToTheValue(xr, "seccode")) continue;
            trade.Seccode = xr.Value;

            if (!GoToTheValue(xr, "buysell")) continue;
            trade.BuySell = xr.Value;

            if (!GoToTheValue(xr, "time")) continue;
            trade.DateTime = DateTime.ParseExact(xr.Value, DTForm, IC);

            if (!GoToTheValue(xr, "price")) continue;
            trade.Price = double.Parse(xr.Value, IC);

            if (!GoToTheValue(xr, "quantity")) continue;
            trade.Quantity = int.Parse(xr.Value, IC);

            TradingSystem.Window.Dispatcher.Invoke(() => trades.Add(trade));
            var display = TradingSystem.Settings.DisplayNewTrades && trades.Count > 1 &&
                (trades[^2].Seccode != trade.Seccode || trades[^2].BuySell != trade.BuySell ||
                trades[^2].DateTime < trade.DateTime.AddMinutes(-30));

            AddInfo("New trade: " + trade.Seccode + "/" +
                trade.BuySell + "/" + trade.Price + "/" + trade.Quantity, display);
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
                AddInfo("Connected", !Scheduled);
            }
            else
            {
                Connector.ReconnectionTrigger = DateTime.Now.AddSeconds(TradingSystem.Settings.SessionTM);
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
                AddInfo("Disconnected", !Scheduled);
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
                pos = CreatePosition(xr.Value);
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
                if (property == "buy_deposit") tool.Security.BuyDeposit = double.Parse(xr.Value, IC);
                else if (property == "sell_deposit") tool.Security.SellDeposit = double.Parse(xr.Value, IC);
                else if (property == "minprice") tool.Security.MinPrice = double.Parse(xr.Value, IC);
                else if (property == "maxprice") tool.Security.MaxPrice = double.Parse(xr.Value, IC);
                else if (property == "point_cost") tool.Security.PointCost = double.Parse(xr.Value, IC);
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
        tool.Security.ReserateLong = double.Parse(xr.Value, IC);

        if (!GoToTheValue(xr, "riskrate_short")) return;
        tool.Security.RiskrateShort = double.Parse(xr.Value, IC);

        if (!GoToTheValue(xr, "reserate_short")) return;
        tool.Security.ReserateShort = double.Parse(xr.Value, IC);

        Task.Run(tool.Security.UpdateRequirements);
    }


    private void ProcessSecurities(XmlReader xr)
    {
        while (xr.Read())
        {
            if (xr.Name != "security" && !xr.ReadToFollowing("security")) return;
            if (xr.GetAttribute("active") == "false") continue;

            if (!GoToTheValue(xr, "seccode")) continue;
            Connector.Securities.Add(new Security(xr.Value));

            var name = "";
            while (xr.Read())
            {
                if (xr.NodeType == XmlNodeType.EndElement)
                {
                    if (xr.Name == "security") break;
                    continue;
                }
                if (xr.NodeType == XmlNodeType.Element)
                {
                    name = xr.Name;
                    continue;
                }

                if (name == "currency") Connector.Securities[^1].Currency = xr.Value;
                else if (name == "board") Connector.Securities[^1].Board = xr.Value;
                else if (name == "shortname") Connector.Securities[^1].ShortName = xr.Value;
                else if (name == "decimals") Connector.Securities[^1].Decimals = int.Parse(xr.Value, IC);
                else if (name == "market") Connector.Securities[^1].Market = xr.Value;
                else if (name == "minstep") Connector.Securities[^1].MinStep = double.Parse(xr.Value, IC);
                else if (name == "lotsize") Connector.Securities[^1].LotSize = int.Parse(xr.Value, IC);
                else if (name == "point_cost") Connector.Securities[^1].PointCost = double.Parse(xr.Value, IC);
            }
        }
    }

    private void ProcessClients(XmlReader xr)
    {
        string id, market;
        if (xr.GetAttribute("remove") == "false")
        {
            if (Connector.Clients.SingleOrDefault(x => x.ID == xr.GetAttribute("id")) == null)
                id = xr.GetAttribute("id");
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
            var period = int.Parse(xr.Value, IC);

            if (!GoToTheValue(xr, "name")) return;
            Connector.TimeFrames.Add(new TimeFrame(id, period, xr.Value));
        }
    }

    private void ProcessMarkets(XmlReader xr)
    {
        string id = null;
        while (xr.Read())
        {
            if (xr.HasAttributes) id = xr.GetAttribute("id");
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

    private Position CreatePosition(string seccode)
    {
        var sec = Connector.Securities.SingleOrDefault(x => x.Seccode == seccode);
        if (sec != null)
        {
            var market = Connector.Markets.SingleOrDefault(x => x.ID == sec.Market);
            if (market != null) return new(seccode, sec.ShortName, sec.Market, market.Name);
            else throw new ArgumentException("Market is not found", nameof(seccode));
        }
        else throw new ArgumentException("Security with this seccode is not found", nameof(seccode));
    }
}
