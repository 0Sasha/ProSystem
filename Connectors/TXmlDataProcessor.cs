using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading;
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

    private static bool Scheduled { get => DateTime.Now.TimeOfDay.TotalMinutes is 50 or 400; }

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

        if (xr.Name != "alltrades") ProcessSections(xr, xr.Name);
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
        else if (section is "sec_info" or "sec_info_upd") ProcessSecInfo(xr);
        else if (section == "securities") ProcessSecurities(xr);
        else if (section == "client") ProcessClients(xr);
        else if (section == "markets") ProcessMarkets(xr);
        else if (section == "candlekinds") ProcessTimeFrames(xr);
        else if (section == "error" && xr.Read()) AddInfo(xr.Value);
        else if (section == "messages" && xr.ReadToFollowing("text") && xr.Read())
            AddInfo(xr.Value, TradingSystem.Settings.DisplayMessages);
        else if (section is not "marketord" and not "pits" and not "boards" and not "union" and not "overnight" and not "news_header")
            AddInfo("ProcessData: unknown section: " + section);
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
        if (tool == null)
        {
            xr.Read();
            if (sec.Contains("USD")) Connector.USDRUB = double.Parse(xr.GetAttribute("close"), IC);
            else if (sec.Contains("EUR")) Connector.EURRUB = double.Parse(xr.GetAttribute("close"), IC);
            else AddInfo("ProcessBars: unknown asset: " + sec);
            return;
        }

        var security = tool.Security.Seccode == sec ? tool.Security : tool.BasicSecurity;
        var tf = Connector.TimeFrames.Single(x => x.ID == xr.GetAttribute("period")).Period / 60;
        ProcessBars(xr, tool, security, tf);
    }

    private void ProcessBars(XmlReader xr, Tool tool, Security security, int tf)
    {
        if (security.SourceBars == null || security.SourceBars.TF != tf) security.SourceBars = new Bars(tf);

        List<DateTime> dateTime = new();
        List<double> open = new();
        List<double> high = new();
        List<double> low = new();
        List<double> close = new();
        List<double> volume = new();

        bool filter = security.Market != "4";
        while (xr.Read())
        {
            if (filter && xr.HasAttributes && (dateTime.Count == 0 ||
                dateTime[^1].Date != DateTime.ParseExact(xr.GetAttribute("date"), DTForm, IC).Date) &&
                double.Parse(xr.GetAttribute("high"), IC) - double.Parse(xr.GetAttribute("low"), IC) < 0.00001)
                xr.Read();

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
                if (security.SourceBars.DateTime == null) // Исходные данные отсутсвуют
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
                else if (dateTime.Count < 2) return;
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
                        AddInfo("ProcessBars: Полученные данные слишком старые: " + security.ShortName + " LastBar: " + dateTime[^1]);
                        return;
                    }
                    security.SourceBars.DateTime = dateTime.Concat(security.SourceBars.DateTime).ToArray();
                    security.SourceBars.Open = open.Concat(security.SourceBars.Open).ToArray();
                    security.SourceBars.High = high.Concat(security.SourceBars.High).ToArray();
                    security.SourceBars.Low = low.Concat(security.SourceBars.Low).ToArray();
                    security.SourceBars.Close = close.Concat(security.SourceBars.Close).ToArray();
                    security.SourceBars.Volume = volume.Concat(security.SourceBars.Volume).ToArray();
                }
                else // Полученные данные располагаются внутри массива исходных данных
                {
                    // Поиск общих баров
                    int x = Array.FindIndex(security.SourceBars.DateTime, x => x == dateTime[0]);
                    int y = Array.FindIndex(security.SourceBars.DateTime, x => x == dateTime[^1]);

                    if (x > -1 && y > -1) // Найдены общие бары
                    {
                        var sourceInnerArr = security.SourceBars.DateTime[x..(y + 1)];
                        if (dateTime.Count != sourceInnerArr.Length)
                        {
                            AddInfo("ProcessBars: Массив полученных баров не соответствуют массиву исходных по количеству: " +
                                security.ShortName + " Исх/получ: " + sourceInnerArr.Length + "/" + dateTime.Count + " Период: " +
                                dateTime[0].Date + "-" + dateTime[^1].Date + " Возможно, требуется перезагрузка баров.", false);
                            return;
                        }
                        return; // Только анализ баров

                        /*security.SourceBars.DateTime =
                            security.SourceBars.DateTime[..x].Concat(dateTime.Concat(security.SourceBars.DateTime[(y + 1)..])).ToArray();

                        security.SourceBars.Open =
                            security.SourceBars.Open[..x].Concat(open.Concat(security.SourceBars.Open[(y + 1)..])).ToArray();

                        security.SourceBars.High =
                            security.SourceBars.High[..x].Concat(high.Concat(security.SourceBars.High[(y + 1)..])).ToArray();

                        security.SourceBars.Low =
                            security.SourceBars.Low[..x].Concat(low.Concat(security.SourceBars.Low[(y + 1)..])).ToArray();

                        security.SourceBars.Close =
                            security.SourceBars.Close[..x].Concat(close.Concat(security.SourceBars.Close[(y + 1)..])).ToArray();

                        security.SourceBars.Volume =
                            security.SourceBars.Volume[..x].Concat(volume.Concat(security.SourceBars.Volume[(y + 1)..])).ToArray();*/
                    }
                    else
                    {
                        AddInfo("ProcessBars: Не найдены общие бары полученных и исходных баров: " + security.ShortName, false);
                        return;
                    }
                }

                Task.Run(() => TradingSystem.ToolManager.UpdateBars(tool, security == tool.BasicSecurity));
                return;
            }
        }
        AddInfo("ProcessBars: Не найден EndElement");
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
            if (order == null) order = new Order(trID); // Создание новой заявки с данным TrID

            // Вторичная идентификация
            if (!xr.ReadToFollowing("orderno")) 
            {
                AddInfo("ProcessOrders: Не найден orderno заявки: " + order.TrID, notify: true);
                continue;
            }
            xr.Read();

            if (xr.Value == "0") order.OrderNo = 0;
            else if (orders.SingleOrDefault(x => x.OrderNo == long.Parse(xr.Value, IC)) == null)
                order.OrderNo = long.Parse(xr.Value, IC);
            else // Заявка с данным биржевым номером уже есть в коллекции, обновление TrID и её фиксация
            {
                orders.Single(x => x.OrderNo == long.Parse(xr.Value, IC)).TrID = trID;
                order = orders.Single(x => x.TrID == trID); // Фиксация существующей заявки
            }

            if (!xr.ReadToFollowing("seccode"))
            {
                AddInfo("orders: Не найден seccode заявки: " + order.TrID, notify: true);
                continue;
            }
            xr.Read();
            order.Seccode = xr.Value;

            if (!xr.ReadToFollowing("status"))
            {
                AddInfo("orders: Не найден status заявки: " + order.Seccode + "/" + order.TrID, notify: true);
                continue;
            }
            xr.Read();
            order.Status = xr.Value;

            if (!xr.ReadToFollowing("buysell"))
            {
                AddInfo("orders: Не найден buysell заявки: " + order.Seccode + "/" + order.TrID, notify: true);
                continue;
            }
            xr.Read();
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

            if (!xr.ReadToFollowing("balance"))
            {
                AddInfo("orders: Не найден balance заявки: " + order.Seccode + "/" + order.TrID, notify: true);
                continue;
            }
            xr.Read();
            order.Balance = int.Parse(xr.Value, IC);

            if (!xr.ReadToFollowing("price"))
            {
                AddInfo("orders: Не найдена price заявки: " + order.Seccode + "/" + order.TrID, notify: true);
                continue;
            }
            xr.Read();
            order.Price = double.Parse(xr.Value, IC);

            if (!xr.ReadToFollowing("quantity"))
            {
                AddInfo("orders: Не найдено quantity заявки: " + order.Seccode + "/" + order.TrID, notify: true);
                continue;
            }
            xr.Read();
            order.Quantity = int.Parse(xr.Value, IC);

            if (!xr.ReadToFollowing("withdrawtime"))
            {
                AddInfo("orders: Не найдено withdrawtime заявки: " + order.Seccode + "/" + order.TrID, notify: true);
                continue;
            }
            xr.Read();
            if (xr.Value != "0") order.WithdrawTime = DateTime.ParseExact(xr.Value, DTForm, IC);

            if (!xr.ReadToFollowing("condition"))
            {
                AddInfo("orders: Не найдено condition заявки: " + order.Seccode + "/" + order.TrID, notify: true);
                continue;
            }
            xr.Read();
            order.Condition = xr.Value;

            if (order.Condition != "None")
            {
                if (!xr.ReadToFollowing("conditionvalue"))
                {
                    AddInfo("orders: Не найдено conditionvalue заявки: " + order.Seccode + "/" + order.TrID, notify: true);
                    continue;
                }
                xr.Read();
                order.ConditionValue = double.Parse(xr.Value, IC);

                if (!xr.ReadToFollowing("validafter"))
                {
                    AddInfo("orders: Не найдено validafter заявки: " + order.Seccode + "/" + order.TrID, notify: true);
                    continue;
                }
                xr.Read();
                if (xr.Value != "0") order.ValidAfter = DateTime.ParseExact(xr.Value, DTForm, IC);

                if (!xr.ReadToFollowing("validbefore"))
                {
                    AddInfo("orders: Не найдено validbefore заявки: " + order.Seccode + "/" + order.TrID, notify: true);
                    continue;
                }
                xr.Read();
                if (xr.Value != "" && xr.Value != "0") order.ValidBefore = DateTime.ParseExact(xr.Value, DTForm, IC);
            }
            if (!xr.ReadToFollowing("result"))
            {
                AddInfo("orders: Не найден result заявки: " + order.Seccode + "/" + order.TrID, notify: true);
                continue;
            }
            xr.Read();
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
                if (!xr.ReadToFollowing("seccode")) return;
                xr.Read();

                var pos = portfolio.Positions.SingleOrDefault(x => x.Seccode == xr.Value);
                if (pos == null)
                {
                    pos = CreatePosition(xr.Value);
                    portfolio.Positions.Add(pos);
                }

                if (!xr.ReadToFollowing("market"))
                {
                    AddInfo("Не найден market позиции");
                    continue;
                }
                xr.Read();
                pos.Market = xr.Value;

                if (subsection == "forts_position")
                {
                    if (!xr.ReadToFollowing("startnet"))
                    {
                        AddInfo("Не найден startnet позиции");
                        continue;
                    }
                    xr.Read();
                    pos.SaldoIn = int.Parse(xr.Value, IC);

                    if (!xr.ReadToFollowing("totalnet"))
                    {
                        AddInfo("Не найден totalnet позиции");
                        continue;
                    }
                    xr.Read();
                    pos.Saldo = int.Parse(xr.Value, IC);

                    if (!xr.ReadToFollowing("varmargin"))
                    {
                        AddInfo("Не найдена varmargin позиции");
                        continue;
                    }
                    xr.Read();
                    pos.PL = double.Parse(xr.Value, IC);
                }
                else
                {
                    if (!xr.ReadToFollowing("saldoin"))
                    {
                        AddInfo("Не найдено saldoin позиции");
                        continue;
                    }
                    xr.Read();
                    pos.SaldoIn = int.Parse(xr.Value, IC);

                    if (!xr.ReadToFollowing("saldo"))
                    {
                        AddInfo("Не найдено saldo позиции");
                        continue;
                    }
                    xr.Read();
                    pos.Saldo = int.Parse(xr.Value, IC);

                    if (!xr.ReadToFollowing("amount"))
                    {
                        AddInfo("Не найдено amount позиции");
                        continue;
                    }
                    xr.Read();
                    pos.Amount = double.Parse(xr.Value, IC);

                    if (!xr.ReadToFollowing("equity"))
                    {
                        AddInfo("Не найдено equity позиции");
                        continue;
                    }
                    xr.Read();
                    pos.Equity = double.Parse(xr.Value, IC);
                }
            }
            else if (subsection == "united_limits")
            {
                if (!xr.ReadToFollowing("equity"))
                {
                    AddInfo("Нет equity портфеля.");
                    return;
                }
                xr.Read();
                portfolio.Saldo = double.Parse(xr.Value, IC);

                if (!xr.ReadToFollowing("requirements"))
                {
                    AddInfo("Нет requirements портфеля.");
                    return;
                }
                xr.Read();
                portfolio.InitReqs = double.Parse(xr.Value, IC);

                if (!xr.ReadToFollowing("free")) return;
                xr.Read();
                portfolio.Free = double.Parse(xr.Value, IC);

                if (!xr.ReadToFollowing("vm")) return;
                xr.Read();
                portfolio.VarMargin = double.Parse(xr.Value, IC);

                if (!xr.ReadToFollowing("finres")) return;
                xr.Read();
                portfolio.FinRes = double.Parse(xr.Value, IC);

                if (!xr.ReadToFollowing("go")) return;
                xr.Read();
                portfolio.GO = double.Parse(xr.Value, IC);
                return;
            }
            else if (subsection == "money_position")
            {
                if (!xr.ReadToFollowing("shortname")) return;
                xr.Read();

                Position pos = portfolio.MoneyPositions.SingleOrDefault(x => x.ShortName == xr.Value);
                if (pos == null)
                {
                    pos = new() { ShortName = xr.Value };
                    portfolio.MoneyPositions.Add(pos);
                }

                if (!xr.ReadToFollowing("saldoin"))
                {
                    AddInfo("Не найдено saldoin позиции");
                    continue;
                }
                xr.Read();
                pos.SaldoIn = double.Parse(xr.Value, IC);

                if (!xr.ReadToFollowing("saldo"))
                {
                    AddInfo("Не найдено saldo позиции");
                    continue;
                }
                xr.Read();
                pos.Saldo = double.Parse(xr.Value, IC);
            }
            else
            {
                AddInfo("Неизвестная позиция:" + subsection);
                return;
            }
        }
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

    private void ProcessTrades(XmlReader xr)
    {
        var trades = TradingSystem.Trades;
        while (xr.Read())
        {
            if (!xr.ReadToFollowing("tradeno")) return;
            xr.Read();

            if (trades.SingleOrDefault(x => x.TradeNo == long.Parse(xr.Value, IC)) != null) continue;
            var trade = new Trade(long.Parse(xr.Value, IC));

            if (!xr.ReadToFollowing("orderno"))
            {
                AddInfo("Нет orderno моей сделки.");
                continue;
            }
            xr.Read();
            trade.OrderNo = long.Parse(xr.Value, IC);

            if (!xr.ReadToFollowing("seccode"))
            {
                AddInfo("Нет seccode моей сделки.");
                continue;
            }
            xr.Read();
            trade.Seccode = xr.Value;

            if (!xr.ReadToFollowing("buysell"))
            {
                AddInfo("Нет buysell моей сделки.");
                continue;
            }
            xr.Read();
            trade.BuySell = xr.Value;

            if (!xr.ReadToFollowing("time"))
            {
                AddInfo("Нет time моей сделки.");
                continue;
            }
            xr.Read();
            trade.DateTime = DateTime.ParseExact(xr.Value, DTForm, IC);

            if (!xr.ReadToFollowing("price"))
            {
                AddInfo("Нет price моей сделки.");
                continue;
            }
            xr.Read();
            trade.Price = double.Parse(xr.Value, IC);

            if (!xr.ReadToFollowing("quantity"))
            {
                AddInfo("Нет quantity моей сделки.");
                continue;
            }
            xr.Read();
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
        if (xr.GetAttribute("connected") == "true")
        {
            Connector.ServerAvailable = true;
            if (xr.GetAttribute("recover") != "true")
            {
                Connector.Connection = ConnectionState.Connected;
                AddInfo("Connected", !Scheduled);
            }
            else
            {
                Connector.TriggerReconnection = DateTime.Now.AddSeconds(TradingSystem.Settings.SessionTM);
                Connector.Connection = ConnectionState.Connecting;
                AddInfo("Recover connection");
            }
        }
        else if (xr.GetAttribute("connected") == "false")
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
        else if (xr.GetAttribute("connected") == "error")
        {
            TradingSystem.ReadyToTrade = false;
            Connector.ServerAvailable = false;
            Connector.BackupServer = !Connector.BackupServer;

            Connector.Connection = ConnectionState.Disconnected;
            xr.Read();
            AddInfo("Server error: " + xr.Value + " BackupServer: " + !Connector.BackupServer, notify: true);
        }
    }

    private void ProcessPortfolio(XmlReader xr)
    {
        var portfolio = TradingSystem.Portfolio;

        portfolio.Union = xr.GetAttribute("union");
        if (!xr.ReadToFollowing("open_equity"))
        {
            AddInfo("Нет open_equity портфеля.");
            return;
        }
        xr.Read();
        portfolio.SaldoIn = double.Parse(xr.Value, IC);

        if (!xr.ReadToFollowing("equity"))
        {
            AddInfo("Нет equity портфеля.");
            return;
        }
        xr.Read();
        portfolio.Saldo = double.Parse(xr.Value, IC);

        if (!xr.ReadToFollowing("pl"))
        {
            AddInfo("Нет pl портфеля.");
            return;
        }
        xr.Read();
        portfolio.PL = double.Parse(xr.Value, IC);

        if (!xr.ReadToFollowing("init_req"))
        {
            AddInfo("Нет init_req портфеля.");
            return;
        }
        xr.Read();
        portfolio.InitReqs = double.Parse(xr.Value, IC);

        if (!xr.ReadToFollowing("maint_req"))
        {
            AddInfo("Нет maint_req портфеля.");
            return;
        }
        xr.Read();
        portfolio.MinReqs = double.Parse(xr.Value, IC);

        if (!xr.ReadToFollowing("unrealized_pnl"))
        {
            AddInfo("Нет unrealized_pnl портфеля.");
            return;
        }
        xr.Read();
        portfolio.UnrealPL = double.Parse(xr.Value, IC);

        while (xr.Read())
        {
            if (!xr.ReadToFollowing("seccode")) return;
            xr.Read();

            Position pos = portfolio.Positions.SingleOrDefault(x => x.Seccode == xr.Value);
            if (pos == null)
            {
                pos = CreatePosition(xr.Value);
                portfolio.Positions.Add(pos);
            }

            if (!xr.ReadToFollowing("open_balance"))
            {
                AddInfo("Не найден open_balance позиции");
                continue;
            }
            xr.Read();
            pos.SaldoIn = int.Parse(xr.Value, IC);

            if (!xr.ReadToFollowing("balance"))
            {
                AddInfo("Не найден balance позиции");
                continue;
            }
            xr.Read();
            pos.Saldo = int.Parse(xr.Value, IC);

            if (!xr.ReadToFollowing("pl"))
            {
                AddInfo("Нет pl позиции.");
                continue;
            }
            xr.Read();
            pos.PL = double.Parse(xr.Value, IC);
        }
    }

    private void ProcessSecInfo(XmlReader xr)
    {
        if (!xr.ReadToFollowing("seccode"))
        {
            AddInfo("ProcessSecInfoUpd: no seccode.");
            return;
        }
        xr.Read();

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
        if (!xr.ReadToFollowing("seccode"))
        {
            AddInfo("ProcessClientPermissions: no seccode");
            return;
        }
        xr.Read();

        var tool = TradingSystem.Tools.SingleOrDefault(x => x.Security.Seccode == xr.Value);
        if (tool == null)
        {
            AddInfo("ProcessClientPermissions: unknown tool: " + xr.Value);
            return;
        }

        if (!xr.ReadToFollowing("riskrate_long"))
        {
            AddInfo("sec_permissions: no riskrate_long");
            return;
        }
        xr.Read();
        tool.Security.RiskrateLong = double.Parse(xr.Value, IC);

        if (!xr.ReadToFollowing("reserate_long"))
        {
            AddInfo("sec_permissions: no reserate_long");
            return;
        }
        xr.Read();
        tool.Security.ReserateLong = double.Parse(xr.Value, IC);

        if (!xr.ReadToFollowing("riskrate_short"))
        {
            AddInfo("sec_permissions: no riskrate_short");
            return;
        }
        xr.Read();
        tool.Security.RiskrateShort = double.Parse(xr.Value, IC);

        if (!xr.ReadToFollowing("reserate_short"))
        {
            AddInfo("sec_permissions: no reserate_short");
            return;
        }
        xr.Read();
        tool.Security.ReserateShort = double.Parse(xr.Value, IC);

        Task.Run(tool.Security.UpdateRequirements);
    }


    private void ProcessSecurities(XmlReader xr)
    {
        while (xr.Read())
        {
            if (xr.Name != "security" && !xr.ReadToFollowing("security")) return;
            if (xr.GetAttribute("active") == "false") continue;

            if (!xr.ReadToFollowing("seccode"))
            {
                AddInfo("ProcessSecurities: no seccode.");
                continue;
            }
            xr.Read();
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

        if (!xr.ReadToFollowing("market"))
        {
            AddInfo("ProcessClients: no market");
            return;
        };
        xr.Read();
        market = xr.Value;

        if (!xr.ReadToFollowing("union"))
        {
            AddInfo("ProcessClients: no union");
            return;
        };
        xr.Read();
        Connector.Clients.Add(new(id, market, xr.Value));
    }

    private void ProcessTimeFrames(XmlReader xr)
    {
        while (xr.Read())
        {
            if (!xr.ReadToFollowing("id")) return;
            xr.Read();
            var id = xr.Value;

            if (!xr.ReadToFollowing("period"))
            {
                AddInfo("ProcessTimeFrames: no period");
                return;
            }
            xr.Read();
            var period = int.Parse(xr.Value, IC);

            if (!xr.ReadToFollowing("name"))
            {
                AddInfo("ProcessTimeFrames: no name");
                return;
            }
            xr.Read();
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
}
